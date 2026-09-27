namespace PokemonTCG.API.Product;

public record AccountView(
    Guid Id,
    bool BillingConfigured,
    bool StoreFinderBillingConfigured,
    bool IsPro,
    bool HasStoreFinder,
    string Plan,
    string? SubscriptionStatus,
    string Mode);

public record WatchlistItemView(
    long Id,
    string CardId,
    string Variant,
    string VariantLabel,
    string CardName,
    string SetName,
    string? ImageUrl,
    decimal? BaselinePrice,
    decimal? CurrentPrice,
    decimal? TargetBelow,
    decimal? TargetAbove,
    decimal? MovePercent,
    bool Enabled,
    DateTimeOffset CreatedAt);

public record CreateWatchlistRequest(
    string CardId,
    string Variant,
    string CardName,
    string SetName,
    string? ImageUrl,
    decimal? TargetBelow,
    decimal? TargetAbove,
    decimal? MovePercent);

public record UpdateWatchlistRequest(
    decimal? TargetBelow,
    decimal? TargetAbove,
    decimal? MovePercent,
    bool Enabled);

public record AlertView(
    long Id,
    long WatchlistItemId,
    string Kind,
    string Message,
    decimal? CurrentValue,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt);

public record DashboardView(
    AccountView Account,
    IReadOnlyList<WatchlistItemView> Watchlist,
    IReadOnlyList<AlertView> Alerts,
    int UnreadAlerts);

public record SoldCompView(
    string Id,
    string Source,
    string? Title,
    decimal Price,
    DateOnly SoldAt,
    string? Url);

public record MarketIntelligenceView(
    string CardId,
    string Variant,
    string VariantLabel,
    string Source,
    DateOnly AsOf,
    decimal? Market,
    decimal? Low,
    decimal? Mid,
    decimal? High,
    decimal? Change7Percent,
    decimal? Change30Percent,
    decimal? VolatilityPercent,
    decimal? SpreadPercent,
    int ObservationDays,
    int ConfidenceScore,
    string ConfidenceBand,
    bool IsStale,
    string Liquidity,
    string LiquidityReason,
    int? SoldComps30Days,
    decimal? MedianSold30Days,
    decimal? LastSoldPrice,
    DateOnly? LastSoldAt,
    IReadOnlyList<SoldCompView> RecentSoldComps,
    IReadOnlyList<string> Reasons);

public record CheckoutRequest(string Plan);
public record BillingLink(string Url);
