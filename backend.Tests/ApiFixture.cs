using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using PokemonTCG.API.Pricing;
using Testcontainers.PostgreSql;

namespace PokemonTcgMarketplace.Backend.Tests
{
    /// <summary>
    /// The real API against a real Postgres (Testcontainers), with the
    /// Pokémon TCG API replaced by <see cref="FakePokemonTcgApi"/>.
    /// </summary>
    public class ApiFixture : IAsyncLifetime
    {
        private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();

        public FakePokemonTcgApi Upstream { get; } = new();

        /// <summary>Stripe, faked: billing is "configured" with fixture keys that only this handler accepts.</summary>
        public FakeStripeApi Stripe { get; } = new();

        /// <summary>The API's clock: real time plus <see cref="TestClock.Offset"/>, so a test can record "last week".</summary>
        public TestClock Clock { get; } = new();
        public WebApplicationFactory<Program> Factory { get; private set; } = null!;

        public async Task InitializeAsync()
        {
            await _postgres.StartAsync();
            Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.UseSetting("ConnectionStrings:DefaultConnection", _postgres.GetConnectionString());
                builder.UseSetting("PokemonTcgApi:BaseUrl", "https://tcg.test/v2/");
                builder.UseSetting("Snapshots:PageDelayMilliseconds", "0");
                builder.UseSetting("Auth:LocalLogin", "true");
                builder.UseSetting("RateLimits:AuthPerFiveMinutes", "10000");
                builder.UseSetting("RateLimits:BillingPerMinute", "10000");
                builder.UseSetting("Billing:StripeSecretKey", FakeStripeApi.SecretKey);
                builder.UseSetting("Billing:StripeWebhookSecret", FakeStripeApi.WebhookSecret);
                builder.UseSetting("Billing:ProMonthlyPriceId", FakeStripeApi.MonthlyPrice);
                builder.UseSetting("Billing:ProAnnualPriceId", FakeStripeApi.AnnualPrice);
                builder.UseSetting("Billing:StoreFinderMonthlyPriceId", FakeStripeApi.StoreFinderPrice);
                foreach (var (key, value) in Settings) builder.UseSetting(key, value);
                builder.ConfigureTestServices(services =>
                {
                    services.AddHttpClient<PokemonTcgClient>().ConfigurePrimaryHttpMessageHandler(() => Upstream);
                    services.AddHttpClient<PokemonTCG.API.Product.StripeBillingService>().ConfigurePrimaryHttpMessageHandler(() => Stripe);
                    ConfigureServices(services);
                    services.AddSingleton<TimeProvider>(Clock);
                });
            });

