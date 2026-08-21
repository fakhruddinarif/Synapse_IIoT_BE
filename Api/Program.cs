using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Threading.RateLimiting;
using Infrastructure.Data;
using Microsoft.AspNetCore.RateLimiting;
using Core.Interface;
using Infrastructure.Repositories;
using Infrastructure.Services;
using Api.Middlewares;
using Core.DTOs;
using Core.DTOs.Tag;
using System.Text.Json;
using System.Text.Json.Serialization;
using Api.Hubs;
using Core.Security;
using Microsoft.AspNetCore.Authorization;
using Infrastructure.Acquisition;
using Infrastructure.Drivers;

var builder = WebApplication.CreateBuilder(args);

/* ===========================================================================
   VALIDASI KONFIGURASI KEAMANAN — dijalankan sebelum apa pun lainnya.

   Konfigurasi keamanan yang salah harus membuat aplikasi MENOLAK MENYALA, bukan
   menyala dengan diam-diam lemah. Gateway yang berjalan dengan JWT secret bawaan
   terlihat sehat di semua dasbor, dan baru terlihat salah setelah dimanfaatkan.
=========================================================================== */
builder.Services.Configure<SecuritySettings>(
	builder.Configuration.GetSection(SecuritySettings.SectionName));

var securitySettings = builder.Configuration
	.GetSection(SecuritySettings.SectionName)
	.Get<SecuritySettings>() ?? new SecuritySettings();

const string placeholderSecret = "YourSuperSecretKeyThatIsAtLeast32CharactersLong!";
var jwtSecret = builder.Configuration["JwtSettings:Secret"];
var isProduction = builder.Environment.IsProduction();

if (string.IsNullOrWhiteSpace(jwtSecret))
{
	throw new InvalidOperationException(
		"JwtSettings:Secret belum diisi. Setel lewat variabel lingkungan " +
		"JwtSettings__Secret atau 'dotnet user-secrets set \"JwtSettings:Secret\" <nilai>'. " +
		"Gunakan minimal 32 karakter acak.");
}

// HS256 memakai kunci simetris: kunci lebih pendek dari 256 bit (32 byte) menurunkan
// kekuatan tanda tangan ke panjang kunci itu, bukan ke kekuatan algoritmanya.
if (System.Text.Encoding.UTF8.GetByteCount(jwtSecret) < 32)
{
	throw new InvalidOperationException(
		"JwtSettings:Secret terlalu pendek. HS256 membutuhkan minimal 32 byte (256 bit).");
}

if (jwtSecret == placeholderSecret)
{
	if (isProduction)
	{
		throw new InvalidOperationException(
			"JwtSettings:Secret masih memakai nilai contoh dari repositori. Nilai ini " +
			"publik, jadi siapa pun bisa menandatangani token yang sah. Ganti sebelum " +
			"menjalankan di produksi.");
	}

	// Di pengembangan tidak diblokir, tapi harus berisik — supaya tidak diam-diam
	// terbawa ke lingkungan lain.
	Console.ForegroundColor = ConsoleColor.Yellow;
	Console.WriteLine("[PERINGATAN KEAMANAN] JwtSettings:Secret masih nilai contoh repositori. " +
					  "Aplikasi menolak menyala dengan nilai ini di Production.");
	Console.ResetColor();
}

if (isProduction)
{
	if (!securitySettings.CookieSecure)
	{
		throw new InvalidOperationException(
			"Security:CookieSecure wajib true di produksi. Cookie sesi tanpa atribut Secure " +
			"ikut terkirim lewat HTTP polos dan bisa dibaca siapa pun di jaringan yang sama.");
	}

	if (securitySettings.ExposeExceptionDetails)
	{
		throw new InvalidOperationException(
			"Security:ExposeExceptionDetails wajib false di produksi. Pesan exception memuat " +
			"potongan SQL, path server, dan host internal.");
	}

	if (securitySettings.AllowedOrigins.Count == 0)
	{
		throw new InvalidOperationException(
			"Security:AllowedOrigins kosong. Autentikasi berbasis cookie mengharuskan daftar " +
			"origin eksplisit — wildcard tidak bisa dipadukan dengan AllowCredentials.");
	}
}

// Fallback origin untuk pengembangan lokal saja; di produksi daftar ini wajib dari konfigurasi.
if (securitySettings.AllowedOrigins.Count == 0)
{
	securitySettings.AllowedOrigins.AddRange(new[]
	{
		"http://localhost:5173", "http://127.0.0.1:5173",
		"http://localhost:4173", "http://localhost:3000"
	});
}

