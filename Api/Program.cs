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
using Core.DTOs.Tag;
using System.Text.Json.Serialization;
using Api.Hubs;

var builder = WebApplication.CreateBuilder(args);

// Config Database and Dependency Injection
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured");
builder.Services.AddDbContext<AppDbContext>(options =>
	options.UseMySQL(connectionString));

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

// Register HttpClient for HTTP device polling
builder.Services.AddHttpClient();

// Register CORS for Frontend Communication
builder.Services.AddCors(options =>
{
	options.AddPolicy("AllowFrontend", policy =>
	{
		policy
			.WithOrigins(
				"http://localhost:5173",
				"http://localhost:3000",
				"http://localhost:4173",
				"http://127.0.0.1:5173",
				"http://127.0.0.1:3000"
			)
			.AllowAnyMethod()
			.AllowAnyHeader()
			.AllowCredentials() // Critical for JWT cookies
			.WithExposedHeaders("X-Total-Count", "X-Page-Number", "X-Total-Pages");
	});
});

// Register SignalR for real-time data streaming
builder.Services.AddSignalR(options =>
{
	options.EnableDetailedErrors = true;
	options.MaximumReceiveMessageSize = 64 * 1024; // 64KB max message
});

// Register Background Worker Service for device polling (event-driven)
// Must be registered as Singleton so it can be injected into scoped services
builder.Services.AddSingleton<IDeviceWorkerService, DeviceWorkerService<DeviceDataHub>>();
builder.Services.AddHostedService(provider => (DeviceWorkerService<DeviceDataHub>)provider.GetRequiredService<IDeviceWorkerService>());

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

	// Default rejection status code
	options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
	
	// Custom response when rate limit exceeded
	options.OnRejected = async (context, token) =>
	{
		context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
		context.HttpContext.Response.ContentType = "application/json";

		var response = new
		{
			status = 429,
			success = false,
			message = "Too many requests. Please try again later.",
			data = (object?)null,
			error = new
			{
				retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter) 
					? retryAfter.TotalSeconds 
					: (double?)null
			}
		};

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
			ClockSkew = TimeSpan.Zero
		};

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
				var response = new
				{
					status = 401,
					success = false,
					message = string.IsNullOrEmpty(context.ErrorDescription) 
						? "Unauthorized. Please login first." 
						: context.ErrorDescription,
					data = (object?)null,
					error = (object?)null
				};

				return context.Response.WriteAsJsonAsync(response);
			}
		};
	});

// Add services to the container.

builder.Services.AddControllers()
	.AddJsonOptions(options =>
	{
		// Serialize enums as strings instead of integers
		options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
	});
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
// Exception handling must be first to catch all exceptions
app.UseExceptionHandling();

if (app.Environment.IsDevelopment())
{
	app.MapOpenApi();
}

// CORS must be before authentication
app.UseCors("AllowFrontend");

// Enable serving static files (for uploaded files)
app.UseStaticFiles();

// Audit logging middleware to track all user actions
app.UseMiddleware<AuditLoggingMiddleware>();

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

app.MapControllers();

// Map SignalR Hub for real-time device data
app.MapHub<DeviceDataHub>("/signalr/device-hub");

await app.RunAsync();
