using Core.Interface;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Acquisition
{
	public sealed class HistorianOptions
	{
		/// <summary>Jumlah sampel maksimum per batch tulis.</summary>
		public int BatchSize { get; set; } = 500;

		/// <summary>Jeda saat buffer kosong. Tidak perlu agresif: sampel sudah aman di WAL.</summary>
		public int IdlePollMs { get; set; } = 250;

		/// <summary>Jeda awal saat penulisan gagal, lalu naik sampai <see cref="MaxRetryMs"/>.</summary>
		public int RetryBaseMs { get; set; } = 1_000;

		public int MaxRetryMs { get; set; } = 30_000;
	}

	/// <summary>
	/// Menguras buffer tahan-mati ke historian.
	///
	/// URUTAN YANG MEMBUAT JANJI "TIDAK ADA DATA HILANG" BISA DIPEGANG:
	///
	///   1. baca batch dari WAL (belum dikomit)
	///   2. tulis ke historian secara idempoten
	///   3. baru setelah historian mengonfirmasi, batch dikomit di WAL
	///
	/// Kalau proses mati di antara 2 dan 3, batch yang sama akan dibaca ulang setelah restart
	/// dan ditulis dua kali — dan justru itu sebabnya penulisannya harus idempoten
	/// (<c>ON CONFLICT (tag_id, source_ts)</c>). Menukar urutan 2 dan 3 akan menghasilkan sistem
	/// yang kelihatan lebih rapi dan diam-diam kehilangan satu batch setiap kali proses mati.
	///
	/// Kegagalan database TIDAK menghentikan akuisisi: WAL terus tumbuh, backoff melambat, dan
	/// begitu database kembali seluruh tumpukan mengalir masuk. Inilah store-and-forward yang
	/// membedakan gateway dari skrip polling.
	/// </summary>
	public sealed class HistorianDrainService(
		ISampleBuffer buffer,
		ISampleWriter writer,
		HistorianOptions options,
		ILogger<HistorianDrainService> logger) : BackgroundService
	{
		private long _written;
		private long _failures;

		public long TotalWritten => Interlocked.Read(ref _written);

		protected override async Task ExecuteAsync(CancellationToken stoppingToken)
		{
			logger.LogInformation("Historian drain mulai");
			var retryMs = options.RetryBaseMs;

			while (!stoppingToken.IsCancellationRequested)
			{
				try
				{
					var batch = await buffer.ReadBatchAsync(options.BatchSize, stoppingToken);

					if (batch.IsEmpty)
					{
						await Task.Delay(options.IdlePollMs, stoppingToken);
						continue;
					}

					var ok = await writer.WriteAsync(batch.Samples, stoppingToken);

					if (!ok)
					{
						// Batch TIDAK dikomit. Ia akan dibaca lagi pada putaran berikutnya —
						// itulah perilaku yang benar untuk kegagalan sementara.
						Interlocked.Increment(ref _failures);
						logger.LogWarning(
							"Historian menolak {Count} sampel; akan dicoba lagi dalam {Delay} ms",
							batch.Samples.Count, retryMs);

						await Task.Delay(retryMs, stoppingToken);
						retryMs = Math.Min(retryMs * 2, options.MaxRetryMs);
						continue;
					}

					await buffer.CommitAsync(batch.CommitToken, stoppingToken);
					Interlocked.Add(ref _written, batch.Samples.Count);
					retryMs = options.RetryBaseMs;

					logger.LogDebug("{Count} sampel masuk historian", batch.Samples.Count);
				}
				catch (OperationCanceledException)
				{
					break;
				}
				catch (Exception ex)
				{
					Interlocked.Increment(ref _failures);
					logger.LogError(ex, "Historian drain gagal; mencoba lagi dalam {Delay} ms", retryMs);

					try
					{
						await Task.Delay(retryMs, stoppingToken);
					}
					catch (OperationCanceledException)
					{
						break;
					}

					retryMs = Math.Min(retryMs * 2, options.MaxRetryMs);
				}
			}

			logger.LogInformation(
				"Historian drain berhenti; {Written} sampel tertulis, {Failures} kegagalan",
				Interlocked.Read(ref _written), Interlocked.Read(ref _failures));
		}
	}
}
