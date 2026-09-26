namespace TcgSignal.Inventory.Domain;

public sealed record InventoryQuery(double Latitude, double Longitude, int RadiusMiles);

public sealed record InventoryListing(
    string Retailer,
    string StoreId,
    string StoreName,
    string Address,
    string City,
    string Region,
    string PostalCode,
    double Latitude,
    double Longitude,
    double DistanceMiles,
    string ProductSku,
    string ProductName,
    string Category,
    string? ImageUrl,
    string? ProductUrl,
    decimal? Price,
    bool LowStock,
    string Availability,
    int ConfidenceScore,
    DateTimeOffset ObservedAt,
    string Source,
    string Evidence);

public sealed record ProviderCoverage(
    string Retailer,
    string Status,
    string Detail,
    DateTimeOffset? CheckedAt = null);

public sealed record ProviderInventoryResult(
    IReadOnlyList<InventoryListing> Listings,
    ProviderCoverage Coverage);

public sealed record NearbyInventoryResponse(
    DateTimeOffset CheckedAt,
    int RadiusMiles,
    IReadOnlyList<InventoryListing> Listings,
    IReadOnlyList<ProviderCoverage> Providers,
    string AccuracyPolicy);
