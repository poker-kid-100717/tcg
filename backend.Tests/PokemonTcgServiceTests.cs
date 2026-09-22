using System.Net;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using PokemonTCG.API.Services;

namespace PokemonTcgMarketplace.Backend.Tests
{
    /// <summary>An HttpMessageHandler stub that counts requests and returns canned JSON, so tests never hit the real network.</summary>
    public class StubHttpMessageHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        private readonly string _responseJson;

        public StubHttpMessageHandler(string responseJson)
        {
            _responseJson = responseJson;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseJson)
            };
            return Task.FromResult(response);
        }
    }

    public class PokemonTcgServiceTests
    {
        private static PokemonTcgService CreateService(StubHttpMessageHandler handler, out IMemoryCache cache)
        {
            var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.pokemontcg.io/v2/") };
            var configuration = new ConfigurationBuilder().Build();
            cache = new MemoryCache(new MemoryCacheOptions());
            return new PokemonTcgService(httpClient, configuration, cache, NullLogger<PokemonTcgService>.Instance);
        }

        [Fact]
        public async Task GetCardByIdAsync_ParsesJsonResponse()
        {
            var handler = new StubHttpMessageHandler("{\"data\":{\"id\":\"sv4-172\",\"name\":\"Charizard ex\"}}");
            var service = CreateService(handler, out _);

            var result = await service.GetCardByIdAsync("sv4-172");

            Assert.Equal("Charizard ex", result.RootElement.GetProperty("data").GetProperty("name").GetString());
        }

        [Fact]
        public async Task GetCardByIdAsync_SecondCallForSameId_IsServedFromCacheNotHttp()
        {
            var handler = new StubHttpMessageHandler("{\"data\":{\"id\":\"sv4-172\",\"name\":\"Charizard ex\"}}");
            var service = CreateService(handler, out _);

            await service.GetCardByIdAsync("sv4-172");
            await service.GetCardByIdAsync("sv4-172");

            Assert.Equal(1, handler.RequestCount);
        }

        [Fact]
        public async Task GetCardByIdAsync_DifferentIds_EachHitHttpOnce()
        {
            var handler = new StubHttpMessageHandler("{\"data\":{\"id\":\"x\",\"name\":\"Whatever\"}}");
            var service = CreateService(handler, out _);

            await service.GetCardByIdAsync("sv4-172");
            await service.GetCardByIdAsync("sv4-173");

            Assert.Equal(2, handler.RequestCount);
        }
    }
}
