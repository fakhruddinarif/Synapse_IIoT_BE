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
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// Config Database and Dependency Injection
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured");
builder.Services.AddDbContext<AppDbContext>(options =>
	options.UseMySQL(connectionString));

// Register Repositories
builder.Services.AddScoped<IUserRepository, UserRepository>();

// Register Services
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();

// Config Rate Limiting
builder.Services.AddRateLimiter(options =>
{
	options.AddFixedWindowLimiter("Fixed", limiterOptions =>
	{
		limiterOptions.PermitLimit = 10; // Max 100 requests
		limiterOptions.Window = TimeSpan.FromMinutes(1); // Per 1 minute
		limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
		limiterOptions.QueueLimit = 0; // No queuing, reject immediately when limit is reached
	});

	options.RejectionStatusCode = StatusCodes.Status429TooManyRequests; // Too Many Requests
});

// Config CSRF Protection
builder.Services.AddAntiforgery(options => 
{
    options.HeaderName = "X-CSRF-TOKEN"; // Header yang wajib dikirim frontend untuk validasi CSRF
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

// Setup CORS policy
builder.Services.AddCors(options =>
{
	options.AddPolicy("App", policy =>
	{
		policy.WithOrigins("http://localhost:5173")
			  .AllowAnyHeader()
			  .AllowAnyMethod()
			  .AllowCredentials(); // Wajib TRUE agar Cookie bisa lewat
	});
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
	app.MapOpenApi();
}

app.UseCors("App");

app.UseHttpsRedirection();

app.UseRateLimiter();

// Add CSRF Validation Middleware (must be before Authentication)
app.UseCsrfValidation();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

await app.RunAsync();