// Config Database and Dependency Injection
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

// String kosong bukan null, jadi pemeriksaan null saja akan meloloskannya dan kegagalan
// baru muncul saat koneksi pertama — jauh dari penyebabnya.
if (string.IsNullOrWhiteSpace(connectionString))
{
	throw new InvalidOperationException(
		"ConnectionStrings:DefaultConnection belum diisi. Setel lewat variabel lingkungan " +
		"ConnectionStrings__DefaultConnection atau user-secrets. Kredensial database tidak " +
		"disimpan di appsettings.json yang ikut masuk repositori.");
}
builder.Services.AddDbContext<AppDbContext>(options =>
	options.UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure(3)));

// Register Repositories
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IDeviceRepository, DeviceRepository>();
builder.Services.AddScoped<IMasterTableRepository, MasterTableRepository>();
builder.Services.AddScoped<IMasterTableFieldsRepository, MasterTableFieldsRepository>();
builder.Services.AddScoped<IStorageFlowRepository, StorageFlowRepository>();
builder.Services.AddScoped<IAuditLogRepository, AuditLogRepository>();
builder.Services.AddScoped<ITagRepository, TagRepository>();

// Register Services
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IDeviceService, DeviceService>();
builder.Services.AddScoped<IMasterTableService, MasterTableService>();
builder.Services.AddScoped<IStorageFlowService, StorageFlowService>();
builder.Services.AddScoped<IFileService, FileService>();
builder.Services.AddScoped<ITagService, TagService>();
builder.Services.AddScoped<IDiscoveryService, DiscoveryService>();
builder.Services.AddScoped<ITagHistoryService, TagHistoryService>();

// Singleton: penghitung percobaan login harus dibagi seluruh request, bukan per-scope.
builder.Services.AddSingleton<ILoginThrottle, LoginThrottle>();

// Register HttpClient for HTTP device polling
builder.Services.AddHttpClient();

// Klien khusus discovery: redirect TIDAK diikuti supaya probe melaporkan 301/302 apa adanya
// (endpoint perangkat yang mengalihkan biasanya berarti URL-nya salah, bukan harus diikuti).
builder.Services.AddHttpClient("discovery")
	.ConfigureHttpClient(client => client.Timeout = Timeout.InfiniteTimeSpan)
	.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
	{
		AllowAutoRedirect = false
	});

// Register CORS for Frontend Communication
builder.Services.AddCors(options =>
{
	options.AddPolicy("AllowFrontend", policy =>
	{
		// Origin dari konfigurasi, bukan literal di kode: daftar yang di-hardcode berarti
		// setiap penambahan lingkungan menuntut kompilasi ulang, dan biasanya berakhir
		// dengan seseorang menambahkan wildcard "sementara".
		policy
			.WithOrigins(securitySettings.AllowedOrigins.ToArray())
			.WithMethods("GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS")
			// Header dibatasi ke yang benar-benar dipakai klien. AllowAnyHeader membuat
			// preflight meloloskan header apa pun, termasuk yang dipakai serangan
			// eksperimental di masa depan.
			.WithHeaders("Content-Type", "Accept", "Authorization", "X-Requested-With")
			.AllowCredentials() // wajib untuk cookie sesi
			.WithExposedHeaders("Retry-After");
	});
});

// Register SignalR for real-time data streaming
builder.Services.AddSignalR(options =>
{
	// Pesan error rinci membocorkan struktur internal ke setiap klien yang terhubung.
	options.EnableDetailedErrors = builder.Environment.IsDevelopment();
	options.MaximumReceiveMessageSize = 64 * 1024; // 64KB max message
});

// Register Background Worker Service for storage flows (event-driven)
// Must be registered as Singleton so it can be injected into scoped services
builder.Services.AddSingleton<IDeviceWorkerService, DeviceWorkerService<DeviceDataHub>>();
builder.Services.AddHostedService(provider => (DeviceWorkerService<DeviceDataHub>)provider.GetRequiredService<IDeviceWorkerService>());

/* ======================= Jalur akuisisi (Fase 0) =======================
 *
 * Semuanya singleton dan sengaja: tag engine memegang nilai sekarang seluruh pabrik, buffer
 * memegang satu berkas WAL, dan penjadwal memegang koneksi perangkat. Semuanya per-scope
 * berarti setiap request HTTP membuat salinannya sendiri — RTDB kosong, WAL kedua yang
 * merusak yang pertama, dan koneksi PLC yang berlipat.
 */
