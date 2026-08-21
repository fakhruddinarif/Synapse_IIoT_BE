using System.Globalization;
using System.Text;
using Core.Interface;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Acquisition
{
	/// <summary>
	/// Catatan jeda akuisisi, ditulis ke berkas JSONL — bukan ke database.
	///
	/// KENAPA BERKAS, padahal semua metadata lain ada di database:
	///
	/// Penyebab jeda yang paling sering justru database yang tidak bisa dihubungi. Catatan jeda
	/// yang butuh database untuk menulis akan gagal tepat pada saat ia paling dibutuhkan, dan
	/// jeda itu menjadi satu-satunya kejadian yang tidak tercatat di seluruh sistem. Berkas lokal
	/// tetap bisa ditulis saat jaringan, DBMS, atau kredensialnya bermasalah.
	///
	/// Satu baris satu kejadian, append-only: format yang paling sulit dirusak oleh proses yang
	/// mati di tengah penulisan, dan masih bisa dibaca manusia saat hasil produksi
	/// dipertanyakan.
	/// </summary>
	public sealed class FileGapLedger : IGapLedger
	{
		private readonly string _path;
		private readonly ILogger<FileGapLedger>? _logger;
		private readonly SemaphoreSlim _mutex = new(1, 1);

		public FileGapLedger(string path, ILogger<FileGapLedger>? logger = null)
		{
			_path = path;
			_logger = logger;

			var dir = Path.GetDirectoryName(Path.GetFullPath(path));
			if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
		}

		public Task OpenAsync(Guid deviceId, string deviceName, DateTime from, string reason, CancellationToken ct) =>
			AppendAsync(
				$"{{\"kind\":\"gap_open\"," +
				$"\"deviceId\":\"{deviceId}\"," +
				$"\"deviceName\":{Quote(deviceName)}," +
				$"\"from\":\"{Iso(from)}\"," +
				$"\"reason\":{Quote(reason)}," +
				$"\"recordedAt\":\"{Iso(DateTime.UtcNow)}\"}}", ct);

		public Task CloseAsync(Guid deviceId, DateTime to, CancellationToken ct) =>
			AppendAsync(
				$"{{\"kind\":\"gap_close\"," +
				$"\"deviceId\":\"{deviceId}\"," +
				$"\"to\":\"{Iso(to)}\"," +
				$"\"recordedAt\":\"{Iso(DateTime.UtcNow)}\"}}", ct);

		private static string Iso(DateTime value) =>
			value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

		/// <summary>
		/// Menyusun string JSON dengan tangan.
		///
		/// Bentuk barisnya hanya beberapa field tetap, sementara <c>JsonSerializer</c> atas tipe
		/// anonim membutuhkan refleksi — yang dimatikan pada host trimmed/AOT dan MELEMPAR di
		/// sana. Sebuah catatan jeda yang gagal ditulis justru pada host yang paling ketat adalah
		/// kegagalan yang paling mahal: kejadiannya lewat tanpa jejak. Empat baris ini menghapus
		/// seluruh kelas kegagalan itu.
		/// </summary>
		private static string Quote(string? value)
		{
			if (value is null) return "null";

			var sb = new StringBuilder(value.Length + 2);
			sb.Append('"');

			foreach (var c in value)
			{
				switch (c)
				{
					case '"': sb.Append("\\\""); break;
					case '\\': sb.Append("\\\\"); break;
					case '\n': sb.Append("\\n"); break;
					case '\r': sb.Append("\\r"); break;
					case '\t': sb.Append("\\t"); break;
					default:
						// Karakter kendali harus di-escape, kalau tidak satu byte aneh dari pesan
						// galat perangkat membuat seluruh baris tidak bisa diparsing.
						if (char.IsControl(c)) sb.Append("\\u").Append(((int)c).ToString("x4"));
						else sb.Append(c);
						break;
				}
			}

			sb.Append('"');
			return sb.ToString();
		}

		private async Task AppendAsync(string json, CancellationToken ct)
		{
			var line = json + Environment.NewLine;

			await _mutex.WaitAsync(ct);
			try
			{
				// FileOptions.WriteThrough: catatan jeda hanya berguna kalau ia bertahan melewati
				// kejadian yang menyebabkannya — termasuk mati listrik.
				await using var stream = new FileStream(
					_path, FileMode.Append, FileAccess.Write, FileShare.Read,
					bufferSize: 4096, FileOptions.WriteThrough);

				var bytes = Encoding.UTF8.GetBytes(line);
				await stream.WriteAsync(bytes, ct);
				await stream.FlushAsync(ct);
			}
			catch (Exception ex)
			{
				_logger?.LogError(ex, "Gagal menulis catatan jeda ke {Path}", _path);
			}
			finally
			{
				_mutex.Release();
			}
		}
	}
}
