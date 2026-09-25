using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PokemonTCG.API.Data;

namespace PokemonTcgMarketplace.Backend.Tests
{
    public class DatabaseInitializerTests
    {
        private sealed class FlakyInitializer : DatabaseInitializer
        {
            private readonly int _failures;
            public int Attempts { get; private set; }

            public FlakyInitializer(DatabaseInitializationStatus status, int failures)
                : base(new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
                    status, NullLogger<DatabaseInitializer>.Instance, TimeSpan.FromMilliseconds(10))
            {
                _failures = failures;
            }

            protected override Task InitializeOnceAsync(CancellationToken cancellationToken)
            {
                Attempts++;
                return Attempts <= _failures
                    ? Task.FromException(new InvalidOperationException("database unreachable"))
                    : Task.CompletedTask;
            }
        }

        private static async Task WaitUntil(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!condition())
            {
                Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for the initializer.");
                await Task.Delay(10);
            }
        }

        [Fact]
        public async Task RetriesUntilTheDatabaseIsReachable()
        {
            var status = new DatabaseInitializationStatus();
            var initializer = new FlakyInitializer(status, failures: 2);

            await initializer.StartAsync(CancellationToken.None);
            await WaitUntil(() => status.Succeeded);
            await initializer.StopAsync(CancellationToken.None);

            Assert.Equal(3, initializer.Attempts);
            Assert.Null(status.Error);
        }

        [Fact]
        public async Task StopsRetryingWhenTheHostShutsDown()
        {
            var status = new DatabaseInitializationStatus();
            var initializer = new FlakyInitializer(status, failures: int.MaxValue);

            await initializer.StartAsync(CancellationToken.None);
            await WaitUntil(() => initializer.Attempts >= 1);
            await initializer.StopAsync(CancellationToken.None);

            Assert.False(status.Succeeded);
            Assert.IsType<InvalidOperationException>(status.Error);
        }
    }
}
