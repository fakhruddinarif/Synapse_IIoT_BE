using System.Security.Claims;
using Core.DTOs;
using Core.Interface;
using Core.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Api.Controllers
{
	[ApiController]
	[Route("api")]
	public class AuthController : ControllerBase
	{
		private const string SessionCookieName = "JWT-TOKEN";

		private readonly IAuthService _authService;
		private readonly ILoginThrottle _loginThrottle;
		private readonly SecuritySettings _security;
		private readonly int _sessionMinutes;
		private readonly ILogger<AuthController> _logger;

		public AuthController(
			IAuthService authService,
			ILoginThrottle loginThrottle,
			IOptions<SecuritySettings> security,
			IConfiguration configuration,
			ILogger<AuthController> logger)
		{
			_authService = authService;
			_loginThrottle = loginThrottle;
			_security = security.Value;
			_sessionMinutes = int.TryParse(configuration["JwtSettings:ExpirationInMinutes"], out var minutes)
				? minutes
				: 60;
			_logger = logger;
		}

		/* ------------------------------------------------------------ register */

		/// <summary>
		/// Mendaftarkan pengguna baru.
		///
		/// Registrasi TIDAK memasang cookie sesi: pendaftaran dan pemberian sesi adalah dua
		/// keputusan berbeda, dan memisahkannya membuat alur persetujuan admin (bila kelak
		/// dibutuhkan) tidak perlu membongkar endpoint ini.
		/// </summary>
		[HttpPost("auth/register")]
		[AllowAnonymous]
		[EnableRateLimiting("Login")]
		public async Task<IActionResult> Register([FromBody] RegisterDto dto)
		{
			if (!ModelState.IsValid) return InvalidModelState();

			var result = await _authService.RegisterAsync(dto);

			if (result.Status is not (200 or 201))
			{
				return StatusCode(result.Status,
					ApiResponse<UserInfoDto>.Fail(result.Status, result.Message, result.Errors));
			}

			_logger.LogInformation("User baru terdaftar: {Username}", dto.Username);

			return StatusCode(201,
				ApiResponse<UserInfoDto>.SuccessWithStatus(201, result.Data, result.Message));
		}

		/* --------------------------------------------------------------- login */

		/// <summary>
		/// Login. Token dikirim sebagai cookie HTTP-only, tidak pernah di body respons —
		/// token yang bisa dibaca JavaScript berarti satu XSS mana pun cukup untuk mencuri
		/// sesi.
		/// </summary>
		[HttpPost("auth/login")]
		[AllowAnonymous]
		[EnableRateLimiting("Login")]
		public async Task<IActionResult> Login([FromBody] LoginDto dto)
		{
			if (!ModelState.IsValid) return InvalidModelState();

			var ip = HttpContext.Connection.RemoteIpAddress?.ToString();

			// Penguncian diperiksa SEBELUM password diverifikasi. Kalau diperiksa sesudah,
			// setiap percobaan tetap menghabiskan satu perhitungan BCrypt dan penyerang
			// mendapat pembayaran gratis atas serangannya.
			var (isLocked, retryAfter) = _loginThrottle.Check(dto.Username, ip);
			if (isLocked)
			{
				Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();

				_logger.LogWarning("Login ditolak (terkunci) untuk {Username} dari {Ip}", dto.Username, ip);

				return StatusCode(429, ApiResponse<object>.Fail(429,
					"Terlalu banyak percobaan login",
					$"Coba lagi dalam {Math.Ceiling(retryAfter.TotalMinutes)} menit."));
			}

			var (success, message, userInfo, token) = await _authService.LoginAsync(dto);

			if (!success || token is null)
			{
				var remaining = _loginThrottle.RegisterFailure(dto.Username, ip);

				// Pesannya tetap sama untuk username tidak dikenal maupun password salah —
				// membedakannya memberi tahu penyerang akun mana yang ada. Sisa percobaan
				// disertakan karena itu informasi tentang PEMANGGIL, bukan tentang akun.
				var errors = new List<string> { message };
				if (remaining is > 0 and <= 2)
				{
					errors.Add($"Sisa {remaining} percobaan sebelum akun dikunci sementara.");
				}

				return Unauthorized(ApiResponse<object>.Fail(401, message, errors));
			}

			_loginThrottle.Reset(dto.Username, ip);
			AppendSessionCookie(token);

			_logger.LogInformation("Login berhasil: {Username} dari {Ip}", dto.Username, ip);

			return Ok(ApiResponse<UserInfoDto>.Success(userInfo, "Login berhasil"));
		}

		/* ---------------------------------------------------------------- info */

		[HttpGet("auth/info")]
		[Authorize]
		public async Task<IActionResult> GetUserInfo()
		{
			var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
				?? User.FindFirst("sub")?.Value;

			if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
			{
				return Unauthorized(ApiResponse<object>.Fail(401, "Sesi tidak valid"));
			}

			var userInfo = await _authService.GetUserInfoAsync(userId);

			if (userInfo is null)
			{
				// Token sah tapi penggunanya sudah tidak ada (dihapus admin lain). Cookie
				// dihapus di sini supaya klien tidak terus mengirim token yang tidak akan
				// pernah berhasil lagi.
				DeleteSessionCookie();
				return Unauthorized(ApiResponse<object>.Fail(401, "Akun tidak ditemukan"));
			}

			return Ok(ApiResponse<UserInfoDto>.Success(userInfo, "Info user berhasil diambil"));
		}

		/* -------------------------------------------------------------- logout */

		/// <summary>
		/// Logout. Dibiarkan anonim: memaksa <c>[Authorize]</c> di sini berarti sesi yang
		/// sudah kedaluwarsa tidak bisa dibersihkan cookienya, dan pengguna terjebak dengan
		/// cookie basi yang membuat setiap request berakhir 401.
		/// </summary>
		[HttpPost("auth/logout")]
		[AllowAnonymous]
		public IActionResult Logout()
		{
			DeleteSessionCookie();
			return Ok(ApiResponse<object>.Success(null, "Berhasil keluar"));
		}

		/* ------------------------------------------------------------- helpers */

		/// <summary>
		/// Opsi cookie sesi. Semua atribut harus IDENTIK antara pemasangan dan penghapusan;
		/// browser mencocokkan cookie berdasarkan nama + domain + path, dan penghapusan
		/// dengan atribut berbeda menyisakan cookie aslinya tetap hidup.
		/// </summary>
		private CookieOptions BuildCookieOptions(DateTimeOffset? expires) => new()
		{
			HttpOnly = true,

			// Wajib true di produksi: cookie tanpa Secure ikut terkirim di HTTP polos, dan
			// siapa pun di jaringan pabrik yang sama bisa membacanya.
			Secure = _security.CookieSecure,

			// Strict berarti cookie tidak pernah dikirim pada permintaan lintas situs —
			// lapisan pertama pertahanan CSRF, dilengkapi validasi Origin di middleware.
			SameSite = _security.CookieSameSite.Equals("Lax", StringComparison.OrdinalIgnoreCase)
				? SameSiteMode.Lax
				: SameSiteMode.Strict,

			Path = "/",
			Expires = expires,

			// Domain sengaja tidak diisi: cookie menjadi host-only, sehingga subdomain lain
			// pada domain yang sama tidak bisa menerimanya.
			IsEssential = true
		};

		private void AppendSessionCookie(string token)
		{
			// Masa hidup cookie disamakan dengan masa hidup token. Cookie yang hidup lebih
			// lama hanya membuat browser terus mengirim token mati; cookie yang lebih pendek
			// membuat sesi berakhir lebih awal dari yang dijanjikan.
			Response.Cookies.Append(
				SessionCookieName,
				token,
				BuildCookieOptions(DateTimeOffset.UtcNow.AddMinutes(_sessionMinutes)));
		}

		private void DeleteSessionCookie()
		{
			Response.Cookies.Delete(SessionCookieName, BuildCookieOptions(null));
		}

		private IActionResult InvalidModelState()
		{
			var errors = ModelState.Values
				.SelectMany(v => v.Errors.Select(e => e.ErrorMessage))
				.Where(m => !string.IsNullOrWhiteSpace(m))
				.ToList();

			return BadRequest(ApiResponse<object>.Fail(400, "Input tidak valid", errors));
		}
	}
}
