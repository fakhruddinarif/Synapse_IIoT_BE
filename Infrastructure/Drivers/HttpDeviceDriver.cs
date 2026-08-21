using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Core.Acquisition;
using Core.DTOs;
using Core.Enums;
using Core.Interface;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Infrastructure.Drivers
{
	/// <summary>
	/// Driver HTTP — pola TARIK. Scheduler memanggil <see cref="ReadAsync"/> setiap tick.
	///
	/// Keputusan terpenting di kelas ini: <b>satu permintaan melayani seluruh tag perangkat</b>.
	/// Dua belas tag dari satu endpoint berarti satu panggilan HTTP, bukan dua belas; JSONPath
	/// tiap tag dievaluasi pada dokumen yang sama. Kontrak per-tag akan mengubah satu scan
	/// 40 ms menjadi 480 ms, dan pada scan 1 detik dengan lima perangkat itu sudah melewati
	/// anggaran waktunya.
	/// </summary>
	public sealed class HttpDeviceDriver : IDeviceDriver
	{
		private readonly HttpConfig _config;
		private readonly IHttpClientFactory _httpClientFactory;
		private readonly ILogger<HttpDeviceDriver> _logger;
		private readonly int _timeoutMs;

		private DriverHealth _health;

		public Protocol Protocol => Protocol.HTTP;
		public Guid DeviceId { get; }
		public DriverHealth Health => _health;

		public HttpDeviceDriver(
			DevicePlan plan,
			IHttpClientFactory httpClientFactory,
			ILogger<HttpDeviceDriver> logger)
		{
			DeviceId = plan.DeviceId;
			_httpClientFactory = httpClientFactory;
			_logger = logger;

			_config = JsonSerializer.Deserialize<HttpConfig>(
				plan.ConnectionConfigJson,
				new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
				?? throw new ArgumentException("Konfigurasi HTTP tidak bisa dibaca", nameof(plan));

			// Permintaan yang lebih lambat dari periodenya sendiri harus dibatalkan, bukan
			// menumpuk: scan 1 detik dengan timeout 30 detik akan mengumpulkan 30 permintaan
			// menggantung untuk satu perangkat yang lambat.
			_timeoutMs = Math.Clamp(plan.ScanIntervalMs, 1_000, 10_000);
		}

		public Task ConnectAsync(CancellationToken ct) => Task.CompletedTask;

		public Task SubscribeAsync(
			IReadOnlyList<TagPlan> tags,
			Func<TagSample, CancellationToken, Task> onSample,
			CancellationToken ct) => Task.CompletedTask;

		public async Task<IReadOnlyList<TagSample>> ReadAsync(
			IReadOnlyList<TagPlan> tags,
			CancellationToken ct)
		{
			if (tags.Count == 0) return Array.Empty<TagSample>();

			var gatewayTs = DateTime.UtcNow;
			var stopwatch = Stopwatch.StartNew();

			try
			{
				var client = _httpClientFactory.CreateClient("device-http");
				using var request = new HttpRequestMessage(
					new HttpMethod(string.IsNullOrWhiteSpace(_config.Method) ? "GET" : _config.Method.ToUpperInvariant()),
					_config.Url);

				if (_config.Headers is not null)
				{
					foreach (var (key, value) in _config.Headers)
					{
						request.Headers.TryAddWithoutValidation(key, value);
					}
				}

				using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
				timeoutCts.CancelAfter(_timeoutMs);

				using var response = await client.SendAsync(request, timeoutCts.Token);
				var body = await response.Content.ReadAsStringAsync(timeoutCts.Token);

				if (!response.IsSuccessStatusCode)
				{
					return Fail(tags, gatewayTs,
						$"Endpoint menjawab {(int)response.StatusCode} {response.ReasonPhrase}");
				}

				var samples = Extract(tags, body, gatewayTs);

				_health = new DriverHealth
				{
					IsConnected = true,
					LastSuccessAt = gatewayTs,
					ConsecutiveFailures = 0
				};

				_logger.LogDebug("HTTP {DeviceId}: {Count} tag dibaca dalam {Ms} ms",
					DeviceId, samples.Count, stopwatch.ElapsedMilliseconds);

				return samples;
			}
			catch (OperationCanceledException) when (!ct.IsCancellationRequested)
			{
				return Fail(tags, gatewayTs, $"Tidak ada jawaban dalam {_timeoutMs} ms");
			}
			catch (HttpRequestException ex)
			{
				return Fail(tags, gatewayTs, InnermostMessage(ex));
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				_logger.LogError(ex, "Pembacaan HTTP gagal untuk perangkat {DeviceId}", DeviceId);
				return Fail(tags, gatewayTs, "Kesalahan tak terduga saat membaca perangkat");
			}
		}

		/// <summary>
		/// Mengevaluasi JSONPath setiap tag pada satu dokumen. Payload diurai SEKALI —
		/// mengurainya per tag adalah biaya CPU yang berlipat tanpa manfaat.
		/// </summary>
		private List<TagSample> Extract(IReadOnlyList<TagPlan> tags, string body, DateTime gatewayTs)
		{
			var samples = new List<TagSample>(tags.Count);

			JToken? root = null;
			var isJson = false;
			try
			{
				using var reader = new Newtonsoft.Json.JsonTextReader(new StringReader(body))
				{
					DateParseHandling = Newtonsoft.Json.DateParseHandling.None
				};
				root = JToken.ReadFrom(reader);
				isJson = true;
			}
			catch (Newtonsoft.Json.JsonReaderException)
			{
				// Payload non-JSON tetap berguna: tag dengan address "$" mengambil seluruh isi.
			}

			foreach (var tag in tags)
			{
				// Address "$" berarti seluruh payload adalah nilainya — bentuk yang lazim pada
				// perangkat sederhana yang menjawab satu angka polos.
				if (tag.Address is "$" or "")
				{
					samples.Add(FromScalarText(tag, body.Trim(), gatewayTs));
					continue;
				}

				if (!isJson || root is null)
				{
					samples.Add(TagSample.Failed(tag.TagId, DeviceId,
						"Payload bukan JSON, sementara alamat tag berupa JSONPath", gatewayTs));
					continue;
				}

				JToken? token;
				try
				{
					token = root.SelectToken(tag.Address);
				}
				catch (Newtonsoft.Json.JsonException ex)
				{
					samples.Add(TagSample.Failed(tag.TagId, DeviceId,
						$"JSONPath tidak valid: {ex.Message}", gatewayTs));
					continue;
				}

				if (token is null || token.Type == JTokenType.Null)
				{
					// Path tidak ditemukan adalah kesalahan PEMETAAN, bukan kesalahan
					// perangkat — dan pesannya harus mengatakan itu, supaya operator memeriksa
					// mapping alih-alih memeriksa kabel.
					samples.Add(TagSample.Failed(tag.TagId, DeviceId,
						$"Path {tag.Address} tidak ada pada payload", gatewayTs));
					continue;
				}

				samples.Add(FromToken(tag, token, gatewayTs));
			}

			return samples;
		}

		private TagSample FromToken(TagPlan tag, JToken token, DateTime gatewayTs)
		{
			var basis = new TagSample
			{
				TagId = tag.TagId,
				DeviceId = DeviceId,
				SourceTs = gatewayTs,
				GatewayTs = gatewayTs,
				Quality = Quality.Good
			};

			switch (token.Type)
			{
				case JTokenType.Integer:
				case JTokenType.Float:
					return basis with { Numeric = token.Value<double>() };

				case JTokenType.Boolean:
					var flag = token.Value<bool>();
					// Tag boolean disimpan sebagai boolean; tag numerik yang sumbernya boolean
					// dipetakan ke 0/1 supaya tetap bisa digrafikkan.
					return tag.DataType == DataType.BOOLEAN
						? basis with { Boolean = flag }
						: basis with { Numeric = flag ? 1 : 0, Boolean = flag };

				case JTokenType.String:
					return FromScalarText(tag, token.Value<string>() ?? string.Empty, gatewayTs);

				case JTokenType.Object:
				case JTokenType.Array:
					return TagSample.Failed(tag.TagId, DeviceId,
						$"Path {tag.Address} menunjuk nilai bersarang, bukan nilai tunggal", gatewayTs);

				default:
					return TagSample.Failed(tag.TagId, DeviceId,
						$"Tipe nilai tidak didukung: {token.Type}", gatewayTs);
			}
		}

		/// <summary>
		/// Teks yang berisi angka tetap diperlakukan sebagai angka bila tag-nya numerik.
		/// Perangkat MQTT/HTTP sederhana sangat sering mengirim <c>"23.4"</c>, bukan
		/// <c>23.4</c>, dan menolaknya berarti menolak data yang sah.
		/// </summary>
		private TagSample FromScalarText(TagPlan tag, string text, DateTime gatewayTs)
		{
			var basis = new TagSample
			{
				TagId = tag.TagId,
				DeviceId = DeviceId,
				SourceTs = gatewayTs,
				GatewayTs = gatewayTs,
				Quality = Quality.Good
			};

			if (tag.DataType == DataType.STRING)
			{
				return basis with { Text = text };
			}

			if (tag.DataType == DataType.BOOLEAN)
			{
				if (bool.TryParse(text, out var parsedBool)) return basis with { Boolean = parsedBool };
				if (text is "1") return basis with { Boolean = true };
				if (text is "0") return basis with { Boolean = false };

				return TagSample.Failed(tag.TagId, DeviceId,
					$"Nilai '{Trim(text)}' tidak bisa dibaca sebagai boolean", gatewayTs);
			}

			if (double.TryParse(text, System.Globalization.NumberStyles.Float,
					System.Globalization.CultureInfo.InvariantCulture, out var number))
			{
				return basis with { Numeric = number };
			}

			return TagSample.Failed(tag.TagId, DeviceId,
				$"Nilai '{Trim(text)}' tidak bisa dibaca sebagai angka", gatewayTs);
		}

		private static string Trim(string value) => value.Length <= 32 ? value : value[..32] + "…";

		/// <summary>
		/// Kegagalan mengembalikan satu sampel Bad untuk SETIAP tag, bukan daftar kosong.
		/// Dengan daftar kosong, tag engine tidak punya cara membedakan "perangkat gagal
		/// dibaca" dari "tidak ada tag yang diminta", dan dasbor akan terus menampilkan nilai
		/// terakhir seolah masih hidup.
		/// </summary>
		private List<TagSample> Fail(IReadOnlyList<TagPlan> tags, DateTime at, string reason)
		{
			_health = new DriverHealth
			{
				IsConnected = false,
				LastError = reason,
				LastSuccessAt = _health.LastSuccessAt,
				ConsecutiveFailures = _health.ConsecutiveFailures + 1
			};

			var samples = new List<TagSample>(tags.Count);
			foreach (var tag in tags)
			{
				samples.Add(TagSample.Failed(tag.TagId, DeviceId, reason, at));
			}
			return samples;
		}

		private static string InnermostMessage(Exception ex)
		{
			var current = ex;
			while (current.InnerException is not null) current = current.InnerException;
			return current.Message;
		}

		public ValueTask DisposeAsync() => ValueTask.CompletedTask;
	}
}
