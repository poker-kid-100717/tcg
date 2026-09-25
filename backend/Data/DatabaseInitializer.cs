namespace PokemonTCG.API.Data
{
    /// <summary>
    /// Migrates and seeds the database in the background, retrying with
    /// backoff until it succeeds. A database that is briefly unreachable when
    /// the container starts (e.g. Neon waking from suspend) therefore delays
    /// readiness instead of leaving it failed for the container's lifetime.
    /// </summary>
    public class DatabaseInitializer : BackgroundService
    {
        private static readonly TimeSpan MaxDelay = TimeSpan.FromMinutes(1);

        private readonly IServiceScopeFactory _scopes;
        private readonly DatabaseInitializationStatus _status;
        private readonly ILogger<DatabaseInitializer> _logger;
        private readonly TimeSpan _initialDelay;

        public DatabaseInitializer(IServiceScopeFactory scopes, DatabaseInitializationStatus status, ILogger<DatabaseInitializer> logger)
            : this(scopes, status, logger, TimeSpan.FromSeconds(2))
        {
        }

        public DatabaseInitializer(IServiceScopeFactory scopes, DatabaseInitializationStatus status, ILogger<DatabaseInitializer> logger, TimeSpan initialDelay)
        {
            _scopes = scopes;
            _status = status;
            _logger = logger;
            _initialDelay = initialDelay;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Don't hold up host startup: /health/live must answer meanwhile.
            await Task.Yield();

            var delay = _initialDelay;
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    await InitializeOnceAsync(stoppingToken);
                    _status.MarkSucceeded();
                    return;
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    _status.MarkFailed(ex);
                    _logger.LogError(ex, "Migrating or seeding the database failed (attempt {Attempt}); retrying in {Delay}.", attempt, delay);
                }

                await Task.Delay(delay, stoppingToken);
                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxDelay.Ticks));
            }
        }

        protected virtual Task InitializeOnceAsync(CancellationToken cancellationToken)
        {
            using var scope = _scopes.CreateScope();
            DbInitializer.Initialize(scope.ServiceProvider.GetRequiredService<AppDbContext>());
            return Task.CompletedTask;
        }
    }
}
