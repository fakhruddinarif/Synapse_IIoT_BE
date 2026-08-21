using Core.Acquisition;

namespace Core.Interface
{
	/// <summary>
	/// Sumber rencana akuisisi. Dipisahkan dari worker supaya penjadwal bisa diuji tanpa
	/// database — dan supaya asal rencana bisa berubah (DB, berkas, API) tanpa menyentuh
	/// logika penjadwalan.
	/// </summary>
	public interface IAcquisitionPlanSource
	{
		/// <summary>
		/// Seluruh rencana yang aktif. Dipanggil ulang setiap kali konfigurasi berubah;
		/// menyusun ulang seluruhnya jauh lebih sulit salah daripada menambal rencana yang
		/// ada, dan biayanya kecil.
		/// </summary>
		Task<IReadOnlyList<DevicePlan>> GetActivePlansAsync(CancellationToken ct);
	}

	/// <summary>
	/// Pengirim aliran realtime ke klien. Antarmuka, bukan <c>IHubContext</c> langsung, supaya
	/// penggabungan frame (coalescing) bisa diuji tanpa menyalakan SignalR.
	/// </summary>
	public interface IRealtimePublisher
	{
		/// <summary>
		/// Mengirim satu frame berisi banyak sampel ke grup perangkat.
		/// </summary>
		Task PublishAsync(Guid deviceId, RealtimeFrame frame, CancellationToken ct);
	}

	/// <summary>
	/// Satu frame realtime — banyak tag sekaligus, bukan satu pesan per tag.
	///
	/// Pada 1.000 tag @ 1 s, satu pesan per pembacaan berarti ribuan pesan kecil per detik ke
	/// setiap tab yang terbuka. Frame gabungan menurunkannya ke 2–4 pesan per detik tanpa
	/// kehilangan satu pun nilai yang bisa dilihat mata.
	/// </summary>
	public sealed record RealtimeFrame
	{
		public required Guid DeviceId { get; init; }
		public required string DeviceName { get; init; }

		/// <summary>Nomor urut frame per gateway. Klien menyimpan yang terakhir; loncatan
		/// berarti ada frame yang terlewat dan harus ditambal lewat kueri historis.</summary>
		public required long Seq { get; init; }

		public required DateTime Ts { get; init; }

		/// <summary>Nilai terkompaksi: <c>[tagId, value, quality]</c>. Nama tag tidak diulang
		/// karena payload ini dikirim ribuan kali lebih sering daripada metadatanya.</summary>
		public required IReadOnlyList<RealtimeValue> Values { get; init; }
	}

	public readonly record struct RealtimeValue
	{
		public required Guid TagId { get; init; }
		public double? Numeric { get; init; }
		public bool? Boolean { get; init; }
		public string? Text { get; init; }
		public required byte Quality { get; init; }
	}

	/// <summary>
	/// Penulis historian. Worker tidak pernah menulis ke database langsung — ia menulis ke
	/// buffer tahan-mati, dan implementasi ini yang mengurasnya.
	/// </summary>
	public interface ISampleWriter
	{
		/// <summary>
		/// Menulis satu batch secara idempoten. Mengembalikan <c>true</c> hanya bila seluruh
		/// batch tersimpan — buffer memakai nilai itu untuk memutuskan boleh tidaknya
		/// memangkas isinya, jadi <c>true</c> yang keliru berarti kehilangan data.
		/// </summary>
		Task<bool> WriteAsync(IReadOnlyList<TagSample> samples, CancellationToken ct);
	}

	/// <summary>
	/// Catatan jeda akuisisi. Inilah yang mengubah "data hilang" menjadi "data tidak ada, dan
	/// ini sebabnya" — bagian yang paling dibutuhkan saat hasil produksi dipertanyakan.
	/// </summary>
	public interface IGapLedger
	{
		Task OpenAsync(Guid deviceId, string deviceName, DateTime from, string reason, CancellationToken ct);
		Task CloseAsync(Guid deviceId, DateTime to, CancellationToken ct);
	}
}