            // Wait for the startup migration before tests use the database.
            using var client = Factory.CreateClient();
            for (var attempt = 0; attempt < 60; attempt++)
            {
                if ((await client.GetAsync("/health")).IsSuccessStatusCode) return;
                await Task.Delay(500);
            }
            throw new TimeoutException("The API never reported healthy.");
        }

        /// <summary>Extra configuration for fixtures that need it.</summary>
        protected virtual IEnumerable<(string Key, string Value)> Settings => [];

        /// <summary>Extra service overrides for fixtures that need them.</summary>
        protected virtual void ConfigureServices(IServiceCollection services) { }

        public HttpClient CreateClient() => Factory.CreateClient();

        /// <summary>A browser signed in (local test sign-in) as a new account. Returns the account id.</summary>
        public async Task<(HttpClient Client, Guid UserId)> SignInAsync(string? email = null)
        {
            var client = CreateClient();
            var response = await client.PostAsJsonAsync("/api/auth/local-login", new { email = email ?? $"{Guid.NewGuid():N}@example.test" });
            response.EnsureSuccessStatusCode();
            var me = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/me");
            return (client, me.GetProperty("account").GetProperty("id").GetGuid());
        }

        /// <summary>A card with one day of holofoil prices, so it can be watched. Returns its id.</summary>
        public async Task<string> SeedCardAsync(decimal market = 10m)
        {
            var id = $"seed-{Guid.NewGuid():N}"[..20];
            using var scope = Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PokemonTCG.API.Data.AppDbContext>();
            db.Cards.Add(new PokemonTCG.API.Data.Card { Id = id, Name = "Seed card", Number = "1", SetId = "seed", SetName = "Seed set" });
            db.PriceSnapshots.Add(new PokemonTCG.API.Data.PriceSnapshot
            {
                CardId = id, Variant = "holofoil", Date = DateOnly.FromDateTime(Clock.GetUtcNow().UtcDateTime), Market = market,
            });
            await db.SaveChangesAsync();
            return id;
        }

        /// <summary>Makes the account Pro the only way production does: Stripe reports an active subscription and a signed webhook says so.</summary>
        public async Task<string> GrantProAsync(Guid userId, string status = "active")
        {
            var subscriptionId = $"sub_{Guid.NewGuid():N}";
            var customerId = $"cus_{Guid.NewGuid():N}";
            Stripe.SetSubscription(subscriptionId, customerId, status, FakeStripeApi.MonthlyPrice, userId);
            using var client = CreateClient();
            using var request = FakeStripeApi.Webhook("checkout.session.completed", new
            {
                id = "cs_test", @object = "checkout.session", client_reference_id = userId.ToString(), customer = customerId, subscription = subscriptionId,
            });
            (await client.SendAsync(request)).EnsureSuccessStatusCode();
            return subscriptionId;
        }

        public async Task DisposeAsync()
        {
            await Factory.DisposeAsync();
            await _postgres.DisposeAsync();
        }
    }

    /// <summary>
    /// Its own database, so the prediction tests control every price the model sees, and a model sized
    /// for a few thousand rows: a 7-day horizon sampled daily.
    /// </summary>
    public sealed class PredictionsFixture : ApiFixture
    {
        protected override IEnumerable<(string Key, string Value)> Settings =>
        [
            ("Predictions:HorizonDays", "7"),
            ("Predictions:SampleEveryDays", "1"),
            ("Predictions:MinTrainingRows", "200"),
            ("Predictions:MinValidationRows", "50"),
            ("Predictions:Trees", "100"),
            ("Predictions:MinExamplesPerLeaf", "5"),
        ];
    }

    /// <summary>Its own database, with TCGplayer Developer API keys set and TCGplayer replaced by <see cref="Tcgplayer.FakeTcgplayerApi"/>.</summary>
    public sealed class TcgplayerFixture : ApiFixture
    {
        public Tcgplayer.FakeTcgplayerApi TcgplayerApi { get; } = new();

        protected override IEnumerable<(string Key, string Value)> Settings =>
        [
            ("Tcgplayer:PublicKey", Tcgplayer.FakeTcgplayerApi.PublicKey),
            ("Tcgplayer:PrivateKey", Tcgplayer.FakeTcgplayerApi.PrivateKey),
            ("Tcgplayer:BaseUrl", "https://tcgplayer.test"),
        ];

        protected override void ConfigureServices(IServiceCollection services)
        {
            services.AddHttpClient(PokemonTCG.API.Pricing.Tcgplayer.TcgplayerTokenProvider.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => TcgplayerApi);
            services.AddHttpClient<PokemonTCG.API.Pricing.Tcgplayer.TcgplayerClient>().ConfigurePrimaryHttpMessageHandler(() => TcgplayerApi);
        }
    }

    /// <summary>
    /// Its own API with an OpenID Connect provider configured. The provider's metadata is supplied up front, so the
    /// sign-in redirect can be checked without any network call to a real identity provider.
    /// </summary>
    public sealed class OidcFixture : ApiFixture
    {
        public const string AuthorizeEndpoint = "https://id.example.test/authorize";

        protected override IEnumerable<(string Key, string Value)> Settings =>
        [
            ("Auth:Authority", "https://id.example.test/"),
            ("Auth:ClientId", "tcg-signal-test"),
            ("Auth:ClientSecret", "fixture-client-secret-not-real"),
        ];

        protected override void ConfigureServices(IServiceCollection services) =>
            services.PostConfigure<Microsoft.AspNetCore.Authentication.OpenIdConnect.OpenIdConnectOptions>(
                PokemonTCG.API.Accounts.AuthSetup.OidcScheme,
                o =>
                {
                    o.Configuration = new Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfiguration
                    {
                        Issuer = "https://id.example.test/",
                        AuthorizationEndpoint = AuthorizeEndpoint,
                        TokenEndpoint = "https://id.example.test/token",
                    };
                    o.ConfigurationManager = new Microsoft.IdentityModel.Protocols.StaticConfigurationManager<
                        Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfiguration>(o.Configuration);
                });
    }

    /// <summary>Its own database, so alert and signal tests control which day is "latest" across the market.</summary>
    public sealed class AlertsFixture : ApiFixture;

    [CollectionDefinition(Name)]
    public class AlertsCollection : ICollectionFixture<AlertsFixture>
    {
        public const string Name = "alerts";
    }

    [CollectionDefinition(Name)]
    public class OidcCollection : ICollectionFixture<OidcFixture>
    {
        public const string Name = "oidc";
    }

    [CollectionDefinition(Name)]
    public class TcgplayerCollection : ICollectionFixture<TcgplayerFixture>
    {
        public const string Name = "tcgplayer";
    }

    [CollectionDefinition(Name)]
    public class PredictionsCollection : ICollectionFixture<PredictionsFixture>
    {
        public const string Name = "predictions";
    }

    [CollectionDefinition(Name)]
    public class ApiCollection : ICollectionFixture<ApiFixture>
    {
        public const string Name = "api";
    }

    public sealed class TestClock : TimeProvider
    {
        public TimeSpan Offset { get; set; }
        public override DateTimeOffset GetUtcNow() => System.GetUtcNow() + Offset;
    }
}
