using System.Security.Claims;
using Core.Entities;
using Core.Interface;

namespace Api.Middlewares
{
	/// <summary>
	/// Middleware for capturing and logging all HTTP requests for compliance and security audit trails.
	/// </summary>
	public class AuditLoggingMiddleware
	{
		private readonly RequestDelegate _next;
		private readonly ILogger<AuditLoggingMiddleware> _logger;
		private readonly IServiceProvider _serviceProvider;

		// Endpoints to exclude from audit logging (health checks, metrics, etc.)
		private static readonly HashSet<string> ExcludedPaths = new()
		{
			"/health",
			"/metrics",
			"/swagger",
			"/openapi",
			"/signalr" // Don't audit WebSocket connections
		};

		public AuditLoggingMiddleware(RequestDelegate next, ILogger<AuditLoggingMiddleware> logger, IServiceProvider serviceProvider)
		{
			_next = next;
			_logger = logger;
			_serviceProvider = serviceProvider;
		}

		public async Task InvokeAsync(HttpContext context)
		{
			// Check if this path should be excluded from audit logging
			if (ShouldExcludePath(context.Request.Path))
			{
				await _next(context);
				return;
			}

			try
			{
				// Make response body readable by rewinding stream
				var originalBodyStream = context.Response.Body;
				using var memoryStream = new MemoryStream();
				context.Response.Body = memoryStream;

				// Extract user information from JWT claims
				var userId = GetUserIdFromClaims(context.User);

				// Call the next middleware
				await _next(context);

				// Capture response body
				memoryStream.Position = 0;
				string responseBody = await new StreamReader(memoryStream).ReadToEndAsync();

				// Reset response stream position and copy to original response
				memoryStream.Position = 0;
				await memoryStream.CopyToAsync(originalBodyStream);
				context.Response.Body = originalBodyStream;

				// Determine action type from HTTP method
				string action = DetermineAction(context.Request.Method);
				string resourceType = ExtractResourceType(context.Request.Path);

				// Create audit log entry with captured information
				var auditLog = new AuditLog
				{
					UserId = userId,
					Action = action,
					EntityType = resourceType,
					EntityId = ExtractResourceId(context.Request.Path),
					OldValues = null,
					NewValues = responseBody.Length > 500 ? responseBody.Substring(0, 500) : responseBody,
					CreatedAt = DateTime.UtcNow
				};

				// Save audit log to database using the repository
				using (var scope = _serviceProvider.CreateScope())
				{
					var auditLogRepository = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();
					try
					{
						await auditLogRepository.CreateAsync(auditLog);
					}
					catch (Exception ex)
					{
						_logger.LogError(ex, "Failed to create audit log: {Exception}", ex.Message);
					}
				}

				_logger.LogInformation(
					"[{Method}] {Path} | StatusCode: {StatusCode}",
					context.Request.Method, context.Request.Path, context.Response.StatusCode);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Exception in AuditLoggingMiddleware: {Exception}", ex.Message);
				await _next(context);
			}
		}

		/// <summary>
		/// Check if this path should be excluded from audit logging
		/// </summary>
		private static bool ShouldExcludePath(PathString path)
		{
			var pathValue = path.Value ?? string.Empty;
			return ExcludedPaths.Any(excluded => pathValue.StartsWith(excluded, StringComparison.OrdinalIgnoreCase));
		}

		/// <summary>
		/// Extract user ID from JWT claims
		/// </summary>
		private static Guid? GetUserIdFromClaims(ClaimsPrincipal user)
		{
			var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier) ?? user.FindFirst("sub");
			if (userIdClaim != null && Guid.TryParse(userIdClaim.Value, out var userId))
				return userId;

			return null;
		}

		/// <summary>
		/// Determine the audit action type based on HTTP method
		/// </summary>
		private static string DetermineAction(string method)
		{
			return method switch
			{
				"GET" => "READ",
				"POST" => "CREATE",
				"PUT" => "UPDATE",
				"PATCH" => "UPDATE",
				"DELETE" => "DELETE",
				_ => "UNKNOWN"
			};
		}

		/// <summary>
		/// Extract the resource type from the API path
		/// </summary>
		private static string ExtractResourceType(PathString path)
		{
			var pathValue = path.Value?.ToLower() ?? string.Empty;

			if (pathValue.Contains("/api/users")) return "User";
			if (pathValue.Contains("/api/devices")) return "Device";
			if (pathValue.Contains("/api/tags")) return "Tag";
			if (pathValue.Contains("/api/master-tables")) return "MasterTable";
			if (pathValue.Contains("/api/storage-flows")) return "StorageFlow";
			if (pathValue.Contains("/api/files")) return "File";
			if (pathValue.Contains("/api/auth")) return "Auth";

			return "Unknown";
		}

		/// <summary>
		/// Extract the resource ID from the API path
		/// </summary>
		private static Guid ExtractResourceId(PathString path)
		{
			var pathValue = path.Value ?? string.Empty;
			var segments = pathValue.Split('/');

			// Try to parse the last segment as a GUID
			if (segments.Length > 0)
			{
				var lastSegment = segments[^1];
				if (Guid.TryParse(lastSegment, out var guid))
					return guid;
			}

			return Guid.Empty;
		}
	}
}
