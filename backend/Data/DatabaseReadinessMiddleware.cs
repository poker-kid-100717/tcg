namespace PokemonTCG.API.Data
{
    /// <summary>
    /// Holds API requests that arrive while the database is still being
    /// migrated or seeded (a cold container start, or a fresh database), so
    /// they don't run against a missing schema. If initialization takes
    /// longer than <see cref="MaxWait"/>, the request gets a 503 with
    /// Retry-After instead of a 500 from a half-built database.
    /// </summary>
    public class DatabaseReadinessMiddleware
    {
        public static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(20);

        private readonly RequestDelegate _next;
        private readonly DatabaseInitializationStatus _status;
        private readonly TimeSpan _maxWait;

        public DatabaseReadinessMiddleware(RequestDelegate next, DatabaseInitializationStatus status)
            : this(next, status, MaxWait)
        {
        }

        public DatabaseReadinessMiddleware(RequestDelegate next, DatabaseInitializationStatus status, TimeSpan maxWait)
        {
            _next = next;
            _status = status;
            _maxWait = maxWait;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (!_status.Succeeded && (context.Request.Path.StartsWithSegments("/api") || context.Request.Path.StartsWithSegments("/internal")))
            {
                await Task.WhenAny(_status.WhenSucceeded, Task.Delay(_maxWait, context.RequestAborted));
                if (!_status.Succeeded)
                {
                    context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                    context.Response.Headers.RetryAfter = "5";
                    await context.Response.WriteAsJsonAsync(
                        new { message = "The service is starting up. Please try again in a few seconds." },
                        context.RequestAborted);
                    return;
                }
            }

            await _next(context);
        }
    }
}
