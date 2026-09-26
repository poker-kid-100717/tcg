using System.Net;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using TcgSignal.Inventory;
using TcgSignal.Inventory.Domain;
using TcgSignal.Inventory.Providers.BestBuy;

namespace TcgSignal.Inventory.Tests;

public sealed class BestBuyInventoryProviderTests
{
    [Fact]
    public async Task Missing_key_never_calls_retailer_or_claims_stock()
    {
        using var http = new HttpClient(new ThrowingHandler()) { BaseAddress = new Uri("https://api.bestbuy.com/") };
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var provider = new BestBuyInventoryProvider(
            http,
            cache,
            new InventoryOptions(),
            TimeProvider.System,
            NullLogger<BestBuyInventoryProvider>.Instance);

        var result = await provider.SearchAsync(new InventoryQuery(35.0844, -106.6504, 25), CancellationToken.None);

        Assert.Empty(result.Listings);
        Assert.Equal("NeedsConfiguration", result.Coverage.Status);
    }

    [Fact]
    public async Task Returns_only_store_specific_in_stock_evidence_inside_radius()
    {
        using var http = new HttpClient(new BestBuyStubHandler()) { BaseAddress = new Uri("https://api.bestbuy.com/") };
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var provider = new BestBuyInventoryProvider(
            http,
            cache,
            new InventoryOptions
            {
                BestBuyApiKey = "test-key",
                MaxProductsPerRefresh = 10,
                CacheSeconds = 1,
            },
            TimeProvider.System,
            NullLogger<BestBuyInventoryProvider>.Instance);

        var result = await provider.SearchAsync(new InventoryQuery(35.0844, -106.6504, 25), CancellationToken.None);

        var listing = Assert.Single(result.Listings);
        Assert.Equal("Best Buy", listing.Retailer);
        Assert.Equal("1234567", listing.ProductSku);
        Assert.Equal("Confirmed", listing.Availability);
        Assert.Equal(98, listing.ConfidenceScore);
        Assert.False(listing.LowStock);
        Assert.InRange(listing.DistanceMiles, 0, 25);
        Assert.Contains("near-real-time", listing.Evidence, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Live", result.Coverage.Status);
    }

    [Fact]
    public async Task Low_stock_is_disclosed_and_receives_lower_confidence()
    {
        using var http = new HttpClient(new BestBuyStubHandler(lowStock: true)) { BaseAddress = new Uri("https://api.bestbuy.com/") };
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var provider = new BestBuyInventoryProvider(
            http,
            cache,
            new InventoryOptions
            {
                BestBuyApiKey = "test-key",
                MaxProductsPerRefresh = 10,
                CacheSeconds = 1,
            },
            TimeProvider.System,
            NullLogger<BestBuyInventoryProvider>.Instance);

        var listing = Assert.Single((await provider.SearchAsync(
            new InventoryQuery(35.0844, -106.6504, 25),
            CancellationToken.None)).Listings);

        Assert.True(listing.LowStock);
        Assert.Equal(94, listing.ConfidenceScore);
        Assert.Contains("low stock", listing.Evidence, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new Xunit.Sdk.XunitException($"HTTP should not have been called: {request.RequestUri}");
    }

    private sealed class BestBuyStubHandler(bool lowStock = false) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.PathAndQuery ?? "";

            if (path.StartsWith("/v1/stores(area(", StringComparison.Ordinal))
            {
                return Json(
                    """
                    {
                      "stores": [
                        {
                          "storeId": 1,
                          "name": "Albuquerque",
                          "address": "2100 Louisiana Blvd NE",
                          "city": "Albuquerque",
                          "region": "NM",
                          "postalCode": "87110",
                          "lat": 35.1010,
                          "lng": -106.5670
                        },
                        {
                          "storeId": 2,
                          "name": "Far Away",
                          "address": "1 Far Road",
                          "city": "Santa Fe",
                          "region": "NM",
                          "postalCode": "87501",
                          "lat": 35.6870,
                          "lng": -105.9380
                        }
                      ]
                    }
                    """);
            }

            if (path.StartsWith("/v1/products(search=pokemon", StringComparison.Ordinal))
            {
                return Json(
                    """
                    {
                      "products": [
                        {
                          "sku": 1234567,
                          "name": "Pokemon Trading Card Game - Mega Evolution Elite Trainer Box",
                          "salePrice": 54.99,
                          "image": "https://example.invalid/etb.jpg",
                          "url": "https://example.invalid/product",
                          "inStorePickup": true
                        }
                      ]
                    }
                    """);
            }

            if (path.StartsWith("/v1/products/1234567/stores.json", StringComparison.Ordinal))
            {
                return Json($$"""
                    {
                      "ispuEligible": true,
                      "stores": [
                        { "storeID": "1", "lowStock": {{lowStock.ToString().ToLowerInvariant()}} },
                        { "storeID": "2", "lowStock": false }
                      ]
                    }
                    """);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static Task<HttpResponseMessage> Json(string json) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
    }
}
