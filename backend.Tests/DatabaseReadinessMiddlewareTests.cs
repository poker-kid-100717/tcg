using Microsoft.AspNetCore.Http;
using PokemonTCG.API.Data;

namespace PokemonTcgMarketplace.Backend.Tests
{
    public class DatabaseReadinessMiddlewareTests
    {
        private static (DatabaseReadinessMiddleware Middleware, Func<bool> NextCalled) Create(DatabaseInitializationStatus status, TimeSpan maxWait)
        {
            var called = false;
            var middleware = new DatabaseReadinessMiddleware(_ => { called = true; return Task.CompletedTask; }, status, maxWait);
            return (middleware, () => called);
        }

        private static DefaultHttpContext Request(string path)
        {
            var context = new DefaultHttpContext();
            context.Request.Path = path;
            context.Response.Body = new MemoryStream();
            return context;
        }

        [Fact]
        public async Task PassesApiRequestsThroughOnceInitialized()
        {
            var status = new DatabaseInitializationStatus();
            status.MarkSucceeded();
            var (middleware, nextCalled) = Create(status, TimeSpan.FromSeconds(5));

            await middleware.InvokeAsync(Request("/api/cards"));

            Assert.True(nextCalled());
        }

        [Fact]
        public async Task HoldsAnApiRequestUntilInitializationFinishes()
        {
            var status = new DatabaseInitializationStatus();
            var (middleware, nextCalled) = Create(status, TimeSpan.FromSeconds(10));

            var pending = middleware.InvokeAsync(Request("/api/orders"));
            await Task.Delay(50);
            Assert.False(pending.IsCompleted);

            status.MarkSucceeded();
            await pending;

            Assert.True(nextCalled());
        }

        [Fact]
        public async Task Returns503WithRetryAfterWhenInitializationTakesTooLong()
        {
            var status = new DatabaseInitializationStatus();
            status.MarkFailed(new InvalidOperationException("database unreachable"));
            var (middleware, nextCalled) = Create(status, TimeSpan.FromMilliseconds(50));
            var context = Request("/api/auth/login");

            await middleware.InvokeAsync(context);

            Assert.False(nextCalled());
            Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
            Assert.Equal("5", context.Response.Headers.RetryAfter.ToString());
        }

        [Fact]
        public async Task NeverHoldsHealthChecks()
        {
            var (middleware, nextCalled) = Create(new DatabaseInitializationStatus(), TimeSpan.FromSeconds(10));

            await middleware.InvokeAsync(Request("/health/live"));

            Assert.True(nextCalled());
        }
    }
}
