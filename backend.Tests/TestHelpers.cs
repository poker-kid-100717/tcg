using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using PokemonTCG.API.Data;

namespace PokemonTcgMarketplace.Backend.Tests
{
    /// <summary>
    /// Shared test setup helpers. Each test that needs a database gets its own
    /// uniquely-named EF Core InMemory database so tests never see each
    /// other's data, even when run in parallel. Production code uses PostgreSQL -
    /// InMemory is a deliberate, standard trade-off for fast, isolated unit tests.
    /// </summary>
    public static class TestHelpers
    {
        public static AppDbContext CreateInMemoryContext(string? dbName = null)
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(dbName ?? Guid.NewGuid().ToString())
                .Options;

            return new AppDbContext(options);
        }

        public static IConfiguration CreateJwtConfiguration(string key = "unit-test-signing-key-at-least-32-bytes-long")
        {
            var settings = new Dictionary<string, string?>
            {
                ["JwtSettings:Key"] = key,
                ["JwtSettings:Issuer"] = "PokemonTCGApi",
                ["JwtSettings:Audience"] = "PokemonTCGClient",
                ["JwtSettings:DurationInMinutes"] = "60"
            };

            return new ConfigurationBuilder()
                .AddInMemoryCollection(settings)
                .Build();
        }
    }
}
