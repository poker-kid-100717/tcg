using Microsoft.Extensions.Diagnostics.HealthChecks;
using PokemonTCG.API.Data;

namespace PokemonTcgMarketplace.Backend.Tests
{
    public class DatabaseInitializationHealthCheckTests
    {
        private static Task<HealthCheckResult> Check(DatabaseInitializationStatus status) =>
            new DatabaseInitializationHealthCheck(status).CheckHealthAsync(new HealthCheckContext());

        [Fact]
        public async Task IsUnhealthyUntilInitializationSucceeds()
        {
            Assert.Equal(HealthStatus.Unhealthy, (await Check(new DatabaseInitializationStatus())).Status);
        }

        [Fact]
        public async Task IsUnhealthyWithTheErrorWhenInitializationFailed()
        {
            var status = new DatabaseInitializationStatus();
            var error = new InvalidOperationException("permission denied for schema public");
            status.MarkFailed(error);

            var result = await Check(status);

            Assert.Equal(HealthStatus.Unhealthy, result.Status);
            Assert.Same(error, result.Exception);
        }

        [Fact]
        public async Task IsHealthyAfterInitializationSucceeds()
        {
            var status = new DatabaseInitializationStatus();
            status.MarkSucceeded();

            Assert.Equal(HealthStatus.Healthy, (await Check(status)).Status);
        }
    }
}
