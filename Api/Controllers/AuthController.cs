using Core.DTOs;
using Core.Interface;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Api.Controllers
{
	[ApiController]
	[Route("api")]
	public class AuthController : ControllerBase
	{
		private readonly IAuthService _authService;

		public AuthController(IAuthService authService)
		{
			_authService = authService;
		}

		/// <summary>
		/// Generate CSRF Token and store in cookie
		/// </summary>
		[HttpGet("csrf-token")]
		public IActionResult GetCsrfToken()
		{
			var token = _authService.GenerateCsrfToken();

			// Set CSRF token in HTTP-only cookie
			Response.Cookies.Append("X-CSRF-TOKEN", token, new CookieOptions
			{
				HttpOnly = true,
				Secure = true, // Only send over HTTPS
				SameSite = SameSiteMode.Strict,
				Expires = DateTimeOffset.UtcNow.AddHours(1)
			});

			var response = new { csrf_token = token };

			return Ok(ApiResponse<object>.Success(response, "CSRF token generated successfully"));
		}

		/// <summary>
		/// Register new user
		/// POST /api/auth/register
		/// </summary>
		[HttpPost("auth/register")]
		public async Task<IActionResult> Register([FromBody] RegisterDto dto, [FromHeader(Name = "X-CSRF-TOKEN")] string csrfToken)
		{
			if (!ModelState.IsValid)
			{
				var errors = ModelState.Values.SelectMany(v => v.Errors.Select(e => e.ErrorMessage)).ToList();
				return BadRequest(ApiResponse<object>.Fail(400, "Invalid input", errors));
			}

			// Validate CSRF token
			if (!_authService.ValidateCsrfToken(csrfToken ?? string.Empty))
			{
				return BadRequest(ApiResponse<object>.Fail(400, "Invalid or missing CSRF token"));
			}

			var result = await _authService.RegisterAsync(dto);

			if (result.Status != 201)
			{
				return BadRequest(ApiResponse<UserInfoDto>.Fail(result.Status, result.Message, result.Error));
			}

			return StatusCode(201, ApiResponse<UserInfoDto>.SuccessWithStatus(201, result.Data, result.Message));
		}

		/// <summary>
		/// Login user and generate JWT token
		/// POST /api/auth/login
		/// </summary>
		[HttpPost("auth/login")]
		public async Task<IActionResult> Login([FromBody] LoginDto dto)
		{
			if (!ModelState.IsValid)
			{
				var errors = ModelState.Values.SelectMany(v => v.Errors.Select(e => e.ErrorMessage)).ToList();
				return BadRequest(ApiResponse<object>.Fail(400, "Invalid input", errors));
			}

			var (success, message, userInfo, token) = await _authService.LoginAsync(dto);

			if (!success || token == null)
			{
				return Unauthorized(ApiResponse<object>.Fail(401, message));
			}

			// Set JWT token in HTTP-only cookie
			Response.Cookies.Append("JWT-TOKEN", token, new CookieOptions
			{
				HttpOnly = true,
				Secure = true, // Only send over HTTPS
				SameSite = SameSiteMode.Strict,
				Expires = DateTimeOffset.UtcNow.AddHours(1)
			});

			return Ok(ApiResponse<UserInfoDto>.Success(userInfo, message));
		}

		/// <summary>
		/// Get current logged-in user info
		/// GET /api/auth/info
		/// Requires authentication
		/// </summary>
		[Authorize]
		[HttpGet("auth/info")]
		public async Task<IActionResult> GetUserInfo()
		{
			// Get userId from JWT claims
			var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
				?? User.FindFirst("sub")?.Value;

			if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
			{
				return Unauthorized(ApiResponse<object>.Fail(401, "Invalid token"));
			}

			var userInfo = await _authService.GetUserInfoAsync(userId);

			if (userInfo == null)
			{
				return NotFound(ApiResponse<object>.Fail(404, "User not found"));
			}

			return Ok(ApiResponse<UserInfoDto>.Success(userInfo, "User info retrieved successfully"));
		}

		/// <summary>
		/// Logout user by removing JWT token cookie
		/// POST /api/auth/logout
		/// </summary>
		[HttpPost("auth/logout")]
		public IActionResult Logout()
		{
			// Delete JWT-TOKEN cookie
			Response.Cookies.Delete("JWT-TOKEN", new CookieOptions
			{
				HttpOnly = true,
				Secure = true,
				SameSite = SameSiteMode.Strict
			});

			return Ok(ApiResponse<object>.Success(null, "Logged out successfully"));
		}
	}
}
