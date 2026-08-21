using Core.Acquisition;
using Core.Enums;

namespace Core.Interface
{
	/// <summary>Kesehatan koneksi driver, untuk halaman kesehatan sistem dan alarm koneksi.</summary>
	public readonly record struct DriverHealth
	{
		public bool IsConnected { get; init; }
		public string? LastError { get; init; }
		public DateTime? LastSuccessAt { get; init; }
		public int ConsecutiveFailures { get; init; }
	}

	/// <summary>
	/// Satu kontrak untuk semua protokol.
	///
	/// KENAPA SATU KONTRAK, padahal tarik (HTTP/Modbus) dan dorong (MQTT/OPC UA subscription)
	/// bekerja sangat berbeda:
	///
	/// Tanpa lapisan ini, setiap protokol baru harus menyentuh scheduler, tag engine, dan
	/// jalur penyimpanan sekaligus — itulah yang membuat <c>DeviceWorkerService</c> tumbuh
	/// menjadi 817 baris untuk satu protokol yang benar-benar jalan. Dengan kontrak ini,
	/// scheduler hanya tahu "baca tag ini", dan driver dorong memenuhi kontrak yang sama
	/// dengan menyerahkan nilai terakhir yang sudah diterimanya dari broker.
	///
	/// Driver bertanggung jawab atas: koneksi, protokol, dan mengubah respons mentah menjadi
	/// <see cref="TagSample"/> ber-quality. Driver TIDAK bertanggung jawab atas: penskalaan
	/// raw→EU, keputusan simpan, penomoran urut, atau penulisan — itu milik tag engine.
	/// </summary>
	public interface IDeviceDriver : IAsyncDisposable
	{
		Protocol Protocol { get; }
		Guid DeviceId { get; }

		/// <summary>
		/// Membuka koneksi. Untuk driver tarik boleh no-op (koneksi dibuat per pembacaan);
		/// untuk driver dorong inilah tempat langganan dipasang.
		/// </summary>
		Task ConnectAsync(CancellationToken ct);

		/// <summary>
		/// Membaca sekumpulan tag SEKALIGUS.
		///
		/// Kolektif, bukan per tag, dan itu keputusan performa yang paling menentukan: satu
		/// endpoint HTTP melayani semua tagnya dalam satu permintaan, dan Modbus menggabungkan
		/// register berdampingan menjadi satu frame. Kontrak per-tag akan memaksa 500
		/// round-trip untuk pekerjaan yang seharusnya 8.
		///
		/// Selalu mengembalikan satu sampel untuk SETIAP tag yang diminta — kegagalan
		/// dilaporkan sebagai sampel ber-quality Bad, bukan sebagai daftar yang lebih pendek.
		/// Daftar yang lebih pendek memaksa pemanggil menebak tag mana yang hilang.
		/// </summary>
		Task<IReadOnlyList<TagSample>> ReadAsync(IReadOnlyList<TagPlan> tags, CancellationToken ct);

		/// <summary>
		/// Memasang langganan untuk driver dorong. No-op untuk driver tarik.
		/// Sampel yang masuk diserahkan lewat <paramref name="onSample"/> segera, tanpa
		/// menunggu tick scheduler.
		/// </summary>
		Task SubscribeAsync(
			IReadOnlyList<TagPlan> tags,
			Func<TagSample, CancellationToken, Task> onSample,
			CancellationToken ct);

		DriverHealth Health { get; }
	}

	/// <summary>
	/// Membuat driver sesuai protokol perangkat. Dipisahkan supaya scheduler tidak pernah
	/// memuat daftar protokol — menambah protokol berarti menambah satu kelas driver dan satu
	/// baris di factory, bukan menyentuh penjadwal.
	/// </summary>
	public interface IDeviceDriverFactory
	{
		IDeviceDriver Create(DevicePlan plan);

		/// <summary>Protokol yang benar-benar punya driver. Dipakai UI untuk menyembunyikan
		/// pilihan yang belum dilayani, alih-alih menawarkannya lalu gagal saat dipakai.</summary>
		IReadOnlyCollection<Protocol> SupportedProtocols { get; }
	}
}
