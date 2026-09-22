using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PokemonTCG.API.Data;
using PokemonTCG.API.Services;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddMemoryCache();

// CORS. In production the React app and the API are served from the same
// Cloudflare origin (the Worker serves the static build and proxies /api/* to
// this container), so no cross-origin access is needed. Cors:AllowedOrigins
// (comma-separated) opts specific origins in. When it is unset, Development
// allows any origin (the CRA dev server runs on another port) and every other
// environment allows none.
var allowedOrigins = (builder.Configuration["Cors:AllowedOrigins"] ?? string.Empty)
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
var allowAnyOrigin = allowedOrigins.Length == 0 && builder.Environment.IsDevelopment();
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        if (allowAnyOrigin)
        {
            policy.AllowAnyOrigin();
        }
        else
        {
            policy.WithOrigins(allowedOrigins);
        }
        policy.AllowAnyMethod().AllowAnyHeader();
    });
});

// PostgreSQL via Npgsql. Locally this is the container from docker-compose.yml;
// in production it's a managed Postgres (e.g. Neon) whose connection string is
// supplied as the ConnectionStrings__DefaultConnection secret.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "ConnectionStrings:DefaultConnection is not configured. Set it via the " +
        "ConnectionStrings__DefaultConnection environment variable.");
}
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

// The API runs behind Cloudflare, which terminates TLS and forwards plain HTTP
// to the container. Trust the X-Forwarded-* headers so the app sees the
// original scheme and client IP.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// Add services
builder.Services.AddHttpClient<PokemonTcgService>(client =>
{
    client.BaseAddress = new Uri("https://api.pokemontcg.io/v2/");
    client.DefaultRequestHeaders.Add("Accept", "application/json");
});

builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<ITokenService, TokenService>();

// Configure JWT authentication. The signing key is never hardcoded here: it
// is read from configuration, which in a real deployment means an
// environment variable (JwtSettings__Key) or a secret store such as
// `dotnet user-secrets` / Azure Key Vault / AWS Secrets Manager - never
// appsettings.json. In Development only, a fixed non-production fallback
// key is used so the project runs immediately after `dotnet run` with no
// setup step.
var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var jwtKey = jwtSettings["Key"];
if (string.IsNullOrWhiteSpace(jwtKey))
{
    if (builder.Environment.IsDevelopment())
    {
        jwtKey = "dev-only-insecure-signing-key-do-not-use-in-production-1234567890";
        builder.Configuration["JwtSettings:Key"] = jwtKey;
    }
    else
    {
        throw new InvalidOperationException(
            "JwtSettings:Key is not configured. Set it via the JwtSettings__Key " +
            "environment variable or a secret store before running outside Development.");
    }
}

builder.Services.AddAuthentication(opt =>
{
    opt.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    opt.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings["Issuer"],
        ValidAudience = jwtSettings["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
    };
});

var app = builder.Build();

// Seed data
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<AppDbContext>();
        DbInitializer.Initialize(context);
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while seeding the database.");
    }
}

// Configure the HTTP request pipeline
app.UseForwardedHeaders();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseStaticFiles();

app.UseCors("Frontend");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();