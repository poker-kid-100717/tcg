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
        private readonly TaskCompletionSource _succeeded = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Succeeded => _succeeded.Task.IsCompleted;
        public Exception? Error { get; private set; }

        /// <summary>Completes once initialization has succeeded.</summary>
        public Task WhenSucceeded => _succeeded.Task;

        public void MarkSucceeded()
        {
            Error = null;
            _succeeded.TrySetResult();
        }

        public void MarkFailed(Exception error) => Error = error;
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
