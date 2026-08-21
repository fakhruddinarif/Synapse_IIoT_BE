using Core.Acquisition;

namespace Core.Interface
{
	/// <summary>Satu batch sampel yang siap ditulis ke historian, beserta token komitnya.</summary>
	public sealed record SampleBatch
	{
		public required IReadOnlyList<TagSample> Samples { get; init; }

		/// <summary>
		/// Penanda posisi untuk dikembalikan ke <see cref="ISampleBuffer.CommitAsync"/> setelah
		/// historian mengonfirmasi tulisan. Batch yang tidak dikomit akan dibaca ulang setelah
		/// restart — itulah yang membuat crash tidak menghilangkan sampel.
		/// </summary>
		public required long CommitToken { get; init; }

		public bool IsEmpty => Samples.Count == 0;
	}

	/// <summary>Ukuran buffer, untuk halaman kesehatan sistem dan alarm kapasitas.</summary>
	public readonly record struct BufferStats
	{
		public long PendingBytes { get; init; }
		public long TotalBytes { get; init; }
		public long AppendedCount { get; init; }
		public long CommittedCount { get; init; }
	}

	/// <summary>
	/// Penyangga tahan-mati antara akuisisi dan historian.
	///
	/// Inilah komponen yang membuat janji "setiap sampel yang berhasil diakuisisi akan sampai
	/// ke historian" bisa dipertahankan. Tanpanya, database yang sedang restart atau proses
	/// yang mati berarti sampel yang sudah dibaca dari perangkat hilang tanpa jejak — dan
	/// perangkat polling tidak punya cara mengulanginya.
	/// </summary>
	public interface ISampleBuffer : IAsyncDisposable
	{
		/// <summary>Menambahkan sampel. Durabilitasnya mengikuti kebijakan flush implementasi.</summary>
		Task AppendAsync(IReadOnlyList<TagSample> samples, CancellationToken ct = default);

		/// <summary>Mengambil batch berikutnya yang belum dikomit, maksimal <paramref name="maxSamples"/>.</summary>
		Task<SampleBatch> ReadBatchAsync(int maxSamples, CancellationToken ct = default);

		/// <summary>Menandai batch selesai ditulis. Hanya setelah ini isinya boleh dibuang.</summary>
		Task CommitAsync(long commitToken, CancellationToken ct = default);

		/// <summary>Memaksa data yang tertahan di buffer OS turun ke disk.</summary>
		Task FlushAsync(CancellationToken ct = default);

		BufferStats GetStats();
	}
}
