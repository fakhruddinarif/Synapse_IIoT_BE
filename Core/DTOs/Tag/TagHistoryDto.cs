using System.ComponentModel.DataAnnotations;

namespace Core.DTOs.Tag
{
	/// <summary>Satu titik riwayat, sekecil mungkin: payload ini dikirim ribuan titik sekaligus.</summary>
	public class TagHistoryPointDto
	{
		public DateTime SourceTs { get; set; }
		public double? Numeric { get; set; }
		public bool? Boolean { get; set; }
		public string? Text { get; set; }
		public byte Quality { get; set; }
	}

	public class TagHistoryDto
	{
		public Guid TagId { get; set; }
		public string TagName { get; set; } = string.Empty;
		public string Unit { get; set; } = string.Empty;
		public DateTime From { get; set; }
		public DateTime To { get; set; }

		/// <summary>Jumlah titik yang dikembalikan.</summary>
		public int Count { get; set; }

		/// <summary>
		/// Benar bila batas jumlah titik tercapai, sehingga rentang yang diminta TIDAK terwakili
		/// seluruhnya. Tanpa penanda ini grafik akan tampak lengkap padahal terpotong — dan
		/// pembacanya menyimpulkan mesin berhenti pada titik terakhir yang terlihat.
		/// </summary>
		public bool IsTruncated { get; set; }

		public List<TagHistoryPointDto> Points { get; set; } = [];
	}

	public class TagHistoryQueryDto
	{
		/// <summary>Awal rentang. Kosong berarti satu jam terakhir.</summary>
		public DateTime? From { get; set; }

		public DateTime? To { get; set; }

		/// <summary>
		/// Batas jumlah titik. Dibatasi keras di sisi server: satu tag @ 500 ms selama sehari
		/// adalah 172.800 titik, dan mengirimkannya ke peramban akan membekukan tab — bukan
		/// menampilkan grafik.
		/// </summary>
		[Range(1, 20_000)]
		public int Limit { get; set; } = 2_000;

		/// <summary>Hanya kualitas Good (0). Baku false: menyembunyikan titik ber-quality buruk
		/// membuat grafik terlihat mulus justru pada periode yang paling perlu dicurigai.</summary>
		public bool GoodOnly { get; set; }
	}

	/// <summary>Nilai sekarang satu tag, dari RTDB.</summary>
	public class TagCurrentValueDto
	{
		public Guid TagId { get; set; }
		public Guid DeviceId { get; set; }
		public double? Numeric { get; set; }
		public bool? Boolean { get; set; }
		public string? Text { get; set; }

		/// <summary>0 = Good, 1 = Uncertain, 2 = Bad, 3 = Stale. Dikirim sebagai angka, bukan
		/// teks, karena field ini ikut di setiap nilai dan namanya lebih panjang dari isinya.</summary>
		public byte Quality { get; set; }

		public DateTime SourceTs { get; set; }

		/// <summary>Nomor urut pembaruan tag ini. Klien memakainya untuk mengabaikan frame yang
		/// tiba tidak berurutan setelah koneksi pulih.</summary>
		public long Seq { get; set; }
	}
}