var acquisitionOptions = new AcquisitionOptions();
builder.Configuration.GetSection("Acquisition").Bind(acquisitionOptions);
builder.Services.AddSingleton(acquisitionOptions);

var historianOptions = new HistorianOptions();
builder.Configuration.GetSection("Historian").Bind(historianOptions);
builder.Services.AddSingleton(historianOptions);

// Letak WAL dan catatan jeda. Baku di bawah folder aplikasi supaya jalan tanpa konfigurasi,
// tetapi di produksi sebaiknya menunjuk ke volume terpisah: penuhnya disk sistem tidak boleh
// ikut menghentikan akuisisi.
var acquisitionDataPath = builder.Configuration["Acquisition:DataPath"]
	?? Path.Combine(builder.Environment.ContentRootPath, "acquisition-data");

builder.Services.AddSingleton<ITagEngine, TagEngine>();

builder.Services.AddSingleton<ISampleBuffer>(provider => new FileSampleBuffer(
	acquisitionDataPath,
	provider.GetRequiredService<ILoggerFactory>().CreateLogger<FileSampleBuffer>()));

builder.Services.AddSingleton<IGapLedger>(provider => new FileGapLedger(
	Path.Combine(acquisitionDataPath, "acquisition-gaps.jsonl"),
	provider.GetRequiredService<ILoggerFactory>().CreateLogger<FileGapLedger>()));

builder.Services.AddSingleton<IDeviceDriverFactory, DeviceDriverFactory>();
builder.Services.AddSingleton<IAcquisitionPlanSource, DbAcquisitionPlanSource>();
builder.Services.AddSingleton<IRealtimePublisher, SignalRRealtimePublisher<DeviceDataHub>>();
builder.Services.AddSingleton<ISampleWriter, TagHistoryWriter>();

builder.Services.AddSingleton(provider => new RealtimeCoalescer(
	provider.GetRequiredService<IRealtimePublisher>(),
	acquisitionOptions.RealtimeWindowMs,
	provider.GetRequiredService<ILoggerFactory>().CreateLogger<RealtimeCoalescer>()));

// Satu instans dipakai dua peran: hosted service yang menjalankan penjadwal, dan
// IAcquisitionControl yang dipanggil lapisan service saat konfigurasi berubah. Dua registrasi
// terpisah akan menghasilkan dua penjadwal, dan yang dipanggil service bukan yang berjalan.
builder.Services.AddSingleton<AcquisitionWorker>();
builder.Services.AddSingleton<IAcquisitionControl>(provider => provider.GetRequiredService<AcquisitionWorker>());
builder.Services.AddHostedService(provider => provider.GetRequiredService<AcquisitionWorker>());

builder.Services.AddHostedService<HistorianDrainService>();

// Config Rate Limiting (Optimized for Real-time Dashboard)
builder.Services.AddRateLimiter(options =>
{
	// General API limiter - allows real-time dashboard traffic
	options.AddFixedWindowLimiter("Default", limiterOptions =>
	{
		limiterOptions.PermitLimit = 100;      // 100 requests
		limiterOptions.Window = TimeSpan.FromMinutes(1); // per 1 minute
		limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
		limiterOptions.QueueLimit = 5;         // Queue up to 5 requests
	});

	// Stricter limiter for login endpoint (Anti-brute-force)
	options.AddFixedWindowLimiter("Login", limiterOptions =>
	{
		limiterOptions.PermitLimit = 5;        // 5 attempts
		limiterOptions.Window = TimeSpan.FromMinutes(15); // per 15 minutes
		limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
		limiterOptions.QueueLimit = 0;
	});

	// Pembatas global per alamat IP. Tanpa ini, kebijakan bernama di atas hanya berlaku
	// pada endpoint yang secara eksplisit memakai [EnableRateLimiting] — dan sebelum
	// perubahan ini TIDAK ADA satu pun endpoint yang memakainya, sehingga seluruh
	// konfigurasi rate limit tidak pernah berpengaruh.
	options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
	{
		var key = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

		// SignalR memakai koneksi berumur panjang dengan banyak pesan; membatasinya seperti
		// REST akan memutus stream realtime yang sah.
		if (httpContext.Request.Path.StartsWithSegments("/signalr"))
		{
			return RateLimitPartition.GetNoLimiter("signalr");
		}

		return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
		{
			PermitLimit = 300,
			Window = TimeSpan.FromMinutes(1),
			QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
			QueueLimit = 10
		});
	});

	// Default rejection status code
	options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

	// Custom response when rate limit exceeded
	options.OnRejected = async (context, token) =>
	{
		context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
		context.HttpContext.Response.ContentType = "application/json";

		var retryAfterSeconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
			? (int)Math.Ceiling(retryAfter.TotalSeconds)
			: (int?)null;

		if (retryAfterSeconds is not null)
		{
			// Header standar; klien dan proksi tahu cara membacanya tanpa mengurai body.
			context.HttpContext.Response.Headers.RetryAfter = retryAfterSeconds.Value.ToString();
		}

		// Envelope yang sama dengan seluruh API: status, message, data, errors, paging.
		var response = Core.DTOs.ApiResponse<object>.Fail(
			429,
			"Terlalu banyak permintaan",
			retryAfterSeconds is not null
				? $"Coba lagi dalam {retryAfterSeconds} detik."
				: "Coba lagi beberapa saat lagi.");

		await context.HttpContext.Response.WriteAsJsonAsync(response, cancellationToken: token);
	};
});

