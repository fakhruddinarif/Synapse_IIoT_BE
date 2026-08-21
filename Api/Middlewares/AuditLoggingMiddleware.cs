using System.Diagnostics;
using System.Security.Claims;
using Core.Entities;
using Core.Interface;

namespace Api.Middlewares
{
	/// <summary>
	/// Mencatat setiap permintaan untuk jejak audit.
	///
	/// Dua hal disengaja dan penting:
	///
	/// <b>1. Body respons TIDAK dicatat.</b> Versi sebelumnya menukar
	/// <c>Response.Body</c> dengan <c>MemoryStream</c>, membaca isinya, lalu menyimpan 500
	/// karakter pertama ke kolom audit. Akibatnya jejak audit ikut menyimpan data yang
	/// dikembalikan endpoint — termasuk profil pengguna, daftar user, dan isi payload
	/// perangkat — sehingga tabel audit menjadi salinan kedua data sensitif dengan kendali
	/// akses yang lebih longgar. Selain itu penyanggaan tersebut memaksa seluruh respons
	/// masuk memori dan mematikan streaming.
	///
	/// Yang dicatat adalah metadata: siapa, apa, kapan, dari mana, hasilnya apa, dan berapa
	/// lama. Itulah yang dibutuhkan saat menelusuri insiden.
	///
	/// <b>2. <c>_next</c> dipanggil TEPAT sekali.</b> Versi sebelumnya membungkus pemanggilan
	/// <c>_next</c> dan pencatatan audit dalam satu <c>try</c>, lalu memanggil <c>_next</c>
	/// lagi di <c>catch</c>. Kegagalan pencatatan audit — misalnya database sedang restart —
	/// karena itu menjalankan ulang seluruh pipeline, dan satu <c>POST</c> bisa tereksekusi
	/// dua kali.
	/// </summary>
	public class AuditLoggingMiddleware
	{
		private readonly RequestDelegate _next;
		private readonly ILogger<AuditLoggingMiddleware> _logger;
		private readonly IServiceProvider _serviceProvider;

		private static readonly string[] ExcludedPrefixes =
		{
			"/health", "/metrics", "/swagger", "/openapi", "/signalr"
		};

		public AuditLoggingMiddleware(
			RequestDelegate next,
			ILogger<AuditLoggingMiddleware> logger,
			IServiceProvider serviceProvider)
		{
			_next = next;
			_logger = logger;
			_serviceProvider = serviceProvider;
		}

		public async Task InvokeAsync(HttpContext context)
		{
			if (ShouldExclude(context.Request.Path))
			{
				await _next(context);
				return;
			}

			var stopwatch = Stopwatch.StartNew();

			// Di luar try: pipeline harus jalan tepat sekali, apa pun yang terjadi pada
			// pencatatan audit sesudahnya.
			await _next(context);

			stopwatch.Stop();

			try
			{
				await RecordAsync(context, stopwatch.ElapsedMilliseconds);
			}
			catch (Exception ex)
			{
				// Audit yang gagal tidak boleh menggagalkan permintaan yang sudah selesai
				// dilayani — tapi harus terlihat di log, karena audit yang diam-diam mati
				// berarti jejak yang hilang.
				_logger.LogError(ex, "Gagal menulis jejak audit untuk {Method} {Path}",
					context.Request.Method, context.Request.Path);
			}
		}

		private async Task RecordAsync(HttpContext context, long elapsedMs)
		{
			var request = context.Request;
			var statusCode = context.Response.StatusCode;

			// Metode baca yang berhasil adalah mayoritas lalu lintas dasbor dan tidak
			// mengubah apa pun; mencatatnya akan menenggelamkan kejadian yang penting.
			// Yang tetap dicatat: seluruh perubahan data, dan setiap penolakan akses.
			var isMutation = !HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method);
			var isDenied = statusCode is 401 or 403 or 429;

			if (!isMutation && !isDenied) return;

			var auditLog = new AuditLog
			{
				UserId = GetUserId(context.User),
				Action = DetermineAction(request.Method),
				EntityType = ExtractResourceType(request.Path),
				EntityId = ExtractResourceId(request.Path),
				OldValues = null,
				// Ringkasan metadata, BUKAN isi respons.
				NewValues =
					$"{request.Method} {request.Path}{request.QueryString} -> {statusCode} " +
					$"({elapsedMs} ms) ip={GetClientIp(context)} ua={Truncate(request.Headers.UserAgent.ToString(), 80)}",
				CreatedAt = DateTime.UtcNow
			};

			using var scope = _serviceProvider.CreateScope();
			var repository = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();
			await repository.CreateAsync(auditLog);

			if (isDenied)
			{
				_logger.LogWarning(
					"Akses ditolak: [{Method}] {Path} -> {StatusCode} dari {Ip}",
					request.Method, request.Path, statusCode, GetClientIp(context));
			}
		}

		private static bool ShouldExclude(PathString path)
		{
			var value = path.Value ?? string.Empty;
			return ExcludedPrefixes.Any(prefix => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
		}

		/// <summary>
		/// IP klien. <c>X-Forwarded-For</c> hanya dipercaya bila memang ada reverse proxy di
		/// depan; header itu bisa dipalsukan klien, jadi nilai koneksi didahulukan dan
		/// header hanya dicatat sebagai tambahan.
		/// </summary>
		private static string GetClientIp(HttpContext context)
		{
			var remote = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
			var forwarded = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
			return string.IsNullOrWhiteSpace(forwarded) ? remote : $"{remote} (xff:{Truncate(forwarded, 45)})";
		}

		private static Guid? GetUserId(ClaimsPrincipal user)
		{
			var claim = user.FindFirst(ClaimTypes.NameIdentifier) ?? user.FindFirst("sub");
			return claim is not null && Guid.TryParse(claim.Value, out var id) ? id : null;
		}

		private static string DetermineAction(string method) => method switch
		{
			"GET" => "READ",
			"POST" => "CREATE",
			"PUT" or "PATCH" => "UPDATE",
			"DELETE" => "DELETE",
			_ => "UNKNOWN"
		};

		private static string ExtractResourceType(PathString path)
		{
			var value = path.Value?.ToLowerInvariant() ?? string.Empty;

			if (value.Contains("/api/auth")) return "Auth";
			if (value.Contains("/api/device")) return "Device";
			if (value.Contains("/api/tags")) return "Tag";
			if (value.Contains("/api/master-tables")) return "MasterTable";
			if (value.Contains("/api/storage-flow")) return "StorageFlow";
			if (value.Contains("/api/discovery")) return "Discovery";
			if (value.Contains("/api/file")) return "File";
			if (value.Contains("/api/users")) return "User";

			return "Unknown";
		}

		private static Guid ExtractResourceId(PathString path)
		{
			var segments = (path.Value ?? string.Empty).Split('/', StringSplitOptions.RemoveEmptyEntries);

			// Id bisa berada di tengah path (mis. /master-tables/{id}/fields/{fieldId}),
			// jadi seluruh segmen diperiksa dari belakang, bukan hanya yang terakhir.
			for (var i = segments.Length - 1; i >= 0; i--)
			{
				if (Guid.TryParse(segments[i], out var id)) return id;
			}

			return Guid.Empty;
		}

		private static string Truncate(string? value, int max)
		{
			if (string.IsNullOrEmpty(value)) return string.Empty;
			return value.Length <= max ? value : value[..max];
		}
	}

	public static class AuditLoggingMiddlewareExtensions
	{
		public static IApplicationBuilder UseAuditLogging(this IApplicationBuilder builder)
			=> builder.UseMiddleware<AuditLoggingMiddleware>();
	}
}
