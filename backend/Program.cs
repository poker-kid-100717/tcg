using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
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
// /health is readiness (includes the database); /health/live only says the
// process is up, which is what the container runtime pings on start.
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("database");

// In production the API runs in a Cloudflare Container behind a Worker, which
// terminates TLS and forwards plain HTTP with X-Forwarded-Proto/For set. The
// container is only reachable through that Worker, so trust those headers.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// CORS: in production the frontend and API share one origin through the
// Worker, so no cross-origin access is needed. Development allows any
// origin so the CRA dev server (port 3000) can reach the API (port 5259).
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (builder.Environment.IsDevelopment())
            policy.AllowAnyOrigin();
        else
            policy.WithOrigins(allowedOrigins);

        policy.AllowAnyMethod().AllowAnyHeader();
    });
});

// PostgreSQL via EF Core. Accepts either an Npgsql key/value string or a
// postgres:// URL (the format Neon and most hosts hand out). Development
// defaults to the local instance from docker-compose.yml; anywhere else a
// connection string is required, so a misconfigured deploy fails fast.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "ConnectionStrings:DefaultConnection is not configured. Set it via the " +
        "ConnectionStrings__DefaultConnection environment variable.");
}
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(PostgresConnectionString.Normalize(connectionString)));

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
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// No UseHttpsRedirection: Cloudflare only serves the site over HTTPS and
// reaches the container over its private network, so there is no plain-HTTP
// public entry point to redirect.
app.UseForwardedHeaders();

app.UseCors();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });

app.Run();