// Config Authentication with JWT
var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var secretKey = jwtSettings["Secret"] ?? throw new InvalidOperationException("JWT Secret is not configured");
var issuer = jwtSettings["Issuer"] ?? "SynapseIIoT";
var audience = jwtSettings["Audience"] ?? "SynapseIIoT";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
	.AddJwtBearer(options =>
	{
		options.TokenValidationParameters = new TokenValidationParameters
		{
			ValidateIssuerSigningKey = true,
			IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
			ValidateIssuer = true,
			ValidIssuer = issuer,
			ValidateAudience = true,
			ValidAudience = audience,
			ValidateLifetime = true,
			ClockSkew = TimeSpan.Zero,

			// Algoritma dipaku. Tanpa daftar ini, validator menerima algoritma apa pun yang
			// cocok dengan jenis kunci — dan penyempitan eksplisit adalah pertahanan baku
			// terhadap serangan pergantian algoritma.
			ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },

			// Token tanpa tanda tangan atau tanpa masa berlaku ditolak secara eksplisit,
			// bukan bergantung pada default pustaka yang bisa berubah antar versi.
			RequireSignedTokens = true,
			RequireExpirationTime = true,

			// Nama klaim dipetakan eksplisit supaya [Authorize(Roles = ...)] tidak
			// bergantung pada pemetaan bawaan yang berbeda antar versi pustaka.
			NameClaimType = System.Security.Claims.ClaimTypes.NameIdentifier,
			RoleClaimType = System.Security.Claims.ClaimTypes.Role
		};

		// Token tidak perlu disimpan di AuthenticationProperties: tidak ada kode yang
		// membacanya dari sana, dan menyimpannya memperluas tempat token bisa terbaca.
		options.SaveToken = false;

		// Logic read Token from HTTP-Only Cookie
		options.Events = new JwtBearerEvents
		{
			OnMessageReceived = context =>
			{
				// Get token from cookie bernama "JWT-TOKEN"
				context.Token = context.Request.Cookies["JWT-TOKEN"];
				return Task.CompletedTask;
			},
			OnChallenge = context =>
			{
				// Skip default behavior
				context.HandleResponse();

				// Set status code and content type
				context.Response.StatusCode = 401;
				context.Response.ContentType = "application/json";

				// Create custom response format
				// Pesan dibuat SERAGAM dan tidak memuat ErrorDescription dari pustaka:
				// deskripsi itu memberi tahu penyerang alasan penolakan yang tepat
				// ("token expired" vs "signature invalid"), yang mempersempit tebakannya.
				var response = Core.DTOs.ApiResponse<object>.Fail(
					401, "Sesi tidak valid atau sudah berakhir. Silakan masuk kembali.");

				return context.Response.WriteAsJsonAsync(response);
			},
			OnForbidden = context =>
			{
				context.Response.StatusCode = 403;
				context.Response.ContentType = "application/json";

				var response = Core.DTOs.ApiResponse<object>.Fail(
					403, "Anda tidak berwenang mengakses sumber daya ini.");

				return context.Response.WriteAsJsonAsync(response);
			}
		};
	});

