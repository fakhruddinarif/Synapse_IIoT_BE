using System.Collections.Concurrent;
using Core.Acquisition;
using Core.Interface;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Acquisition
{
	/// <summary>
	/// Menggabungkan sampel menjadi frame per perangkat, lalu mengirimkannya paling sering
	/// sekali per <c>windowMs</c>.
	///
	/// KENAPA DIGABUNG:
	///
	/// Mata manusia dan piksel layar tidak bisa memanfaatkan lebih dari beberapa pembaruan per
	/// detik. Mengirim satu pesan per pembacaan pada 1.000 tag @ 1 s berarti ribuan pesan
	/// kecil per detik ke setiap tab — biaya CPU di browser dan di gateway, tanpa satu pun
	/// informasi tambahan yang bisa dilihat.
	///
	/// Nilai yang ditimpa di dalam satu jendela TIDAK hilang dari sistem: jalur historian
	/// menerima setiap sampel secara terpisah lewat WAL. Yang dilewatkan hanya tampilannya —
	/// dan itu memang boleh, karena UI adalah jendela, bukan arsip.
	/// </summary>
	public sealed class RealtimeCoalescer : IAsyncDisposable
	{
		private sealed class Pending
		{
			public readonly Dictionary<Guid, TagSample> Latest = new();
			public string DeviceName = string.Empty;

			/// <summary>
			/// Nomor urut frame PER PERANGKAT, bukan per gateway.
			///
			/// Klien hampir selalu berlangganan sebagian perangkat saja. Dengan penomoran global,
			/// frame yang ia terima akan melompat-lompat (1, 4, 9) hanya karena perangkat lain
			/// ikut mengirim — sehingga deteksi "ada frame yang hilang" akan menyala terus dan
			/// berhenti dipercaya. Nomor per perangkat membuat setiap lompatan berarti benar-benar
			/// ada frame yang tidak sampai.
			/// </summary>
			public long Seq;
		}

		private readonly IRealtimePublisher _publisher;
		private readonly ILogger<RealtimeCoalescer>? _logger;
		private readonly int _windowMs;
		private readonly ConcurrentDictionary<Guid, Pending> _pending = new();
		private readonly CancellationTokenSource _cts = new();
		private readonly Task _loop;

		public RealtimeCoalescer(
			IRealtimePublisher publisher,
			int windowMs = 250,
			ILogger<RealtimeCoalescer>? logger = null)
		{
			_publisher = publisher;
			_windowMs = Math.Max(50, windowMs);
			_logger = logger;
			_loop = Task.Run(() => FlushLoopAsync(_cts.Token));
		}

		/// <summary>
		/// Menyerahkan sampel untuk dikirim. Dalam satu jendela, nilai terbaru per tag
		/// MENGGANTIKAN yang sebelumnya — bukan ditumpuk. Menumpuknya akan membuat frame
		/// tumbuh tanpa batas saat klien lambat, dan yang dikirim tetap hanya nilai terakhir
		/// yang terlihat.
		/// </summary>
		public void Enqueue(Guid deviceId, string deviceName, TagSample sample)
		{
			var pending = _pending.GetOrAdd(deviceId, _ => new Pending());
			lock (pending)
			{
				pending.DeviceName = deviceName;
				pending.Latest[sample.TagId] = sample;
			}
		}

		private async Task FlushLoopAsync(CancellationToken ct)
		{
			// PeriodicTimer, bukan Task.Delay setelah pekerjaan: periode harus diukur dari
			// jadwal, bukan dari selesainya pengiriman, kalau tidak jendela melar mengikuti
			// lambatnya klien.
			using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_windowMs));

			while (await SafeWaitAsync(timer, ct))
			{
				foreach (var deviceId in _pending.Keys)
				{
					if (!_pending.TryGetValue(deviceId, out var pending)) continue;

					RealtimeFrame? frame = null;
					lock (pending)
					{
						if (pending.Latest.Count == 0) continue;

						frame = new RealtimeFrame
						{
							DeviceId = deviceId,
							DeviceName = pending.DeviceName,
							Seq = ++pending.Seq,
							Ts = DateTime.UtcNow,
							Values = pending.Latest.Values.Select(ToValue).ToList()
						};
						pending.Latest.Clear();
					}

					try
					{
						await _publisher.PublishAsync(deviceId, frame, ct);
					}
					catch (Exception ex) when (ex is not OperationCanceledException)
					{
						// Klien yang gagal dikirimi tidak boleh menghentikan pengiriman ke
						// perangkat lain, dan sama sekali tidak boleh menyentuh akuisisi.
						_logger?.LogWarning(ex, "Gagal mengirim frame realtime perangkat {DeviceId}", deviceId);
					}
				}
			}
		}

		private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken ct)
		{
			try
			{
				return await timer.WaitForNextTickAsync(ct);
			}
			catch (OperationCanceledException)
			{
				return false;
			}
		}

		private static RealtimeValue ToValue(TagSample sample) => new()
		{
			TagId = sample.TagId,
			Numeric = sample.Numeric,
			Boolean = sample.Boolean,
			Text = sample.Text,
			Quality = (byte)sample.Quality
		};

		public async ValueTask DisposeAsync()
		{
			await _cts.CancelAsync();
			try
			{
				await _loop;
			}
			catch (OperationCanceledException)
			{
				// Penghentian normal.
			}
			_cts.Dispose();
		}
	}
}
