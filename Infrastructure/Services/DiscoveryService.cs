using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Core.DTOs;
using Core.DTOs.Discovery;
using Core.Interface;
using Core.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Protocol;

namespace Infrastructure.Services
{
	/// <summary>
	/// Penemuan struktur data perangkat untuk pemilih key di UI.
	///
	/// Prinsip yang dipegang seluruh kelas ini: <b>kegagalan bukan exception</b>. Endpoint
	/// yang mati, kredensial broker yang salah, atau topik yang salah tulis adalah keadaan
	/// normal saat seseorang sedang menyusun konfigurasi — jawabannya harus berupa hasil yang
	/// menjelaskan apa yang terjadi, bukan HTTP 500 yang membuat form terlihat rusak.
	/// </summary>
	public class DiscoveryService : IDiscoveryService
	{
		private readonly IHttpClientFactory _httpClientFactory;
		private readonly ILogger<DiscoveryService> _logger;

		/// <summary>Batas payload yang dibaca dari endpoint; cukup besar untuk respons wajar,
		/// cukup kecil untuk menolak endpoint yang mengalirkan data tanpa henti.</summary>
		private const int MaxPayloadBytes = 512 * 1024;

		/// <summary>Batas topik yang dilaporkan. Filter <c>#</c> pada broker sibuk bisa
		/// menyentuh ribuan topik; melaporkan semuanya hanya membekukan UI.</summary>
		private const int MaxTopics = 60;

		/// <summary>Batas pesan yang diproses per pendengaran, sebagai jaring terakhir
		/// terhadap broker yang mengalir sangat cepat.</summary>
		private const int MaxMessages = 5000;

		private readonly bool _allowLinkLocal;

		public DiscoveryService(
			IHttpClientFactory httpClientFactory,
			ILogger<DiscoveryService> logger,
			IOptions<SecuritySettings> securitySettings)
		{
			_httpClientFactory = httpClientFactory;
			_logger = logger;
			_allowLinkLocal = securitySettings.Value.AllowLinkLocalProbe;
		}

		/* ================================================================== HTTP */

		public async Task<ApiResponse<HttpProbeResultDto>> ProbeHttpAsync(
			HttpProbeRequestDto request,
			CancellationToken ct = default)
		{
			if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) ||
				(uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
			{
				return ApiResponse<HttpProbeResultDto>.Fail(400, "URL tidak valid",
					"URL harus absolut dan memakai skema http atau https.");
			}

			// Endpoint ini membuat SERVER memanggil alamat pilihan pemanggil (SSRF). Alamat
			// privat SENGAJA diizinkan — perangkat lapangan justru berada di sana, dan itulah
			// gunanya sebuah gateway. Yang diblokir adalah link-local: tidak ada perangkat
			// industri di sana, sementara 169.254.169.254 adalah endpoint metadata instans
			// cloud yang menyimpan kredensial.
			var blockReason = SsrfGuard.Inspect(uri, _allowLinkLocal);
			if (blockReason is not null)
			{
				return ApiResponse<HttpProbeResultDto>.Fail(400, "Alamat tujuan diblokir", blockReason);
			}

			var result = new HttpProbeResultDto();
			var stopwatch = Stopwatch.StartNew();

			try
			{
				var client = _httpClientFactory.CreateClient("discovery");
				using var httpRequest = new HttpRequestMessage(
					new HttpMethod(request.Method.ToUpperInvariant()),
					uri);

				if (request.Headers is not null)
				{
					foreach (var header in request.Headers)
					{
						// Header konten harus menempel pada konten, bukan pada request —
						// TryAddWithoutValidation di kedua tempat menghindari exception untuk
						// header yang salah tempat.
						if (!httpRequest.Headers.TryAddWithoutValidation(header.Key, header.Value))
						{
							_logger.LogDebug("Header {Header} dilewati saat probe", header.Key);
						}
					}
				}

				if (!string.IsNullOrWhiteSpace(request.Body))
				{
					httpRequest.Content = new StringContent(request.Body, Encoding.UTF8, "application/json");
				}

				using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
				timeoutCts.CancelAfter(request.TimeoutMs);

				using var response = await client.SendAsync(
					httpRequest,
					HttpCompletionOption.ResponseHeadersRead,
					timeoutCts.Token);

				var payload = await ReadLimitedAsync(response, timeoutCts.Token);
				stopwatch.Stop();

				result.StatusCode = (int)response.StatusCode;
				result.LatencyMs = stopwatch.ElapsedMilliseconds;
				result.ContentType = response.Content.Headers.ContentType?.ToString();
				result.PayloadBytes = Encoding.UTF8.GetByteCount(payload);
				result.RawPayload = PayloadFlattener.Prettify(payload);
				result.IsJson = PayloadFlattener.DetectKind(payload) == PayloadFlattener.PayloadKind.Json;
				result.Keys = PayloadFlattener.Flatten(payload);
				result.IsSuccess = response.IsSuccessStatusCode;

				if (!response.IsSuccessStatusCode)
				{
					result.ErrorMessage = $"Endpoint menjawab {(int)response.StatusCode} {response.ReasonPhrase}.";
				}
				else if (!result.IsJson)
				{
					result.ErrorMessage = null; // bukan error; payload non-JSON tetap bisa dipakai
				}

				return ApiResponse<HttpProbeResultDto>.Success(result,
					result.IsSuccess
						? $"{result.Keys.Count} key terdeteksi"
						: "Endpoint terhubung tapi menjawab dengan status gagal");
			}
			catch (OperationCanceledException) when (!ct.IsCancellationRequested)
			{
				stopwatch.Stop();
				result.LatencyMs = stopwatch.ElapsedMilliseconds;
				result.ErrorMessage = $"Tidak ada jawaban dalam {request.TimeoutMs} ms. Periksa alamat, port, dan firewall.";
				return ApiResponse<HttpProbeResultDto>.Success(result, "Waktu tunggu habis");
			}
			catch (HttpRequestException ex)
			{
				stopwatch.Stop();
				result.LatencyMs = stopwatch.ElapsedMilliseconds;
				// Pesan HttpRequestException sering berlapis; yang paling informatif untuk
				// operator justru penyebab terdalamnya ("connection refused", "no such host").
				result.ErrorMessage = InnermostMessage(ex);
				return ApiResponse<HttpProbeResultDto>.Success(result, "Gagal menghubungi endpoint");
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Probe HTTP gagal untuk {Url}", request.Url);
				return ApiResponse<HttpProbeResultDto>.Fail(500, "Terjadi kesalahan saat memprobe endpoint");
			}
		}

