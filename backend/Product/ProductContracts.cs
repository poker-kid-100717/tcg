using PokemonTCG.API.Data;
using PokemonTCG.API.Market;
using PokemonTCG.API.Market.Intelligence;

namespace PokemonTCG.API.Product;

/// <summary>A signed-in user's entitlements, derived only from verified (webhook-confirmed) subscription state.</summary>
public record AccountView(
    Guid Id,
    bool BillingConfigured,
    bool StoreFinderBillingConfigured,
    bool IsPro,
    bool HasStoreFinder,
    string Plan,
    string? SubscriptionStatus,
    string Mode)
{
    public DateTimeOffset? CurrentPeriodEnd { get; init; }
    public bool CancelAtPeriodEnd { get; init; }
    /// <summary>Stripe reports the latest payment failed; access continues while Stripe retries.</summary>
    public bool PaymentIssue { get; init; }
    /// <summary>A Stripe customer exists, so the customer portal can open.</summary>
    public bool CanManageBilling { get; init; }
}

/// <summary>A plan as the pricing page shows it. Amounts come from configuration, billing from the Stripe price IDs.</summary>
public record PlanView(string Id, string Name, decimal Price, string Currency, string Interval, bool Available);

public record SubscriptionView(
    string Plan,
    string? Status,
    bool IsPro,
    DateTimeOffset? CurrentPeriodEnd,
    bool CancelAtPeriodEnd,
    bool PaymentIssue,
    bool CanManageBilling);

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
    DateTimeOffset CreatedAt)
{
    public bool AlertSleeper { get; init; }
    public bool AlertTrendingDown { get; init; }
    public bool AlertUnusualMove { get; init; }
}

public record CreateWatchlistRequest(
    string CardId,
    string Variant,
    string CardName,
    string SetName,
    string? ImageUrl,
    decimal? TargetBelow,
    decimal? TargetAbove,
    decimal? MovePercent,
    bool AlertSleeper = false,
    bool AlertTrendingDown = false,
    bool AlertUnusualMove = false)
{
    public bool HasAlerts => TargetBelow is not null || TargetAbove is not null || MovePercent is not null || AlertSleeper || AlertTrendingDown || AlertUnusualMove;
}

public record UpdateWatchlistRequest(
    decimal? TargetBelow,
    decimal? TargetAbove,
    decimal? MovePercent,
    bool Enabled,
    bool AlertSleeper = false,
    bool AlertTrendingDown = false,
    bool AlertUnusualMove = false)
{
    public bool HasAlerts => TargetBelow is not null || TargetAbove is not null || MovePercent is not null || AlertSleeper || AlertTrendingDown || AlertUnusualMove;
}

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
    int UnreadAlerts)
{
    /// <summary>The watched printings that moved most over 7 days.</summary>
    public IReadOnlyList<WatchlistMoverView> Movers { get; init; } = [];
    /// <summary>Today's signals on watched printings (Pro; empty and <see cref="SignalsLocked"/> otherwise).</summary>
    public IReadOnlyList<SignalItemView> WatchedSignals { get; init; } = [];
    public bool SignalsLocked { get; init; }
    /// <summary>Price-target alerts in the last 7 days.</summary>
    public int TargetsHit7Days { get; init; }
    public MarketSummaryView? Market { get; init; }
}

public record SoldCompView(
    string Id,
    string Source,
    string? Title,
    decimal Price,
    DateOnly SoldAt,
    string? Url);

/// <summary>
/// The Pro "True Market" view of one printing. Market reference prices (a provider's computed prices) and verified sold
/// comps (individual completed sales) are kept apart, every value names its source, and anything the data can't
/// support is null with a reason rather than estimated.
/// </summary>
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
    int? ConfidenceScore,
    string ConfidenceBand,
    bool IsStale,
    string Liquidity,
    string LiquidityReason,
    int? SoldComps30Days,
    decimal? MedianSold30Days,
    decimal? LastSoldPrice,
    DateOnly? LastSoldAt,
    IReadOnlyList<SoldCompView> RecentSoldComps,
    IReadOnlyList<string> Reasons)
{
    /// <summary>One deterministic sentence, e.g. "Medium confidence — current pricing is recent, but …".</summary>
    public string ConfidenceExplanation { get; init; } = "";
    public IReadOnlyList<ConfidenceFactor> ConfidenceFactors { get; init; } = [];
    public MarketProvider ReferenceProvider { get; init; }
    public DataFreshness? Freshness { get; init; }
    public string VolatilityLevel { get; init; } = "Unknown";
    public decimal? Change90Percent { get; init; }
    public decimal? BelowHigh30Percent { get; init; }
    public decimal? LowVsMarketPercent { get; init; }
    public decimal? High30 { get; init; }
    public decimal? Low30 { get; init; }
    public VerifiedCompsView VerifiedComps { get; init; } = VerifiedCompsView.None("No sold-comp source is connected.");
    public PokemonTCG.API.Market.Intelligence.Liquidity? LiquidityDetail { get; init; }
    public IReadOnlyList<SignalResult> Signals { get; init; } = [];
    public IReadOnlyList<string> WhyMoving { get; init; } = [];
    public IReadOnlyList<ReferencePoint> History { get; init; } = [];
}

/// <summary>Individual completed sales, separate from the market reference. Never populated from reference prices.</summary>
public record VerifiedCompsView(bool Available, string? Source, string Explanation, int? Sales30, decimal? MedianSold30, decimal? LastSalePrice, DateOnly? LastSaleAt, double? MedianDaysBetweenSales)
{
    public static VerifiedCompsView None(string why) => new(false, null, why, null, null, null, null, null);
}

public record ReferencePoint(DateOnly Date, decimal Market, decimal? Low);

public record CheckoutRequest(string Plan);
public record BillingLink(string Url);
