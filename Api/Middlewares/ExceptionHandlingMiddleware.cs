using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Core.DTOs;
using Core.Exceptions;
using Core.Security;
using Microsoft.Extensions.Options;

namespace Api.Middlewares
{
	/// <summary>
	/// Penangkap exception terakhir. Dua tugasnya sama pentingnya: mengembalikan envelope
	/// yang bentuknya sama dengan respons lain, dan <b>tidak membocorkan apa pun tentang
	/// dalamnya sistem</b>.
	///
	/// Versi sebelumnya menyertakan <c>type</c> dan <c>message</c> exception pada setiap
	/// respons 500, dan hanya stack trace yang dibatasi ke Development. Pesan exception
	/// adalah kebocoran informasi yang nyata: <c>MySqlException</c> memuat potongan SQL dan
	/// nama kolom, <c>IOException</c> memuat path absolut di server, dan exception koneksi
	/// memuat host beserta port internal.
	///
	/// Sekarang klien menerima pesan umum plus <c>traceId</c>. Detail lengkapnya ada di log
	/// server dengan traceId yang sama, sehingga penelusuran tetap mungkin tanpa
	/// mengirimkan isi perut sistem ke pemanggil.
	/// </summary>
	public class ExceptionHandlingMiddleware
	{
		private readonly RequestDelegate _next;
		private readonly ILogger<ExceptionHandlingMiddleware> _logger;
		private readonly SecuritySettings _settings;

		/// <summary>
		/// Null TIDAK dihilangkan. Kelima field envelope harus selalu hadir supaya bentuk
		/// respons dari middleware identik dengan respons dari controller — MVC memang
		/// menyerialkan null secara baku, dan perbedaan sekecil ini membuat klien harus
		/// menangani dua bentuk envelope tergantung siapa yang menjawab.
		/// </summary>
		private static readonly JsonSerializerOptions SerializerOptions = new()
		{
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase
		};

		public ExceptionHandlingMiddleware(
			RequestDelegate next,
			ILogger<ExceptionHandlingMiddleware> logger,
			IOptions<SecuritySettings> settings)
		{
			_next = next;
			_logger = logger;
			_settings = settings.Value;
		}

		public async Task InvokeAsync(HttpContext context)
		{
			try
			{
				await _next(context);
			}
			catch (Exception ex)
			{
				// TraceId ASP.NET sudah unik per request dan sudah muncul di log terstruktur;
				// memakainya berarti klien dan log bisa dicocokkan tanpa id buatan sendiri.
				var traceId = context.TraceIdentifier;

				_logger.LogError(ex,
					"Exception tidak tertangani pada {Method} {Path} (traceId {TraceId})",
					context.Request.Method, context.Request.Path, traceId);

				await WriteAsync(context, ex, traceId);
			}
		}

		private async Task WriteAsync(HttpContext context, Exception exception, string traceId)
		{
			// Respons yang sudah mulai terkirim tidak bisa ditimpa; memaksanya hanya
			// menghasilkan exception kedua yang menutupi yang pertama di log.
			if (context.Response.HasStarted)
			{
				_logger.LogWarning("Respons sudah terkirim; envelope error tidak bisa ditulis (traceId {TraceId})", traceId);
				return;
			}

			var (statusCode, message, isExpected) = exception switch
			{
				// Exception domain memang dirancang untuk dibaca pengguna.
				NotFoundException => ((int)HttpStatusCode.NotFound, exception.Message, true),
				BadRequestException => ((int)HttpStatusCode.BadRequest, exception.Message, true),
				ArgumentException => ((int)HttpStatusCode.BadRequest, exception.Message, true),
				UnauthorizedAccessException => ((int)HttpStatusCode.Forbidden, "Anda tidak berwenang melakukan tindakan ini.", true),
				OperationCanceledException => (499, "Permintaan dibatalkan.", true),
				_ => ((int)HttpStatusCode.InternalServerError, "Terjadi kesalahan saat memproses permintaan.", false)
			};

			var errors = new List<string> { message };

			if (!isExpected)
			{
				// TraceId ikut dikirim HANYA untuk kesalahan tak terduga: itulah yang perlu
				// disebutkan operator saat melapor, dan tidak ada gunanya pada 404 biasa.
				errors.Add($"traceId: {traceId}");

				if (_settings.ExposeExceptionDetails)
				{
					errors.Add($"{exception.GetType().Name}: {exception.Message}");
				}
			}

			context.Response.Clear();
			context.Response.StatusCode = statusCode;
			context.Response.ContentType = "application/json";

			var payload = ApiResponse<object>.Fail(statusCode, message, errors);
			await context.Response.WriteAsync(JsonSerializer.Serialize(payload, SerializerOptions));
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
