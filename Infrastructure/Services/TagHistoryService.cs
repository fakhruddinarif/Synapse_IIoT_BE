using Core.DTOs;
using Core.DTOs.Tag;
using Core.Interface;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services
{
	/// <summary>
	/// Jalur baca historian.
	///
	/// Memakai <c>AppDbContext</c> langsung, bukan repository generik: tabel ini akan berisi
	/// ratusan juta baris, dan setiap kueri ke arahnya harus terlihat apa adanya — indeks mana
	/// yang dipakai, berapa baris yang mungkin kembali, dan di mana batasnya. Repository yang
	/// menyembunyikan bentuk kuerinya adalah tempat paling umum lahirnya <c>SELECT *</c> atas
	/// tabel sebesar ini.
	/// </summary>
	public class TagHistoryService(
		AppDbContext db,
		ITagEngine engine,
		ILogger<TagHistoryService> logger) : ITagHistoryService
	{
		public async Task<ApiResponse<TagHistoryDto>> GetAsync(Guid tagId, TagHistoryQueryDto query)
		{
			try
			{
				var tag = await db.Tags
					.AsNoTracking()
					.Where(t => t.Id == tagId && t.DeletedAt == null)
					.Select(t => new { t.Id, t.Name, t.Unit })
					.FirstOrDefaultAsync();

				if (tag is null) return ApiResponse<TagHistoryDto>.Fail(404, "Tag tidak ditemukan");

				var to = query.To ?? DateTime.UtcNow;
				var from = query.From ?? to.AddHours(-1);

				if (from >= to)
					return ApiResponse<TagHistoryDto>.Fail(400, "Rentang waktu tidak valid: 'from' harus sebelum 'to'");

				// Batas keras di server. Klien yang meminta 10 juta titik tidak akan pernah bisa
				// menampilkannya, tetapi permintaannya cukup untuk menghabiskan memori gateway.
				var limit = Math.Clamp(query.Limit, 1, 20_000);

				var q = db.TagHistories
					.AsNoTracking()
					.Where(h => h.TagId == tagId && h.SourceTs >= from && h.SourceTs <= to);

				if (query.GoodOnly) q = q.Where(h => h.Quality == 0);

				// Satu titik lebih dari batas, hanya untuk mengetahui apakah rentangnya terpotong.
				// Tanpa itu, "tepat 2.000 titik" tidak bisa dibedakan dari "terpotong di 2.000".
				var points = await q
					.OrderBy(h => h.SourceTs)
					.Take(limit + 1)
					.Select(h => new TagHistoryPointDto
					{
						SourceTs = h.SourceTs,
						Numeric = h.NumericValue,
						Boolean = h.BoolValue,
						Text = h.TextValue,
						Quality = h.Quality
					})
					.ToListAsync();

				var truncated = points.Count > limit;
				if (truncated) points.RemoveAt(points.Count - 1);

				return ApiResponse<TagHistoryDto>.Success(new TagHistoryDto
				{
					TagId = tag.Id,
					TagName = tag.Name,
					Unit = tag.Unit ?? string.Empty,
					From = from,
					To = to,
					Count = points.Count,
					IsTruncated = truncated,
					Points = points
				}, truncated
					? $"{points.Count} titik (rentang terpotong pada batas {limit})"
					: $"{points.Count} titik riwayat");
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Gagal mengambil riwayat tag {TagId}", tagId);
				return ApiResponse<TagHistoryDto>.Fail(500, "Gagal mengambil riwayat tag");
			}
		}

		public ApiResponse<List<TagCurrentValueDto>> GetCurrentValues(Guid? deviceId)
		{
			var snapshots = engine.GetSnapshots()
				.Where(s => deviceId is null || s.DeviceId == deviceId)
				.Select(s => new TagCurrentValueDto
				{
					TagId = s.TagId,
					DeviceId = s.DeviceId,
					Numeric = s.Sample.Numeric,
					Boolean = s.Sample.Boolean,
					Text = s.Sample.Text,
					Quality = (byte)s.Sample.Quality,
					SourceTs = s.Sample.SourceTs,
					Seq = s.Seq
				})
				.ToList();

			return ApiResponse<List<TagCurrentValueDto>>.Success(
				snapshots, $"{snapshots.Count} nilai sekarang");
		}
	}
}
