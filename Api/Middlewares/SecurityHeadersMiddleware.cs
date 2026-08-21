namespace Api.Middlewares
{
	/// <summary>
	/// Memasang header keamanan pada setiap respons.
	///
	/// API ini melayani JSON, bukan HTML, jadi sebagian header terdengar tidak relevan —
	/// tapi justru di API-lah header ini murah dan penting: browser tetap bisa dibujuk
	/// merender respons JSON sebagai dokumen (mis. lewat tag <c>&lt;script&gt;</c> atau
	/// pembukaan langsung URL), dan berkas statis yang diunggah pengguna dilayani dari
	/// origin yang sama lewat <c>UseStaticFiles</c>.
	/// </summary>
	public class SecurityHeadersMiddleware
	{
		private readonly RequestDelegate _next;

		public SecurityHeadersMiddleware(RequestDelegate next)
		{
			_next = next;
		}

		public Task InvokeAsync(HttpContext context)
		{
			// Dipasang lewat OnStarting, bukan langsung, supaya header tetap ada pada respons
			// yang DITULIS ULANG di tengah jalan.
			//
			// Ini bukan kehati-hatian teoretis: `ExceptionHandlingMiddleware` memanggil
			// `Response.Clear()` sebelum menulis envelope error, dan Clear() membuang seluruh
			// header yang sudah dipasang. Tanpa OnStarting, setiap respons 500 keluar TANPA
			// satu pun header keamanan — persis pada respons yang paling mungkin dianalisis
			// penyerang. Ditemukan oleh uji middleware, bukan oleh pembacaan kode.
			context.Response.OnStarting(static state =>
			{
				ApplyHeaders((HttpContext)state);
				return Task.CompletedTask;
			}, context);

			return _next(context);
		}

		private static void ApplyHeaders(HttpContext context)
		{
			var headers = context.Response.Headers;

			// Menolak MIME sniffing. Tanpa ini, berkas unggahan bernama .txt yang isinya HTML
			// bisa dieksekusi sebagai HTML di origin API — jalur XSS klasik lewat unggahan.
			headers["X-Content-Type-Options"] = "nosniff";

			// Tidak ada halaman di API ini yang boleh di-frame.
			headers["X-Frame-Options"] = "DENY";

			// Referrer tidak dibocorkan ke origin lain; URL API memuat id sumber daya.
			headers["Referrer-Policy"] = "no-referrer";

			// Browser tidak boleh dibujuk memuat respons ini sebagai sumber daya lintas
			// origin (Spectre-class + pembacaan tak sengaja lewat tag script/img).
			headers["Cross-Origin-Resource-Policy"] = "same-origin";

			// API tidak butuh satu pun kemampuan perangkat.
			headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), usb=(), serial=()";

			// CSP paling ketat yang mungkin untuk endpoint non-HTML: tidak ada yang boleh
			// dimuat, dan dokumennya tidak boleh di-frame. Berlaku juga saat berkas
			// unggahan dilayani dari origin ini.
			headers["Content-Security-Policy"] =
				"default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'; sandbox";

			// Menghapus jejak teknologi server. Bukan pertahanan sejati, tapi menghilangkan
			// petunjuk gratis untuk pemindai otomatis.
			headers.Remove("Server");
			headers.Remove("X-Powered-By");
		}
	}

	public static class SecurityHeadersMiddlewareExtensions
	{
		public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder builder)
			=> builder.UseMiddleware<SecurityHeadersMiddleware>();
	}
}
