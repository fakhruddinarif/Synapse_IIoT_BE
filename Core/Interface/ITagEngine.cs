using Core.Acquisition;

namespace Core.Interface
{
	/// <summary>Nilai sekarang satu tag, untuk dasbor dan API nilai-terakhir.</summary>
	public sealed record TagSnapshot
	{
		public required Guid TagId { get; init; }
		public required Guid DeviceId { get; init; }
		public required TagSample Sample { get; init; }

		/// <summary>Nomor urut monoton per gateway. Klien memakainya untuk mendeteksi frame
		/// yang terlewat dan menambalnya lewat kueri historis.</summary>
		public required long Seq { get; init; }
	}

	/// <summary>
	/// Basis data nilai-sekarang (RTDB) dan satu-satunya tempat keputusan berikut diambil:
	/// penskalaan raw→EU, penilaian quality, apakah sampel layak disimpan, dan penomoran urut.
	///
	/// Alasan semuanya dipusatkan di sini: sebelumnya penskalaan hanya terjadi di jalur Modbus
	/// tiruan dan hanya untuk tipe FLOAT, sehingga tag yang sama menghasilkan angka berbeda
	/// tergantung protokol yang membacanya. Aturan yang berlaku untuk semua tag harus tinggal
	/// di satu tempat, bukan tersebar di setiap driver.
	/// </summary>
	public interface ITagEngine
	{
		/// <summary>
		/// Memproses satu sampel mentah dari driver: menskalakan, menilai quality, memberi
		/// nomor urut, memperbarui nilai sekarang, dan memutuskan apakah ia harus disimpan.
		/// Mengembalikan sampel final beserta keputusan simpannya.
		/// </summary>
		(TagSample Sample, bool ShouldStore, long Seq) Process(TagPlan plan, TagSample raw);

		TagSnapshot? GetSnapshot(Guid tagId);
		IReadOnlyCollection<TagSnapshot> GetSnapshots();

		/// <summary>Menandai seluruh tag perangkat sebagai Bad — dipakai saat koneksi hilang,
		/// supaya dasbor tidak terus menampilkan nilai terakhir seolah masih hidup.</summary>
		void MarkDeviceBad(Guid deviceId, IEnumerable<TagPlan> tags, string reason);

		/// <summary>Melupakan tag yang dihapus dari konfigurasi.</summary>
		void Forget(IEnumerable<Guid> tagIds);
	}
}
