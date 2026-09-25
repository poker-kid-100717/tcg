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
    public sealed class ApiFixture : IAsyncLifetime
    {
        private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();

        public FakePokemonTcgApi Upstream { get; } = new();
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
                builder.ConfigureTestServices(services =>
                    services.AddHttpClient<PokemonTcgClient>().ConfigurePrimaryHttpMessageHandler(() => Upstream));
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

        public HttpClient CreateClient() => Factory.CreateClient();

        public async Task DisposeAsync()
        {
            await Factory.DisposeAsync();
            await _postgres.DisposeAsync();
        }
    }

    [CollectionDefinition(Name)]
    public class ApiCollection : ICollectionFixture<ApiFixture>
    {
        public const string Name = "api";
    }
}
