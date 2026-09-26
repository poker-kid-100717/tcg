using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Http.Resilience;
using PokemonTCG.API.Collections;
using PokemonTCG.API.Data;
using PokemonTCG.API.Pricing;
using PokemonTCG.API.Pricing.Predictions;
using PokemonTCG.API.Pricing.Tcgplayer;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddProblemDetails();
// Enums (such as a snapshot run's status) read as names, not numbers.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddExceptionHandler<CollectionExceptionHandler>();
builder.Services.AddExceptionHandler<UpstreamUnavailableHandler>();
builder.Services.AddSingleton(TimeProvider.System);

// /health is readiness (includes the database); /health/live only says the
// process is up, which is what the container runtime pings on start.
builder.Services.AddSingleton<DatabaseInitializationStatus>();
builder.Services.AddHostedService<DatabaseInitializer>();
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("database")
    .AddCheck<DatabaseInitializationHealthCheck>("database-migrations");

// In production the API runs in a Cloudflare Container behind a Worker, which
// terminates TLS and forwards plain HTTP with X-Forwarded-Proto/For set. The
// container is only reachable through that Worker, so trust those headers.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
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

// Card and price data: the Pokémon TCG API, with retries and timeouts sized
// for its full 250-card pages, and a shared cache in front of it.
var tcgOptions = builder.Configuration.GetSection(PokemonTcgOptions.SectionName).Get<PokemonTcgOptions>() ?? new();
builder.Services.AddHttpClient<PokemonTcgClient>(client =>
    {
        client.BaseAddress = new Uri(tcgOptions.BaseUrl.EndsWith('/') ? tcgOptions.BaseUrl : tcgOptions.BaseUrl + "/");
        client.DefaultRequestHeaders.Add("Accept", "application/json");
        if (!string.IsNullOrWhiteSpace(tcgOptions.ApiKey)) client.DefaultRequestHeaders.Add("X-Api-Key", tcgOptions.ApiKey);
    })
    .AddStandardResilienceHandler(options =>
    {
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(45);
        options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(3);
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(2);
    });
builder.Services.AddHybridCache();

// TCGplayer Developer API. Optional: with no keys configured the client is
// registered but unused, and prices keep coming from the Pokémon TCG API
// (which republishes TCGplayer's market prices daily).
builder.Services.AddTcgplayerClient(builder.Configuration)
    .AddStandardResilienceHandler(options =>
    {
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(30);
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(1);
    });

// Accounts. Guests get a real (password-less) account so a collection can start
// on the first click; the session is a long-lived cookie. The container sleeps
// and restarts, so the keys that sign that cookie live in Postgres.
builder.Services.AddDataProtection()
    .SetApplicationName("tcg")
    .PersistKeysToDbContext<AppDbContext>();
builder.Services.AddIdentityCore<AppUser>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 10;
        options.Password.RequireDigit = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Lockout.MaxFailedAccessAttempts = 10;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager();
builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddCookie(IdentityConstants.ApplicationScheme, options =>
    {
        options.Cookie.Name = "tcg_session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromDays(365);
        options.SlidingExpiration = true;
        // An API: answer 401/403 rather than redirecting to a login page.
        options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
        options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
    });
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(AccountEndpoints.AuthRateLimit, context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(5) }));
});

builder.Services.AddSingleton(builder.Configuration.GetSection(SnapshotOptions.SectionName).Get<SnapshotOptions>() ?? new());
builder.Services.AddScoped<PriceSnapshotService>();
builder.Services.AddScoped<CatalogReader>();
builder.Services.AddScoped<PriceGuideService>();
builder.Services.AddSingleton(builder.Configuration.GetSection(PredictionOptions.SectionName).Get<PredictionOptions>() ?? new());
builder.Services.AddScoped<PredictionService>();
builder.Services.AddSingleton(builder.Configuration.GetSection(CollectionOptions.SectionName).Get<CollectionOptions>() ?? new());
builder.Services.AddScoped<CollectionService>();

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// No UseHttpsRedirection: Cloudflare only serves the site over HTTPS and
// reaches the container over its private network, so there is no plain-HTTP
// public entry point to redirect. No CORS either: the site and the API share
// one origin through the Worker, and the Vite dev server proxies /api.
app.UseForwardedHeaders();
app.UseMiddleware<DatabaseReadinessMiddleware>();
app.UseMiddleware<SameSiteRequestMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapPriceGuide();
app.MapPredictions();
app.MapAccount();
app.MapCollection();
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });

app.Run();

/// <summary>Exposed for WebApplicationFactory in the integration tests.</summary>
public partial class Program;
