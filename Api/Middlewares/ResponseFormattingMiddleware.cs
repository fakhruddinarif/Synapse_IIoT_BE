using Core.DTOs;
using System.Text.Json;

namespace Api.Middlewares
{
	public class ResponseFormattingMiddleware
	{
		private readonly RequestDelegate _next;

		public ResponseFormattingMiddleware(RequestDelegate next)
		{
			_next = next;
		}

		public async Task InvokeAsync(HttpContext context)
		{
			var originalBodyStream = context.Response.Body;

			using var responseBody = new MemoryStream();
			context.Response.Body = responseBody;

			await _next(context);

			// Check if response has no content and status indicates error
			if (context.Response.StatusCode >= 400 && context.Response.ContentLength == null && responseBody.Length == 0)
			{
				context.Response.ContentType = "application/json";

				var message = context.Response.StatusCode switch
				{
					401 => "Unauthorized. Please login first.",
					403 => "Forbidden. You don't have permission to access this resource.",
					404 => "Resource not found.",
					405 => "Method not allowed.",
					_ => "An error occurred while processing your request."
				};

				var response = ApiResponse<object>.Fail(
					context.Response.StatusCode,
					message
				);

				var options = new JsonSerializerOptions
				{
					PropertyNamingPolicy = JsonNamingPolicy.CamelCase
				};

				var json = JsonSerializer.Serialize(response, options);
				await responseBody.WriteAsync(System.Text.Encoding.UTF8.GetBytes(json));
			}

			responseBody.Seek(0, SeekOrigin.Begin);
			await responseBody.CopyToAsync(originalBodyStream);
		}
	}

	public static class ResponseFormattingMiddlewareExtensions
	{
		public static IApplicationBuilder UseResponseFormatting(this IApplicationBuilder builder)
		{
			return builder.UseMiddleware<ResponseFormattingMiddleware>();
		}
	}
}
