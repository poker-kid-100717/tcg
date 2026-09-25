using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace PokemonTCG.API.Data
{
    /// <summary>
    /// Outcome of the startup migration and seed. The app keeps serving if
    /// they fail (so the error is visible in logs and /health), but it must
    /// not report ready: being able to connect says nothing about the schema.
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
                : HealthCheckResult.Unhealthy("Database migration or seeding did not complete at startup.", _status.Error));
    }
}
