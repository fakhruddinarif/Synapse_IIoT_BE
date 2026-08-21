using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Entities
{
	/// <summary>
	/// Satu sampel tag yang tersimpan permanen — tabel historian, dan satu-satunya sumber
	/// kebenaran untuk pertanyaan "berapa nilainya kemarin pukul 14.32".
	///
	/// Tabel ini akan menjadi yang terbesar di seluruh sistem: 2.000 tag @ 1 s adalah 172 juta
	/// baris per hari. Karena itu tidak ada satu pun kolom di sini yang boleh sekadar "berguna" —
	/// nama tag, satuan, dan deskripsi tinggal di tabel <see cref="Tag"/> dan digabungkan saat
	/// dibaca, bukan diulang 172 juta kali.
	/// </summary>
	public class TagHistory
	{
		[Key]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public long Id { get; set; }

		[Required]
		public Guid TagId { get; set; }

		/// <summary>
		/// Diduplikasi dari <see cref="Tag"/> dengan sengaja: hampir setiap kueri operasional
		/// dibatasi per perangkat, dan JOIN ke tabel tag pada 172 juta baris untuk menemukan
		/// kolom yang tidak pernah berubah adalah harga yang tidak perlu dibayar.
		/// </summary>
		[Required]
		public Guid DeviceId { get; set; }

		/// <summary>
		/// Waktu menurut SUMBER data — kapan nilai itu benar terjadi di lapangan.
		/// Inilah sumbu waktu yang dipakai semua grafik dan laporan.
		/// </summary>
		[Required]
		public DateTime SourceTs { get; set; }

		/// <summary>
		/// Waktu saat gateway menerimanya. Bedanya dengan <see cref="SourceTs"/> adalah latensi
		/// nyata sistem, dan pada data yang menyusul setelah koneksi pulih bedanya bisa berjam-jam.
		/// Menyimpan keduanya membuat pertanyaan "kenapa data ini baru muncul sekarang" bisa
		/// dijawab, bukan diperdebatkan.
		/// </summary>
		[Required]
		public DateTime GatewayTs { get; set; }

		public double? NumericValue { get; set; }
		public bool? BoolValue { get; set; }

		[MaxLength(500)]
		public string? TextValue { get; set; }

		/// <summary>Nilai sebelum penskalaan. Disimpan supaya kesalahan parameter penskalaan bisa
		/// diperbaiki tanpa kehilangan data — tanpa ini, satu salah ketik pada RawMax berarti
		/// riwayat yang salah selamanya.</summary>
		public double? RawValue { get; set; }

		/// <summary>0 = Good, 1 = Uncertain, 2 = Bad, 3 = Stale.</summary>
		public byte Quality { get; set; }

		[MaxLength(255)]
		public string? Note { get; set; }
	}
}
