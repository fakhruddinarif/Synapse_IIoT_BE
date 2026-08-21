using Core.DTOs;
using Core.DTOs.Tag;

namespace Core.Interface
{
	public interface ITagHistoryService
	{
		/// <summary>
		/// Riwayat satu tag pada rentang waktu. Inilah jalur baca historian — pasangan dari
		/// jalur tulisnya, dan tanpanya data yang masuk tidak pernah bisa keluar lagi.
		/// </summary>
		Task<ApiResponse<TagHistoryDto>> GetAsync(Guid tagId, TagHistoryQueryDto query);

		/// <summary>
		/// Nilai sekarang seluruh tag (atau satu perangkat) dari RTDB, bukan dari database.
		///
		/// Dasbor yang baru dibuka butuh angka SEKARANG, sebelum frame realtime berikutnya
		/// datang. Mengambilnya dari historian berarti kueri berat ke tabel terbesar hanya untuk
		/// mendapatkan satu baris terakhir per tag; nilai itu sudah ada di memori.
		/// </summary>
		ApiResponse<List<TagCurrentValueDto>> GetCurrentValues(Guid? deviceId);
	}
}
