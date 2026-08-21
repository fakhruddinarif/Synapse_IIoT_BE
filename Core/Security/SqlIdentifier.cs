using System.Text.RegularExpressions;

namespace Core.Security
{
	/// <summary>
	/// Validasi identifier SQL (nama tabel dan nama kolom) untuk fitur tabel dinamis.
	///
	/// KENAPA INI ADA, dan kenapa tidak boleh dilewati:
	///
	/// Fitur tabel dinamis menyusun <c>CREATE TABLE</c> dan <c>ALTER TABLE</c> dari nama
	/// yang DIKETIK PENGGUNA. Nama tabel dan nama kolom tidak bisa diparameterkan — tidak
	/// ada database yang menerima <c>CREATE TABLE @name</c> — sehingga satu-satunya
	/// pertahanan adalah menolak identifier yang tidak berbentuk identifier.
	///
	/// Tanpa penyaringan ini, nama tabel <c>x`; DROP TABLE users; --</c> akan tereksekusi
	/// apa adanya oleh seorang ENGINEER yang berwenang membuat tabel, tapi tidak
	/// berwenang menghapus tabel pengguna.
	///
	/// Pendekatannya <b>allowlist</b>, bukan denylist: hanya huruf kecil, angka, dan garis
	/// bawah, wajib dimulai huruf. Denylist karakter berbahaya selalu ketinggalan satu
	/// karakter dari kenyataan (backtick, kutip ganda, kurung siku, komentar, NUL, unicode
	/// homoglyph), sementara allowlist tidak bisa dilampaui.
	/// </summary>
	public static class SqlIdentifier
	{
		/// <summary>Huruf kecil di awal, lalu huruf kecil/angka/garis bawah. 2–63 karakter
		/// (63 adalah batas identifier PostgreSQL).</summary>
		private static readonly Regex Pattern = new(
			@"^[a-z][a-z0-9_]{1,62}$",
			RegexOptions.Compiled | RegexOptions.CultureInvariant);

		/// <summary>
		/// Kata yang dicadangkan di MySQL maupun PostgreSQL. Memakainya sebagai nama tabel
		/// atau kolom secara teknis mungkin (dengan kuoting), tapi membuat setiap kueri
		/// manual di masa depan gagal dengan pesan yang membingungkan.
		/// </summary>
		private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
		{
			"select", "insert", "update", "delete", "drop", "create", "alter", "table",
			"from", "where", "join", "union", "order", "group", "by", "having", "into",
			"values", "set", "index", "view", "user", "grant", "revoke", "database",
			"schema", "column", "constraint", "primary", "foreign", "key", "unique",
			"default", "null", "not", "and", "or", "as", "on", "in", "is", "like",
			"between", "exists", "all", "any", "case", "when", "then", "else", "end",
			"limit", "offset", "desc", "asc", "distinct", "count", "sum", "avg", "min",
			"max", "true", "false", "check", "references", "cascade", "with"
		};

		/// <summary>
		/// Prefiks yang dipakai sistem sendiri. Tabel dinamis tidak boleh menyerobotnya,
		/// kalau tidak pengguna bisa membuat tabel bernama <c>tags</c> dan menimpa tabel
		/// konfigurasi.
		/// </summary>
		private static readonly HashSet<string> SystemTables = new(StringComparer.Ordinal)
		{
			"users", "devices", "tags", "master_tables", "master_table_fields",
			"storage_flows", "storage_flow_devices", "storage_flow_mappings",
			"audit_logs", "file_metadata", "scan_classes", "alarm_rules",
			"alarm_events", "tag_current", "tag_history", "acquisition_gap",
			"__efmigrationshistory"
		};

		/// <summary>
		/// <c>true</c> bila identifier berbentuk sah. Tidak melihat kata cadangan maupun
		/// tabel sistem — pakai <see cref="ValidateTableName"/> atau
		/// <see cref="ValidateColumnName"/> untuk pemeriksaan lengkap.
		/// </summary>
		public static bool IsValidShape(string? identifier)
			=> !string.IsNullOrWhiteSpace(identifier) && Pattern.IsMatch(identifier);

		/// <summary>
		/// Memeriksa nama tabel fisik. Mengembalikan pesan kesalahan siap tampil, atau
		/// <c>null</c> bila sah.
		/// </summary>
		public static string? ValidateTableName(string? tableName)
		{
			if (string.IsNullOrWhiteSpace(tableName))
				return "Nama tabel fisik wajib diisi.";

			if (!Pattern.IsMatch(tableName))
				return "Nama tabel hanya boleh huruf kecil, angka, dan garis bawah, dimulai huruf, panjang 2–63 karakter.";

			if (Reserved.Contains(tableName))
				return $"'{tableName}' adalah kata cadangan SQL dan tidak bisa dipakai sebagai nama tabel.";

			if (SystemTables.Contains(tableName))
				return $"'{tableName}' dipakai tabel sistem Synapse dan tidak bisa dipakai ulang.";

			return null;
		}

		/// <summary>
		/// Memeriksa nama kolom. Kolom <c>id</c> dan <c>created_at</c> dibuat otomatis oleh
		/// sistem, jadi tidak boleh didefinisikan ulang pengguna.
		/// </summary>
		public static string? ValidateColumnName(string? columnName)
		{
			if (string.IsNullOrWhiteSpace(columnName))
				return "Nama kolom wajib diisi.";

			if (!Pattern.IsMatch(columnName))
				return "Nama kolom hanya boleh huruf kecil, angka, dan garis bawah, dimulai huruf, panjang 2–63 karakter.";

			if (Reserved.Contains(columnName))
				return $"'{columnName}' adalah kata cadangan SQL dan tidak bisa dipakai sebagai nama kolom.";

			if (columnName is "id" or "created_at" or "updated_at")
				return $"Kolom '{columnName}' dibuat otomatis oleh sistem.";

			return null;
		}

		/// <summary>
		/// Mengembalikan identifier yang sudah dipastikan sah, atau melempar. Dipakai TEPAT
		/// di titik penyusunan SQL sebagai jaring terakhir — sehingga tidak ada jalur kode
		/// yang bisa menyusun DDL dari nama yang belum diperiksa, bahkan bila validasi di
		/// lapisan service kelak terlewat pada endpoint baru.
		/// </summary>
		public static string EnsureSafe(string? identifier, string kind = "identifier")
		{
			if (!IsValidShape(identifier))
			{
				throw new ArgumentException(
					$"Nama {kind} tidak sah dan ditolak sebelum menyentuh database: '{identifier}'.",
					nameof(identifier));
			}

			return identifier!;
		}
	}
}
