using Core.DTOs;
using Core.Exceptions;
using System.Net;
using System.Text.Json;

namespace Api.Middlewares
{
	public class ExceptionHandlingMiddleware
	{
		private readonly RequestDelegate _next;
		private readonly ILogger<ExceptionHandlingMiddleware> _logger;

		public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
		{
			_next = next;
			_logger = logger;
		}

		public async Task InvokeAsync(HttpContext context)
		{
			try
			{
				await _next(context);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "An unhandled exception occurred: {Message}", ex.Message);
				await HandleExceptionAsync(context, ex);
			}
		}

		private static async Task HandleExceptionAsync(HttpContext context, Exception exception)
		{
			context.Response.ContentType = "application/json";

			var (statusCode, message) = exception switch
			{
				NotFoundException => ((int)HttpStatusCode.NotFound, exception.Message),
				BadRequestException => ((int)HttpStatusCode.BadRequest, exception.Message),
				_ => ((int)HttpStatusCode.InternalServerError, "An error occurred while processing your request.")
			};

			context.Response.StatusCode = statusCode;

			var response = ApiResponse<object>.Fail(
				statusCode,
				message,
				new
				{
					type = exception.GetType().Name,
					message = exception.Message,
					// Only include stack trace in development
					stackTrace = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development" 
						? exception.StackTrace 
						: null
				}
			);

			var options = new JsonSerializerOptions
			{
				PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
				WriteIndented = true
			};

			await context.Response.WriteAsync(JsonSerializer.Serialize(response, options));
		}
	}

	public static class ExceptionHandlingMiddlewareExtensions
	{
		public static IApplicationBuilder UseExceptionHandling(this IApplicationBuilder builder)
		{
			return builder.UseMiddleware<ExceptionHandlingMiddleware>();
		}
	}
}
