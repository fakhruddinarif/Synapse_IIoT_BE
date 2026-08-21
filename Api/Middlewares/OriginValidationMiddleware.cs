using System.Text.Json;
using Core.DTOs;
using Core.Security;
using Microsoft.Extensions.Options;

namespace Api.Middlewares
{
	/// <summary>
	/// Pertahanan CSRF untuk autentikasi berbasis cookie.
	///
	/// KENAPA INI, BUKAN TOKEN CSRF:
	///
	/// Sesi Synapse memakai cookie HTTP-only. Cookie ikut terkirim otomatis oleh browser,
	/// jadi tanpa pertahanan tambahan, situs lain yang dibuka operator di tab sebelah bisa
	/// membuat browsernya mengirim <c>DELETE /api/device/{id}</c> dengan cookie yang sah.
	///
	/// Token CSVF ganda (double-submit) menuntut endpoint pembagi token, penyimpanan sisi
	/// server, dan penanganan kedaluwarsa — dan implementasi lamanya di repo ini memang
	/// akhirnya dinonaktifkan seluruhnya (<c>CsrfValidationMiddleware</c> tidak pernah
	/// dipasang di pipeline). Pertahanan yang dinonaktifkan bernilai nol.
	///
	/// Validasi <c>Origin</c> memberi jaminan yang setara untuk API JSON, tanpa state:
	/// browser <b>selalu</b> mengirim header <c>Origin</c> pada request lintas situs yang
	/// mengubah data, dan halaman penyerang tidak bisa memalsukannya. Dipadukan dengan
	/// cookie <c>SameSite=Strict</c>, penyerang harus terlebih dulu menguasai salah satu
	/// origin yang diizinkan — yang berarti pertahanan lain sudah jebol lebih dulu.
	/// </summary>
	public class OriginValidationMiddleware
	{
		private readonly RequestDelegate _next;
		private readonly HashSet<string> _allowedOrigins;
		private readonly ILogger<OriginValidationMiddleware> _logger;

		private static readonly HashSet<string> SafeMethods = new(StringComparer.OrdinalIgnoreCase)
		{
			"GET", "HEAD", "OPTIONS", "TRACE"
		};

		public OriginValidationMiddleware(
			RequestDelegate next,
			IOptions<SecuritySettings> settings,
			ILogger<OriginValidationMiddleware> logger)
		{
			_next = next;
			_logger = logger;
			_allowedOrigins = new HashSet<string>(
				settings.Value.AllowedOrigins.Select(o => o.TrimEnd('/')),
				StringComparer.OrdinalIgnoreCase);
		}

		public async Task InvokeAsync(HttpContext context)
		{
			// Metode aman tidak mengubah apa pun, jadi tidak perlu dijaga — dan menjaganya
			// akan mematahkan pembukaan URL berkas unggahan langsung dari browser.
			if (SafeMethods.Contains(context.Request.Method))
			{
				await _next(context);
				return;
			}

			var origin = context.Request.Headers.Origin.FirstOrDefault();

			if (string.IsNullOrEmpty(origin))
			{
				// Tanpa Origin: bisa berarti klien non-browser (Postman, skrip, perangkat)
				// yang memang tidak terkena CSRF, ATAU browser lama. Karena permintaan
				// non-browser tidak membawa cookie sesi, keputusannya digantung pada ada
				// tidaknya cookie: ada cookie tanpa Origin adalah kombinasi yang tidak
				// dihasilkan browser modern pada request lintas situs.
				if (!context.Request.Cookies.ContainsKey("JWT-TOKEN"))
				{
					await _next(context);
					return;
				}

				// Referer dipakai sebagai cadangan sebelum menolak.
				var referer = context.Request.Headers.Referer.FirstOrDefault();
				if (!string.IsNullOrEmpty(referer) &&
					Uri.TryCreate(referer, UriKind.Absolute, out var refererUri) &&
					_allowedOrigins.Contains($"{refererUri.Scheme}://{refererUri.Authority}"))
				{
					await _next(context);
					return;
				}

				await RejectAsync(context, "Permintaan yang mengubah data harus menyertakan header Origin.");
				return;
			}

			if (!_allowedOrigins.Contains(origin.TrimEnd('/')))
			{
				_logger.LogWarning(
					"Permintaan {Method} {Path} ditolak: origin {Origin} tidak diizinkan",
					context.Request.Method, context.Request.Path, origin);

				await RejectAsync(context, "Origin permintaan tidak diizinkan.");
				return;
			}

			await _next(context);
		}

		private static async Task RejectAsync(HttpContext context, string reason)
		{
			context.Response.StatusCode = StatusCodes.Status403Forbidden;
			context.Response.ContentType = "application/json";

			var payload = ApiResponse<object>.Fail(
				StatusCodes.Status403Forbidden,
				"Permintaan ditolak",
				reason);

			// Null tetap ditulis: kelima field envelope selalu hadir, sama seperti respons MVC.
			await context.Response.WriteAsync(JsonSerializer.Serialize(payload, new JsonSerializerOptions
			{
				PropertyNamingPolicy = JsonNamingPolicy.CamelCase
			}));
		}
	}

	public static class OriginValidationMiddlewareExtensions
	{
		public static IApplicationBuilder UseOriginValidation(this IApplicationBuilder builder)
			=> builder.UseMiddleware<OriginValidationMiddleware>();
	}
}
