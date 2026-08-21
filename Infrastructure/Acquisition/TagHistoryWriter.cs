using System.Text;
using Core.Acquisition;
using Core.Interface;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Acquisition
{
	/// <summary>
	/// Menulis batch sampel ke tabel historian dengan satu pernyataan SQL.
	///
	/// KENAPA SQL MENTAH DAN BUKAN <c>SaveChanges</c>:
	///
	/// EF Core melacak setiap entity yang ditambahkan dan menghasilkan satu pernyataan per baris
	/// pada provider tanpa dukungan batch penuh. Pada 2.000 sampel per detik itu berarti 2.000
	/// perjalanan pulang-balik per detik dan pelacakan perubahan atas objek yang tidak akan
	/// pernah diubah. Satu pernyataan berisi 500 baris menyelesaikan pekerjaan yang sama dengan
	/// satu perjalanan.
	///
	/// SEMUA NILAI TETAP PARAMETER, tidak pernah dirangkai ke dalam string. Sampel membawa teks
	/// yang berasal dari perangkat di lapangan; perangkat yang disusupi atau firmware yang
	/// ngawur akan mengirim tanda kutip, dan jalur historian adalah jalur paling panas di
	/// seluruh sistem — tempat terakhir yang boleh punya lubang injeksi.
	/// </summary>
	public sealed class TagHistoryWriter(
		IServiceScopeFactory scopeFactory,
		ILogger<TagHistoryWriter> logger) : ISampleWriter
	{
		private const int ColumnsPerRow = 10;

		public async Task<bool> WriteAsync(IReadOnlyList<TagSample> samples, CancellationToken ct)
		{
			if (samples.Count == 0) return true;

			try
			{
				using var scope = scopeFactory.CreateScope();
				var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

				var isPostgres = db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;
				var (sql, parameters) = BuildInsert(samples, isPostgres);

				await db.Database.ExecuteSqlRawAsync(sql, parameters, ct);
				return true;
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception ex)
			{
				// false, bukan exception: pemanggil (drain) tidak boleh mengomit WAL, dan harus
				// mencoba lagi. Melempar ke atas akan menghasilkan hasil yang sama tetapi
				// menyembunyikan bahwa kegagalan ini normal dan sudah diperhitungkan.
				logger.LogError(ex, "Gagal menulis {Count} sampel ke historian", samples.Count);
				return false;
			}
		}

		/// <summary>
		/// Menyusun satu INSERT multi-baris yang idempoten. Perbedaan dialek dibatasi pada klausa
		/// terakhir saja, supaya perpindahan ke PostgreSQL tidak menyentuh apa pun selain ini.
		/// </summary>
		private static (string Sql, object[] Parameters) BuildInsert(IReadOnlyList<TagSample> samples, bool isPostgres)
		{
			var quote = isPostgres ? "\"" : "`";
			var sb = new StringBuilder(samples.Count * 64);
			var parameters = new object[samples.Count * ColumnsPerRow];

			sb.Append("INSERT INTO ").Append(quote).Append("TagHistories").Append(quote).Append(" (")
				.Append(Col(quote, "TagId")).Append(", ")
				.Append(Col(quote, "DeviceId")).Append(", ")
				.Append(Col(quote, "SourceTs")).Append(", ")
				.Append(Col(quote, "GatewayTs")).Append(", ")
				.Append(Col(quote, "NumericValue")).Append(", ")
				.Append(Col(quote, "BoolValue")).Append(", ")
				.Append(Col(quote, "TextValue")).Append(", ")
				.Append(Col(quote, "RawValue")).Append(", ")
				.Append(Col(quote, "Quality")).Append(", ")
				.Append(Col(quote, "Note"))
				.Append(") VALUES ");

			for (var i = 0; i < samples.Count; i++)
			{
				var s = samples[i];
				var b = i * ColumnsPerRow;

				if (i > 0) sb.Append(", ");
				sb.Append('(');
				for (var c = 0; c < ColumnsPerRow; c++)
				{
					if (c > 0) sb.Append(", ");
					sb.Append('{').Append(b + c).Append('}');
				}
				sb.Append(')');

				parameters[b + 0] = s.TagId;
				parameters[b + 1] = s.DeviceId;
				parameters[b + 2] = s.SourceTs;
				parameters[b + 3] = s.GatewayTs;
				parameters[b + 4] = (object?)s.Numeric ?? DBNull.Value;
				parameters[b + 5] = (object?)s.Boolean ?? DBNull.Value;
				parameters[b + 6] = Truncate(s.Text, 500);
				parameters[b + 7] = (object?)s.Raw ?? DBNull.Value;
				parameters[b + 8] = (byte)s.Quality;
				parameters[b + 9] = Truncate(s.Note, 255);
			}

			// Idempotensi. Batch yang sama boleh datang dua kali setelah proses mati di antara
			// "historian menerima" dan "WAL dikomit"; yang kedua harus menjadi operasi kosong,
			// bukan baris kembar dan bukan galat yang menghentikan drain.
			sb.Append(isPostgres
				? " ON CONFLICT (\"TagId\", \"SourceTs\") DO NOTHING"
				: " ON DUPLICATE KEY UPDATE `Id` = `Id`");

			return (sb.ToString(), parameters);
		}

		private static string Col(string quote, string name) => quote + name + quote;

		private static object Truncate(string? value, int max)
		{
			if (string.IsNullOrEmpty(value)) return DBNull.Value;

			// Dipotong, bukan ditolak: nilai teks yang kepanjangan dari perangkat tidak boleh
			// menggagalkan seluruh batch berisi 499 sampel lain yang sehat.
			return value.Length <= max ? value : value[..max];
		}
	}
}
