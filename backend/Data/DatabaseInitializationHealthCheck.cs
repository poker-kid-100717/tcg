using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace PokemonTCG.API.Data
{
    /// <summary>
    /// Outcome of the migration and seed run by <see cref="DatabaseInitializer"/>.
    /// Until it succeeds the app must not report ready: being able to connect
    /// says nothing about the schema.
    /// </summary>
    public class DatabaseInitializationStatus
    {
        public bool Succeeded { get; private set; }
        public Exception? Error { get; private set; }

        public void MarkSucceeded() => (Succeeded, Error) = (true, null);
        public void MarkFailed(Exception error) => (Succeeded, Error) = (false, error);
    }

    public class DatabaseInitializationHealthCheck : IHealthCheck
    {
        private readonly DatabaseInitializationStatus _status;

        public DatabaseInitializationHealthCheck(DatabaseInitializationStatus status)
        {
            _status = status;
        }

        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(_status.Succeeded
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Database migration or seeding has not completed yet.", _status.Error));
    }
}
