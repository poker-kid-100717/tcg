using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Http.Resilience;
using PokemonTCG.API.Data;
using PokemonTCG.API.Email;
using PokemonTCG.API.Pricing;
using PokemonTCG.API.Pricing.Predictions;
using PokemonTCG.API.Product;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddProblemDetails();
// Enums (such as a snapshot run's status) read as names, not numbers.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
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
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
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
var normalizedConnectionString = PostgresConnectionString.Normalize(connectionString);
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(normalizedConnectionString));

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

builder.Services.AddSingleton(builder.Configuration.GetSection(SnapshotOptions.SectionName).Get<SnapshotOptions>() ?? new());
builder.Services.AddScoped<PriceSnapshotService>();
builder.Services.AddScoped<PriceGuideService>();
builder.Services.AddSingleton(builder.Configuration.GetSection(PredictionOptions.SectionName).Get<PredictionOptions>() ?? new());
builder.Services.AddScoped<PredictionService>();

// TCG Signal MVP: device-bound collector profiles, watchlists, explainable market
// intelligence and optional Stripe subscriptions. Without Stripe configuration,
// the app deliberately runs as a full-feature founding preview.
var billingOptions = builder.Configuration.GetSection(BillingOptions.SectionName).Get<BillingOptions>() ?? new();
builder.Services.AddSingleton(billingOptions);
builder.Services.AddSingleton(builder.Configuration.GetSection(FeatureOptions.SectionName).Get<FeatureOptions>() ?? new());
builder.Services.AddSingleton(new ProductStore(normalizedConnectionString));
builder.Services.AddSingleton(new MasterSetStore(normalizedConnectionString));
builder.Services.AddScoped<MasterSetService>();
builder.Services.AddSingleton(sp => new AccountStore(normalizedConnectionString, sp.GetRequiredService<ILogger<AccountStore>>()));
builder.Services.AddScoped<SessionService>();
builder.Services.AddScoped<SignInService>();
builder.Services.AddScoped<EntitlementService>();

// Email (sign-in codes and alert digests) goes through Resend. Without a key, development logs
// messages instead, and a deployment reports sign-in as unavailable rather than failing silently.
var emailOptions = builder.Configuration.GetSection(EmailOptions.SectionName).Get<EmailOptions>() ?? new();
builder.Services.AddSingleton(emailOptions);
if (emailOptions.IsConfigured)
{
    builder.Services.AddHttpClient<IEmailSender, ResendEmailSender>(client =>
            client.BaseAddress = new Uri("https://api.resend.com/"))
        .AddStandardResilienceHandler();
}
else if (builder.Environment.IsDevelopment())
{
    builder.Services.AddSingleton<IEmailSender, DevelopmentEmailSender>();
}
else
{
    builder.Services.AddSingleton<IEmailSender, UnavailableEmailSender>();
}

// Sign-in endpoints are limited per client IP (the Worker forwards it as X-Forwarded-For), on top
// of the per-address and per-code limits in SignInService and AccountStore.
var codeRequestsPerIp = builder.Configuration.GetValue("SignIn:CodeRequestsPerIp", 10);
var verifyRequestsPerIp = builder.Configuration.GetValue("SignIn:VerifyRequestsPerIp", 30);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(AccountEndpoints.CodeRateLimit, context => PerIp(context, codeRequestsPerIp));
    options.AddPolicy(AccountEndpoints.VerifyRateLimit, context => PerIp(context, verifyRequestsPerIp));
});
var scrydexOptions = builder.Configuration.GetSection(ScrydexOptions.SectionName).Get<ScrydexOptions>() ?? new();
builder.Services.AddSingleton(scrydexOptions);
builder.Services.AddHttpClient<ScrydexClient>(client =>
{
    client.BaseAddress = new Uri(scrydexOptions.BaseUrl.EndsWith('/') ? scrydexOptions.BaseUrl : scrydexOptions.BaseUrl + "/");
    client.Timeout = TimeSpan.FromSeconds(20);
});
builder.Services.AddScoped<MarketIntelligenceService>();
builder.Services.AddScoped<AlertEvaluationService>();
builder.Services.AddScoped<AlertEmailService>();
builder.Services.AddHttpClient<StripeBillingService>();

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
app.UseRateLimiter();

app.MapPriceGuide();
app.MapPredictions();
app.MapProduct();
app.MapAccounts();
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });

app.Run();

static RateLimitPartition<string> PerIp(HttpContext context, int permitsPer15Minutes) =>
    RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitsPer15Minutes,
            Window = TimeSpan.FromMinutes(15),
            QueueLimit = 0,
        });

/// <summary>Exposed for WebApplicationFactory in the integration tests.</summary>
public partial class Program;
