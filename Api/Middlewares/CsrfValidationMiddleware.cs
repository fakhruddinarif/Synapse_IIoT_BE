using Core.Interface;
using System.Text.Json;
using Core.DTOs;

namespace Api.Middlewares
{
	public class CsrfValidationMiddleware
	{
		private readonly RequestDelegate _next;
		private static readonly string[] _excludedPaths = 
		{
			"/api/csrf-token",
			"/api/auth/login",
			"/api/auth/logout",
			"/api/auth/info"
		};

		public CsrfValidationMiddleware(RequestDelegate next)
		{
			_next = next;
		}

		public async Task InvokeAsync(HttpContext context, IAuthService authService)
		{
			var method = context.Request.Method.ToUpper();
			var path = context.Request.Path.Value?.ToLower() ?? string.Empty;

			// Check if this is a mutating request (POST, PUT, PATCH, DELETE)
			var isMutatingRequest = method == "POST" || method == "PUT" || method == "PATCH" || method == "DELETE";

			// Check if the path is excluded from CSRF validation
			var isExcludedPath = _excludedPaths.Any(p => path.StartsWith(p.ToLower()));

			if (isMutatingRequest && !isExcludedPath)
			{
				// Get CSRF token from header
				var csrfToken = context.Request.Headers["X-CSRF-TOKEN"].FirstOrDefault();

				// Validate CSRF token
				if (string.IsNullOrEmpty(csrfToken) || !authService.ValidateCsrfToken(csrfToken))
				{
					context.Response.StatusCode = StatusCodes.Status403Forbidden;
					context.Response.ContentType = "application/json";

					var errorResponse = ApiResponse<object>.Fail(403, "Invalid or missing CSRF token");

					await context.Response.WriteAsync(JsonSerializer.Serialize(errorResponse));
					return;
				}
			}

			await _next(context);
		}
	}

	public static class CsrfValidationMiddlewareExtensions
	{
		public static IApplicationBuilder UseCsrfValidation(this IApplicationBuilder builder)
		{
			return builder.UseMiddleware<CsrfValidationMiddleware>();
		}
	}
}
