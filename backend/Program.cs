using Microsoft.AspNetCore.Authentication.JwtBearer;
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
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("database");

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", builder =>
    {
        builder.AllowAnyOrigin()
               .AllowAnyMethod()
               .AllowAnyHeader();
    });
});

// Add DbContext - PostgreSQL via Npgsql. Locally this is the postgres service
// in docker-compose.yml; in production it is a managed Postgres (e.g. Neon)
// whose connection string arrives as ConnectionStrings__DefaultConnection.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "ConnectionStrings:DefaultConnection is not configured. Set it via the " +
        "ConnectionStrings__DefaultConnection environment variable.");
}
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure()));

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

// Configure the HTTP request pipeline.
// Swagger stays on in every environment - this is a public portfolio API, so
// the interactive docs are part of what's being shown. They live under /api
// so the Cloudflare Worker's /api/* route reaches them in production.
app.UseSwagger(c => c.RouteTemplate = "api/docs/{documentName}/swagger.json");
app.UseSwaggerUI(c =>
{
    c.RoutePrefix = "api/docs";
    c.SwaggerEndpoint("/api/docs/v1/swagger.json", "Pokémon TCG Marketplace API v1");
});

// In production TLS terminates at Cloudflare's edge and the Worker is the only
// ingress to this container, so an HTTPS redirect here would have no port to
// redirect to.
if (app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseCors("AllowAll");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/healthz");

app.Run();