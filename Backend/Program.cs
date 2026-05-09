using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// ─── CONTROLLERS ──────────────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// ─── SWAGGER ──────────────────────────────────────────────────────────────────
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "AI Financial Health Analyzer API",
        Version = "v1"
    });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter your JWT token."
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// ─── DATABASE ─────────────────────────────────────────────────────────────────
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException(
        "Connection string 'DefaultConnection' is missing or empty. " +
        "Set it in appsettings.Development.json (which is gitignored).");

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));
});

// ─── JWT ──────────────────────────────────────────────────────────────────────
// FIX: Use IsNullOrWhiteSpace — empty string "" passes the old ?? null check
// and causes IDX10720 (key too short) at runtime on first login/register.
var jwtKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrWhiteSpace(jwtKey))
    throw new InvalidOperationException(
        "Jwt:Key is missing or empty. " +
        "Add a key of at least 32 characters to appsettings.Development.json. " +
        "Example: \"Key\": \"my-super-secret-key-at-least-32-chars!!\"");

if (jwtKey.Length < 32)
    throw new InvalidOperationException(
        $"Jwt:Key is too short ({jwtKey.Length} chars). " +
        "HS256 requires at least 32 characters (256 bits).");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidateAudience         = true,
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer              = builder.Configuration["Jwt:Issuer"],
            ValidAudience            = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });

// ─── CORS ─────────────────────────────────────────────────────────────────────
var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? new[] { "http://localhost:4200" };

builder.Services.AddCors(options =>
{
    options.AddPolicy("DefaultCors", policy =>
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials());
});

// ─── DEPENDENCY INJECTION ─────────────────────────────────────────────────────
// Repositories
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<ITransactionRepository, TransactionRepository>();
builder.Services.AddScoped<IRiskPredictionRepository, RiskPredictionRepository>();
builder.Services.AddScoped<IInsightRepository, InsightRepository>();

// Services
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<ITransactionService, TransactionService>();
builder.Services.AddScoped<CsvService>();
builder.Services.AddScoped<XlsxService>();
builder.Services.AddScoped<InsightsService>();

// ML service — Singleton so the model trains only once at startup
builder.Services.AddSingleton<RiskPredictionService>();

// ObjectPool support for PredictionEngine thread safety
builder.Services.AddSingleton<Microsoft.Extensions.ObjectPool.ObjectPoolProvider,
    Microsoft.Extensions.ObjectPool.DefaultObjectPoolProvider>();

// ─── BUILD ────────────────────────────────────────────────────────────────────
var app = builder.Build();

// Global exception handling must be registered first
app.UseMiddleware<GlobalExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("DefaultCors");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();