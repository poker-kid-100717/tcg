using System.Globalization;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using TcgSignal.Inventory.Application;
using TcgSignal.Inventory.Domain;

namespace TcgSignal.Inventory.Providers.BestBuy;

/// <summary>
/// Official Best Buy Developer API integration. The documented SKU store-availability endpoint returns only
/// stores where the SKU is currently in stock and is described by Best Buy as near-real-time.
/// </summary>
public sealed class BestBuyInventoryProvider(
    HttpClient http,
    IMemoryCache cache,
    InventoryOptions options,
    TimeProvider clock,
    ILogger<BestBuyInventoryProvider> logger) : IInventoryProvider
{
    public string Retailer => "Best Buy";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.BestBuyApiKey);

    public async Task<ProviderInventoryResult> SearchAsync(InventoryQuery query, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
            return new ProviderInventoryResult([], new ProviderCoverage(Retailer, "NeedsConfiguration", "Best Buy API key is not configured."));

        var latBucket = Math.Round(query.Latitude, 2);
        var lngBucket = Math.Round(query.Longitude, 2);
        var cacheKey = $"bestbuy:{latBucket}:{lngBucket}:{query.RadiusMiles}";
        if (cache.TryGetValue(cacheKey, out ProviderInventoryResult? cached) && cached is not null)
            return cached;

        var stores = await GetStoresAsync(query, cancellationToken);
        if (stores.Count == 0)
        {
            var none = new ProviderInventoryResult(
                [],
                new ProviderCoverage(Retailer, "Live", "Official feed checked successfully; no Best Buy stores are inside this radius.", clock.GetUtcNow()));
            cache.Set(cacheKey, none, TimeSpan.FromSeconds(options.CacheSeconds));
            return none;
        }

        var products = await GetPokemonProductsAsync(cancellationToken);
        if (products.Count == 0)
        {
            var none = new ProviderInventoryResult(
                [],
                new ProviderCoverage(Retailer, "Live", "Official feed checked successfully; no active Pokémon TCG pickup products were returned.", clock.GetUtcNow()));
            cache.Set(cacheKey, none, TimeSpan.FromSeconds(options.CacheSeconds));
            return none;
        }

        var postalCode = stores.OrderBy(s => DistanceMiles(query.Latitude, query.Longitude, s.Lat, s.Lng))
            .Select(s => s.PostalCode)
            .First();
        var storeMap = stores.ToDictionary(s => s.StoreId, StringComparer.OrdinalIgnoreCase);
        var observed = clock.GetUtcNow();
        var listings = new List<InventoryListing>();

        foreach (var product in products.Take(options.MaxProductsPerRefresh))
        {
            var availability = await GetAvailabilityAsync(product.Sku, postalCode, cancellationToken);
            foreach (var hit in availability.Stores)
            {
                if (!storeMap.TryGetValue(hit.StoreId, out var store)) continue;
                var distance = DistanceMiles(query.Latitude, query.Longitude, store.Lat, store.Lng);
                if (distance > query.RadiusMiles) continue;

                var lowStock = hit.LowStock;
                listings.Add(new InventoryListing(
                    Retailer,
                    store.StoreId,
                    store.Name,
                    store.Address,
                    store.City,
                    store.Region,
                    store.PostalCode,
                    store.Lat,
                    store.Lng,
                    Math.Round(distance, 1),
                    product.Sku,
                    product.Name,
                    Category(product.Name),
                    product.Image,
                    product.Url,
                    product.SalePrice,
                    lowStock,
                    "Confirmed",
                    lowStock ? 94 : 98,
                    observed,
                    "Best Buy Developer API",
                    lowStock
                        ? "Official near-real-time store availability returned this SKU and marked it low stock."
                        : "Official near-real-time store availability returned this SKU as in stock."));
            }

            await Task.Delay(TimeSpan.FromMilliseconds(220), cancellationToken);
        }

        var deduped = listings
            .DistinctBy(x => $"{x.StoreId}:{x.ProductSku}", StringComparer.OrdinalIgnoreCase)
            .ToList();
        var result = new ProviderInventoryResult(
            deduped,
            new ProviderCoverage(
                Retailer,
                "Live",
                $"Official near-real-time availability checked for {Math.Min(products.Count, options.MaxProductsPerRefresh)} active Pokémon TCG products.",
                observed));

        cache.Set(cacheKey, result, TimeSpan.FromSeconds(options.CacheSeconds));
        logger.LogInformation("Best Buy inventory search returned {Listings} listings across {Stores} nearby stores", deduped.Count, stores.Count);
        return result;
    }

    private async Task<List<BestBuyStore>> GetStoresAsync(InventoryQuery query, CancellationToken cancellationToken)
    {
        var lat = query.Latitude.ToString("0.######", CultureInfo.InvariantCulture);
        var lng = query.Longitude.ToString("0.######", CultureInfo.InvariantCulture);
        var path = $"v1/stores(area({lat},{lng},{query.RadiusMiles}))?format=json&show=storeId,name,address,city,region,postalCode,lat,lng&pageSize=100&apiKey={Uri.EscapeDataString(options.BestBuyApiKey)}";
        var response = await http.GetFromJsonAsync<BestBuyStoresResponse>(path, cancellationToken)
            ?? throw new InvalidOperationException("Best Buy stores response was empty.");
        return response.Stores
            .Where(s => !string.IsNullOrWhiteSpace(s.StoreId) && s.Lat is >= -90 and <= 90 && s.Lng is >= -180 and <= 180)
            .Where(s => DistanceMiles(query.Latitude, query.Longitude, s.Lat, s.Lng) <= query.RadiusMiles)
            .ToList();
    }

    private async Task<List<BestBuyProduct>> GetPokemonProductsAsync(CancellationToken cancellationToken)
    {
        var path =
            $"v1/products(search=pokemon&search=trading&search=card&active=true&inStorePickup=true)" +
            $"?format=json&show=sku,name,salePrice,image,url,releaseDate,inStorePickup&sort=releaseDate.desc" +
            $"&pageSize={options.MaxProductsPerRefresh}&apiKey={Uri.EscapeDataString(options.BestBuyApiKey)}";
        var response = await http.GetFromJsonAsync<BestBuyProductsResponse>(path, cancellationToken)
            ?? throw new InvalidOperationException("Best Buy products response was empty.");

        return response.Products
            .Where(p => p.InStorePickup)
            .Where(p => LooksLikePokemonTcg(p.Name))
            .ToList();
    }

    private async Task<BestBuyAvailabilityResponse> GetAvailabilityAsync(
        string sku,
        string postalCode,
        CancellationToken cancellationToken)
    {
        var path = $"v1/products/{Uri.EscapeDataString(sku)}/stores.json?postalCode={Uri.EscapeDataString(postalCode)}&apiKey={Uri.EscapeDataString(options.BestBuyApiKey)}";
        using var response = await http.GetAsync(path, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return new BestBuyAvailabilityResponse();
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<BestBuyAvailabilityResponse>(cancellationToken: cancellationToken)
            ?? new BestBuyAvailabilityResponse();
    }

    private static bool LooksLikePokemonTcg(string name)
    {
        var text = name.ToLowerInvariant();
        if (!text.Contains("pok")) return false;
        return text.Contains("trading card") ||
               text.Contains("booster") ||
               text.Contains("elite trainer") ||
               text.Contains("collection") ||
               text.Contains("tin") ||
               text.Contains("card game");
    }

    private static string Category(string name)
    {
        var text = name.ToLowerInvariant();
        if (text.Contains("elite trainer") || text.Contains(" etb")) return "Elite Trainer Box";
        if (text.Contains("booster bundle")) return "Booster Bundle";
        if (text.Contains("booster box") || text.Contains("display box")) return "Booster Box";
        if (text.Contains("tin")) return "Tin";
        if (text.Contains("collection")) return "Collection";
        if (text.Contains("blister") || text.Contains("booster pack") || text.Contains("sleeved")) return "Booster Pack";
        return "Pokémon TCG";
    }

    private static double DistanceMiles(double lat1, double lon1, double lat2, double lon2)
    {
        const double earthMiles = 3958.7613;
        static double Rad(double degrees) => degrees * Math.PI / 180d;
        var dLat = Rad(lat2 - lat1);
        var dLon = Rad(lon2 - lon1);
        var a = Math.Pow(Math.Sin(dLat / 2), 2) +
                Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Pow(Math.Sin(dLon / 2), 2);
        return earthMiles * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private sealed class BestBuyStoresResponse
    {
        [JsonPropertyName("stores")]
        public List<BestBuyStore> Stores { get; init; } = [];
    }

    private sealed class BestBuyStore
    {
        [JsonPropertyName("storeId")]
        public string StoreId { get; init; } = "";
        [JsonPropertyName("name")]
        public string Name { get; init; } = "";
        [JsonPropertyName("address")]
        public string Address { get; init; } = "";
        [JsonPropertyName("city")]
        public string City { get; init; } = "";
        [JsonPropertyName("region")]
        public string Region { get; init; } = "";
        [JsonPropertyName("postalCode")]
        public string PostalCode { get; init; } = "";
        [JsonPropertyName("lat")]
        public double Lat { get; init; }
        [JsonPropertyName("lng")]
        public double Lng { get; init; }
    }

    private sealed class BestBuyProductsResponse
    {
        [JsonPropertyName("products")]
        public List<BestBuyProduct> Products { get; init; } = [];
    }

    private sealed class BestBuyProduct
    {
        [JsonPropertyName("sku")]
        public string Sku { get; init; } = "";
        [JsonPropertyName("name")]
        public string Name { get; init; } = "";
        [JsonPropertyName("salePrice")]
        public decimal? SalePrice { get; init; }
        [JsonPropertyName("image")]
        public string? Image { get; init; }
        [JsonPropertyName("url")]
        public string? Url { get; init; }
        [JsonPropertyName("inStorePickup")]
        public bool InStorePickup { get; init; }
    }

    private sealed class BestBuyAvailabilityResponse
    {
        [JsonPropertyName("stores")]
        public List<BestBuyAvailabilityStore> Stores { get; init; } = [];
    }

    private sealed class BestBuyAvailabilityStore
    {
        [JsonPropertyName("storeID")]
        public string StoreId { get; init; } = "";
        [JsonPropertyName("lowStock")]
        public bool LowStock { get; init; }
    }
}
