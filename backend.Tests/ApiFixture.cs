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
                foreach (var (key, value) in Settings) builder.UseSetting(key, value);
                builder.ConfigureTestServices(services =>
                {
                    services.AddHttpClient<PokemonTcgClient>().ConfigurePrimaryHttpMessageHandler(() => Upstream);
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
