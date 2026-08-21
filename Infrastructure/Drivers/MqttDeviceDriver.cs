using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Core.Acquisition;
using Core.DTOs;
using Core.Enums;
using Core.Interface;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Protocol;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Infrastructure.Drivers
{
	/// <summary>
	/// Driver MQTT — pola DORONG. Koneksi dibuka sekali saat perangkat diaktifkan dan hidup
	/// selama itu; broker mendorong pesan kapan pun perangkat mengirim.
	///
	/// Tiga setelan menentukan apakah janji anti-kehilangan MQTT benar-benar berlaku, dan
	/// ketiganya harus benar BERSAMAAN:
	///
	/// <list type="number">
	/// <item><b>ClientId tetap</b> per perangkat. Inilah yang membuat broker mengenali sesi
	/// yang sama saat gateway kembali. Form perangkat lama menghasilkan ClientId acak
	/// (<c>Guid.NewGuid()</c>), sehingga broker membuat sesi baru setiap sambung — antrean
	/// tertahan milik sesi lama tidak pernah dikirimkan, dan seluruh mekanismenya mati tanpa
	/// satu pun gejala.</item>
	/// <item><b>CleanStart = false</b>, supaya sesi sebelumnya dilanjutkan.</item>
	/// <item><b>QoS 1 + SessionExpiry ≥ durasi outage terburuk</b>. QoS 1 berarti "minimal
	/// sekali", jadi duplikat mungkin — dan itu sudah ditanggung kunci idempoten
	/// <c>(tag_id, source_ts)</c> di historian. QoS 2 membayar dua kali round-trip untuk
	/// jaminan yang sudah kita punya.</item>
	/// </list>
	/// </summary>
	public sealed class MqttDeviceDriver : IDeviceDriver
	{
		private readonly MqttConfig _config;
		private readonly ILogger<MqttDeviceDriver> _logger;
		private readonly IMqttClient _client;
		private readonly TimeSpan _sessionExpiry;

		/// <summary>
		/// Indeks topik → tag. Dibangun sekali saat langganan dipasang; mencocokkan setiap
		/// pesan dengan iterasi seluruh tag akan menjadi O(pesan × tag), dan pada broker yang
		/// mengalir cepat itu terasa.
		/// </summary>
		private readonly ConcurrentDictionary<string, List<TagPlan>> _byTopic = new(StringComparer.Ordinal);

		private IReadOnlyList<TagPlan> _tags = Array.Empty<TagPlan>();

		/// <summary>Nilai terakhir per tag. Driver dorong tetap harus bisa menjawab
		/// <see cref="ReadAsync"/> supaya scan class bisa mengatur laju SIMPAN tanpa
		/// membatasi laju TERIMA.</summary>
		private readonly ConcurrentDictionary<Guid, TagSample> _latest = new();

		private Func<TagSample, CancellationToken, Task>? _onSample;
		private DriverHealth _health;

		public Protocol Protocol => Protocol.MQTT;
		public Guid DeviceId { get; }
		public DriverHealth Health => _health;

		public MqttDeviceDriver(DevicePlan plan, ILogger<MqttDeviceDriver> logger)
		{
			DeviceId = plan.DeviceId;
			_logger = logger;

			_config = System.Text.Json.JsonSerializer.Deserialize<MqttConfig>(
				plan.ConnectionConfigJson,
				new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
				?? throw new ArgumentException("Konfigurasi MQTT tidak bisa dibaca", nameof(plan));

			_sessionExpiry = TimeSpan.FromHours(24);
			_client = new MqttClientFactory().CreateMqttClient();

			_client.ApplicationMessageReceivedAsync += OnMessageAsync;

			_client.DisconnectedAsync += args =>
			{
				_health = _health with
				{
					IsConnected = false,
					LastError = args.Exception?.Message ?? args.Reason.ToString(),
					ConsecutiveFailures = _health.ConsecutiveFailures + 1
				};
				_logger.LogWarning("MQTT {DeviceId} terputus: {Reason}", DeviceId, args.Reason);
				return Task.CompletedTask;
			};

			_client.ConnectedAsync += async _ =>
			{
				_health = new DriverHealth
				{
					IsConnected = true,
					LastSuccessAt = DateTime.UtcNow,
					ConsecutiveFailures = 0
				};
				_logger.LogInformation("MQTT {DeviceId} tersambung ke {Broker}:{Port}",
					DeviceId, _config.BrokerUrl, _config.Port);

				// Langganan didaftarkan ULANG setiap kali tersambung. Broker melupakan langganan
				// saat sesi berakhir, dan tanpa pendaftaran ulang stream diam-diam tidak pernah
				// kembali setelah reconnect — gejalanya "data berhenti tanpa error".
				await ResubscribeAsync();
			};
		}

		/// <summary>ClientId yang stabil dan dapat diprediksi, diturunkan dari id perangkat.</summary>
		private string StableClientId =>
			string.IsNullOrWhiteSpace(_config.ClientId) || Guid.TryParse(_config.ClientId, out _)
				? $"synapse-{DeviceId:N}"[..Math.Min(32, $"synapse-{DeviceId:N}".Length)]
				: _config.ClientId;

		public async Task ConnectAsync(CancellationToken ct)
		{
			if (_client.IsConnected) return;

			var builder = new MqttClientOptionsBuilder()
				.WithClientId(StableClientId)
				.WithTcpServer(_config.BrokerUrl, _config.Port)
				.WithCleanStart(false)
				.WithSessionExpiryInterval((uint)_sessionExpiry.TotalSeconds)
				.WithTimeout(TimeSpan.FromSeconds(10));

			if (!string.IsNullOrWhiteSpace(_config.Username))
			{
				builder = builder.WithCredentials(_config.Username, _config.Password ?? string.Empty);
			}

			if (_config.UseTls)
			{
				builder = builder.WithTlsOptions(tls => tls.UseTls(true));
			}

			await _client.ConnectAsync(builder.Build(), ct);
		}

		public async Task SubscribeAsync(
			IReadOnlyList<TagPlan> tags,
			Func<TagSample, CancellationToken, Task> onSample,
			CancellationToken ct)
		{
			_tags = tags;
			_onSample = onSample;

			_byTopic.Clear();
			foreach (var tag in tags)
			{
				var topic = tag.SourceTopic ?? _config.Topic;
				_byTopic.AddOrUpdate(topic,
					_ => new List<TagPlan> { tag },
					(_, list) => { lock (list) { list.Add(tag); } return list; });
			}

			if (!_client.IsConnected) await ConnectAsync(ct);
			await ResubscribeAsync();
		}

		private async Task ResubscribeAsync()
		{
			if (!_client.IsConnected) return;

			// Satu langganan per topik yang benar-benar dipakai tag. Berlangganan "#" ketika
			// hanya tiga topik yang dipakai berarti menerima — dan mengurai — seluruh lalu
			// lintas broker.
			var topics = _byTopic.Keys.ToList();
			if (topics.Count == 0) topics.Add(_config.Topic);

			var builder = new MqttClientSubscribeOptionsBuilder();
			foreach (var topic in topics)
			{
				builder = builder.WithTopicFilter(filter => filter
					.WithTopic(topic)
					.WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce));
			}

			try
			{
				await _client.SubscribeAsync(builder.Build(), CancellationToken.None);
				_logger.LogInformation("MQTT {DeviceId} berlangganan {Count} topik", DeviceId, topics.Count);
			}
			catch (Exception ex)
			{
				_logger.LogWarning(ex, "MQTT {DeviceId} gagal berlangganan", DeviceId);
			}
		}

		private async Task OnMessageAsync(MqttApplicationMessageReceivedEventArgs args)
		{
			var topic = args.ApplicationMessage.Topic ?? string.Empty;
			var gatewayTs = DateTime.UtcNow;

			string payload;
			try
			{
				payload = args.ApplicationMessage.ConvertPayloadToString() ?? string.Empty;
			}
			catch (Exception)
			{
				payload = string.Empty;
			}

			var matched = MatchTags(topic);
			if (matched.Count == 0) return;

			JToken? root = null;
			try
			{
				using var reader = new JsonTextReader(new StringReader(payload))
				{
					DateParseHandling = DateParseHandling.None
				};
				root = JToken.ReadFrom(reader);
			}
			catch (JsonReaderException)
			{
				// Payload non-JSON tetap dipakai oleh tag ber-address "$".
			}

			foreach (var tag in matched)
			{
				var sample = Evaluate(tag, root, payload, gatewayTs);
				_latest[tag.TagId] = sample;

				if (_onSample is not null)
				{
					try
					{
						await _onSample(sample, CancellationToken.None);
					}
					catch (Exception ex)
					{
						// Kegagalan hilir tidak boleh mematikan handler pesan: sekali handler
						// melempar, MQTTnet akan terus melempar untuk pesan berikutnya dan
						// stream berhenti seluruhnya.
						_logger.LogError(ex, "Pemroses sampel gagal untuk tag {TagId}", tag.TagId);
					}
				}
			}
		}

		/// <summary>
		/// Mencocokkan topik pesan dengan tag, sadar wildcard MQTT (<c>+</c> satu level,
		/// <c>#</c> sisa level). Pencocokan tepat dicoba lebih dulu karena itulah kasus
		/// mayoritas dan biayanya satu pencarian hash.
		/// </summary>
		private List<TagPlan> MatchTags(string topic)
		{
			if (_byTopic.TryGetValue(topic, out var exact))
			{
				lock (exact) return new List<TagPlan>(exact);
			}

			var result = new List<TagPlan>();
			foreach (var (filter, tags) in _byTopic)
			{
				if (!TopicMatches(filter, topic)) continue;
				lock (tags) result.AddRange(tags);
			}
			return result;
		}

		internal static bool TopicMatches(string filter, string topic)
		{
			if (filter == topic) return true;
			if (filter == "#") return true;

			var f = filter.Split('/');
			var t = topic.Split('/');

			for (var i = 0; i < f.Length; i++)
			{
				if (f[i] == "#") return true;          // cocok sisa level berapa pun
				if (i >= t.Length) return false;
				if (f[i] == "+") continue;             // cocok tepat satu level
				if (f[i] != t[i]) return false;
			}

			return f.Length == t.Length;
		}

		private TagSample Evaluate(TagPlan tag, JToken? root, string payload, DateTime gatewayTs)
		{
			var basis = new TagSample
			{
				TagId = tag.TagId,
				DeviceId = DeviceId,
				SourceTs = gatewayTs,
				GatewayTs = gatewayTs,
				Quality = Quality.Good
			};

			if (tag.Address is "$" or "")
			{
				return FromText(tag, payload.Trim(), basis);
			}

			if (root is null)
			{
				return TagSample.Failed(tag.TagId, DeviceId,
					"Payload bukan JSON, sementara alamat tag berupa JSONPath", gatewayTs);
			}

			JToken? token;
			try
			{
				token = root.SelectToken(tag.Address);
			}
			catch (Newtonsoft.Json.JsonException ex)
			{
				return TagSample.Failed(tag.TagId, DeviceId, $"JSONPath tidak valid: {ex.Message}", gatewayTs);
			}

			if (token is null || token.Type == JTokenType.Null)
			{
				return TagSample.Failed(tag.TagId, DeviceId,
					$"Path {tag.Address} tidak ada pada payload topik ini", gatewayTs);
			}

			return token.Type switch
			{
				JTokenType.Integer or JTokenType.Float => basis with { Numeric = token.Value<double>() },
				JTokenType.Boolean => tag.DataType == DataType.BOOLEAN
					? basis with { Boolean = token.Value<bool>() }
					: basis with { Numeric = token.Value<bool>() ? 1 : 0, Boolean = token.Value<bool>() },
				JTokenType.String => FromText(tag, token.Value<string>() ?? string.Empty, basis),
				JTokenType.Object or JTokenType.Array => TagSample.Failed(tag.TagId, DeviceId,
					$"Path {tag.Address} menunjuk nilai bersarang", gatewayTs),
				_ => TagSample.Failed(tag.TagId, DeviceId, $"Tipe tidak didukung: {token.Type}", gatewayTs)
			};
		}

		private TagSample FromText(TagPlan tag, string text, TagSample basis)
		{
			if (tag.DataType == DataType.STRING) return basis with { Text = text };

			if (tag.DataType == DataType.BOOLEAN)
			{
				if (bool.TryParse(text, out var flag)) return basis with { Boolean = flag };
				if (text is "1") return basis with { Boolean = true };
				if (text is "0") return basis with { Boolean = false };
				return TagSample.Failed(tag.TagId, DeviceId,
					$"Nilai '{text}' tidak bisa dibaca sebagai boolean", basis.GatewayTs);
			}

			if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
			{
				return basis with { Numeric = number };
			}

			return TagSample.Failed(tag.TagId, DeviceId,
				$"Nilai '{text}' tidak bisa dibaca sebagai angka", basis.GatewayTs);
		}

		/// <summary>
		/// Untuk driver dorong, <see cref="ReadAsync"/> mengembalikan nilai TERAKHIR yang
		/// diterima, bukan memicu pembacaan baru — MQTT tidak punya konsep "baca sekarang".
		/// Inilah yang memungkinkan scan class mengatur laju simpan (mis. simpan tiap 1 detik)
		/// secara terpisah dari laju terima (mis. 50 pesan/detik dari perangkat).
		///
		/// Tag yang belum pernah menerima pesan dilaporkan Bad, bukan dilewati: perangkat yang
		/// diam adalah informasi, dan menghilangkannya dari hasil membuat dasbor menampilkan
		/// tag itu sebagai "belum ada data" selamanya tanpa penjelasan.
		/// </summary>
		public Task<IReadOnlyList<TagSample>> ReadAsync(IReadOnlyList<TagPlan> tags, CancellationToken ct)
		{
			var now = DateTime.UtcNow;
			var samples = new List<TagSample>(tags.Count);

			foreach (var tag in tags)
			{
				if (_latest.TryGetValue(tag.TagId, out var last))
				{
					samples.Add(last);
					continue;
				}

				samples.Add(TagSample.Failed(tag.TagId, DeviceId,
					_client.IsConnected
						? "Belum ada pesan pada topik tag ini"
						: "Broker belum tersambung", now));
			}

			return Task.FromResult<IReadOnlyList<TagSample>>(samples);
		}

		public async ValueTask DisposeAsync()
		{
			try
			{
				if (_client.IsConnected)
				{
					await _client.DisconnectAsync();
				}
			}
			catch (Exception)
			{
				// Broker sudah memutus lebih dulu.
			}
			finally
			{
				_client.Dispose();
			}
		}
	}
}