		/// <summary>
		/// Membaca respons dengan batas ukuran. Endpoint yang mengalirkan data tanpa akhir
		/// (SSE, log stream) akan mengisi memori kalau dibaca sampai selesai.
		/// </summary>
		private static async Task<string> ReadLimitedAsync(HttpResponseMessage response, CancellationToken ct)
		{
			await using var stream = await response.Content.ReadAsStreamAsync(ct);
			var buffer = new byte[8192];
			var collected = new MemoryStream();
			int read;

			while ((read = await stream.ReadAsync(buffer, ct)) > 0)
			{
				collected.Write(buffer, 0, read);
				if (collected.Length >= MaxPayloadBytes) break;
			}

			return Encoding.UTF8.GetString(collected.ToArray());
		}

		private static string InnermostMessage(Exception ex)
		{
			var current = ex;
			while (current.InnerException is not null) current = current.InnerException;
			return current.Message;
		}

		/* ================================================================== MQTT */

		public async Task<ApiResponse<MqttSniffResultDto>> SniffMqttAsync(
			MqttSniffRequestDto request,
			CancellationToken ct = default)
		{
			var result = new MqttSniffResultDto { DurationSeconds = request.DurationSeconds };
			var samples = new ConcurrentDictionary<string, TopicAccumulator>();
			var totalMessages = 0;

			// ClientId khusus pendengaran. Memakai ClientId perangkat akan membuat broker
			// memutus koneksi akuisisi yang sedang bekerja — dua klien dengan id sama tidak
			// bisa hidup bersamaan di satu broker.
			var clientId = string.IsNullOrWhiteSpace(request.ClientId)
				? $"synapse-discovery-{Guid.NewGuid():N}"[..32]
				: request.ClientId!;

			var factory = new MqttClientFactory();
			using var client = factory.CreateMqttClient();

			client.ApplicationMessageReceivedAsync += args =>
			{
				var count = Interlocked.Increment(ref totalMessages);
				if (count > MaxMessages) return Task.CompletedTask;

				var topic = args.ApplicationMessage.Topic ?? "(tanpa topik)";

				// Topik baru di luar batas diabaikan, tapi topik yang sudah terdaftar tetap
				// diperbarui — jadi batasnya membatasi keragaman, bukan menghentikan pengamatan.
				if (!samples.ContainsKey(topic) && samples.Count >= MaxTopics) return Task.CompletedTask;

				string payload;
				try
				{
					payload = args.ApplicationMessage.ConvertPayloadToString() ?? string.Empty;
				}
				catch (Exception)
				{
					payload = string.Empty;
				}

				var accumulator = samples.GetOrAdd(topic, _ => new TopicAccumulator());
				accumulator.Add(payload, args.ApplicationMessage.Retain);

				return Task.CompletedTask;
			};

			try
			{
				var optionsBuilder = new MqttClientOptionsBuilder()
					.WithClientId(clientId)
					.WithTcpServer(request.BrokerUrl, request.Port)
					// Pendengaran adalah sesi sekali pakai; sesi bersih menghindari
					// meninggalkan langganan menggantung di broker setelah selesai.
					.WithCleanStart(true)
					.WithTimeout(TimeSpan.FromSeconds(10));

				if (!string.IsNullOrWhiteSpace(request.Username))
				{
					optionsBuilder = optionsBuilder.WithCredentials(request.Username, request.Password ?? string.Empty);
				}

				if (request.UseTls)
				{
					optionsBuilder = optionsBuilder.WithTlsOptions(tls => tls.UseTls(true));
				}

				using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
				connectCts.CancelAfter(TimeSpan.FromSeconds(12));
				await client.ConnectAsync(optionsBuilder.Build(), connectCts.Token);

				await client.SubscribeAsync(
					new MqttClientSubscribeOptionsBuilder()
						.WithTopicFilter(filter => filter
							.WithTopic(request.TopicFilter)
							.WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtMostOnce))
						.Build(),
					ct);

				// QoS 0 untuk pendengaran: kita hanya ingin tahu bentuk datanya, dan tidak
				// boleh mengambil alih antrean QoS 1 milik sesi akuisisi.
				await Task.Delay(TimeSpan.FromSeconds(request.DurationSeconds), ct);
			}
			catch (OperationCanceledException) when (!ct.IsCancellationRequested)
			{
				result.ErrorMessage = "Tidak bisa menyambung ke broker dalam 12 detik. Periksa alamat, port, dan firewall.";
				return ApiResponse<MqttSniffResultDto>.Success(result, "Waktu tunggu koneksi habis");
			}
			catch (Exception ex)
			{
				_logger.LogWarning(ex, "Sniff MQTT gagal untuk {Broker}:{Port}", request.BrokerUrl, request.Port);
				result.ErrorMessage = InnermostMessage(ex);
				return ApiResponse<MqttSniffResultDto>.Success(result, "Gagal menyambung ke broker");
			}
			finally
			{
				try
				{
					if (client.IsConnected) await client.DisconnectAsync(cancellationToken: CancellationToken.None);
				}
				catch (Exception)
				{
					// Broker sudah memutus lebih dulu — tidak ada yang perlu dibereskan.
				}
			}