/* ===========================================================================
   OTORISASI TOLAK-SECARA-BAKU

   Tanpa fallback policy, endpoint yang lupa diberi [Authorize] terbuka untuk
   publik — dan endpoint yang lupa dijaga tidak menghasilkan error apa pun,
   sehingga kelalaiannya hanya terlihat kalau seseorang memeriksanya satu per satu.
   Dengan fallback ini, endpoint publik harus dinyatakan eksplisit lewat
   [AllowAnonymous].
=========================================================================== */
builder.Services.AddAuthorization(options =>
{
	options.FallbackPolicy = new AuthorizationPolicyBuilder()
		.RequireAuthenticatedUser()
		.Build();
});

// Add services to the container.

builder.Services.AddControllers()
	.AddJsonOptions(options =>
	{
		// Enum sebagai string, bukan angka: klien membandingkan "HTTP", bukan 4.
		options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());

		// Dinyatakan eksplisit meski kebetulan sama dengan default: kontrak envelope tidak
		// boleh bergantung pada default pustaka yang bisa berubah antar versi .NET.
		options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
		options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
	});
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
	var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
	dbContext.Database.Migrate();
}

// Configure the HTTP request pipeline.
// Exception handling must be first to catch all exceptions
app.UseExceptionHandling();

// Header keamanan dipasang sedini mungkin: begitu body respons mulai terkirim,
// header tidak bisa diubah lagi.
app.UseSecurityHeaders();

if (!app.Environment.IsDevelopment())
{
	// HSTS memberi tahu browser untuk tidak pernah lagi memakai HTTP ke host ini.
	app.UseHsts();
}

if (app.Environment.IsDevelopment())
{
	app.MapOpenApi();
}

// CORS must be before authentication
app.UseCors("AllowFrontend");

// Enable serving static files (for uploaded files)
app.UseStaticFiles();

// Validasi Origin untuk permintaan yang mengubah data — pertahanan CSRF untuk
// autentikasi berbasis cookie. Ditempatkan setelah CORS supaya preflight sudah
// tertangani, dan sebelum autentikasi supaya permintaan lintas situs ditolak
// sebelum menyentuh logika apa pun.
app.UseOriginValidation();

// Audit logging middleware to track all user actions
app.UseAuditLogging();

// Only use HTTPS redirection in production
if (!app.Environment.IsDevelopment())
{
	app.UseHttpsRedirection();
}

app.UseRateLimiter();

// Format responses for error status codes without body
app.UseResponseFormatting();

app.UseAuthentication();
app.UseAuthorization();

/* ===========================================================================
   KESEHATAN — dua endpoint, dan perbedaannya penting.

   /health/live  : prosesnya hidup. TIDAK menyentuh database.
   /health/ready : siap melayani permintaan yang butuh database.

   Kenapa dipisah: gateway ini dirancang TETAP mengakuisisi saat database mati —
   sampel tertahan di WAL dan menyusul masuk begitu database kembali. Kalau liveness
   ikut memeriksa database, orkestrator akan MEMBUNUH DAN MENYALAKAN ULANG gateway
   justru pada saat ia sedang menjalankan tugas terpentingnya, dan setiap restart
   memutus koneksi ke semua PLC. Itu mengubah gangguan database menjadi kehilangan
   data lapangan.
=========================================================================== */
app.MapGet("/health/live", (IAcquisitionControl acquisition) =>
{
	var status = acquisition.GetStatus();

	return Results.Ok(ApiResponse<object>.Success(new
	{
		alive = true,
		acquisitionRunning = status.IsRunning,
		devices = status.DeviceCount,
		tags = status.TagCount,
		bufferPendingBytes = status.BufferPendingBytes
	}, "Gateway hidup"));
})
	.AllowAnonymous()
	.DisableRateLimiting();

app.MapGet("/health/ready", async (AppDbContext db, CancellationToken ct) =>
{
	bool canConnect;
	try
	{
		canConnect = await db.Database.CanConnectAsync(ct);
	}
	catch (Exception)
	{
		// Sebab teknisnya sudah masuk log; endpoint kesehatan tidak boleh
		// menyiarkan detail koneksi ke siapa pun yang bisa memanggilnya.
		canConnect = false;
	}

	if (canConnect)
	{
		return Results.Ok(ApiResponse<object>.Success(new { database = "ok" }, "Siap melayani"));
	}

	return Results.Json(
		ApiResponse<object>.Fail(503, "Database tidak bisa dihubungi"),
		statusCode: 503);
})
	.AllowAnonymous()
	.DisableRateLimiting();

app.MapControllers();

// Map SignalR Hub for real-time device data
app.MapHub<DeviceDataHub>("/signalr/device-hub");

await app.RunAsync();