			result.IsSuccess = true;
			result.TotalMessages = Math.Min(totalMessages, MaxMessages);
			result.ConnectedButSilent = totalMessages == 0;

			result.Topics = samples
				.Select(pair => pair.Value.ToDto(pair.Key, request.DurationSeconds))
				.OrderByDescending(topic => topic.MessageCount)
				.ToList();

			var message = result.ConnectedButSilent
				? "Tersambung, tapi tidak ada pesan yang masuk selama pendengaran"
				: $"{result.Topics.Count} topik terdeteksi dari {result.TotalMessages} pesan";

			return ApiResponse<MqttSniffResultDto>.Success(result, message);
		}

		/// <summary>
		/// Ringkasan satu topik selama pendengaran. Hanya payload TERAKHIR yang disimpan —
		/// menyimpan seluruh riwayat pesan tidak menambah informasi untuk pemilih key, dan
		/// pada topik yang mengalir cepat akan menghabiskan memori.
		/// </summary>
		private sealed class TopicAccumulator
		{
			private readonly object _gate = new();
			private string _lastPayload = string.Empty;
			private bool _retained;
			private int _count;

			public void Add(string payload, bool retained)
			{
				lock (_gate)
				{
					_count++;
					_lastPayload = payload;
					// Sekali saja retained sudah cukup untuk memberi tahu operator bahwa
					// nilai di topik ini bisa berasal dari masa lalu.
					if (retained) _retained = true;
				}
			}

			public MqttTopicSampleDto ToDto(string topic, int durationSeconds)
			{
				lock (_gate)
				{
					var kind = PayloadFlattener.DetectKind(_lastPayload);

					return new MqttTopicSampleDto
					{
						Topic = topic,
						MessageCount = _count,
						RatePerSecond = durationSeconds > 0
							? Math.Round(_count / (double)durationSeconds, 2)
							: 0,
						IsRetained = _retained,
						PayloadKind = kind switch
						{
							PayloadFlattener.PayloadKind.Json => "json",
							PayloadFlattener.PayloadKind.Number => "number",
							PayloadFlattener.PayloadKind.Empty => "empty",
							PayloadFlattener.PayloadKind.Binary => "binary",
							_ => "text"
						},
						LastPayload = PayloadFlattener.Prettify(_lastPayload, 2000),
						Keys = PayloadFlattener.Flatten(_lastPayload)
					};
				}
			}
		}
	}
}
