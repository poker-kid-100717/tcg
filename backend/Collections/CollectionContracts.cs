using PokemonTCG.API.Data;

namespace PokemonTCG.API.Collections;

/// <summary>How far to trust a card's value: High, Medium or Low, with the reasons.</summary>
public record PriceConfidence(string Level, IReadOnlyList<string> Reasons, int? DaysSinceChange);

public record CollectionEntry(
    Guid Id,
    string CardId,
    string Name,
    string Number,
    string SetId,
    string SetName,
    string? ImageUrl,
    string? Rarity,
    string Variant,
    string VariantLabel,
    CardCondition Condition,
    int Quantity,
    decimal? CostEach,
    DateOnly? AcquiredOn,
    string? Notes,
    /// <summary>TCGplayer market price for the printing (Near Mint).</summary>
    decimal? MarketEach,
    /// <summary>Estimated value in this copy's condition.</summary>
    decimal? ValueEach,
    /// <summary>What one copy would net if sold on TCGplayer, after fees.</summary>
    decimal? NetEach,
    decimal? Total,
    decimal? GainPercent,
    PriceConfidence Confidence,
    string? TcgplayerUrl,
    DateTimeOffset AddedAt);

public record CollectionSummary(
    int Cards,
    int Unique,
    decimal MarketValue,
    decimal ConditionValue,
    decimal NetIfSold,
    decimal? CostBasis,
    decimal? Gain,
    decimal? GainPercent,
    decimal HighConfidenceShare,
    decimal? Change30Percent,
    DateOnly? PricesAsOf);

public record ValuePoint(DateOnly Date, decimal Value);

public record SetProgress(
    string SetId,
    string SetName,
    string? SymbolUrl,
    DateOnly? ReleaseDate,
    int Owned,
    int PrintedTotal,
    int OwnedAll,
    int Total,
    decimal Completion,
    decimal? CostToComplete,
    int UnpricedMissing);

/// <summary>A heads-up about a card the collector owns: ConsiderSelling, Watch or Hold.</summary>
public record CollectionSignal(
    string CardId,
    string Name,
    string SetName,
    string? ImageUrl,
    string Variant,
    string VariantLabel,
    string Kind,
    IReadOnlyList<string> Reasons,
    decimal? ValueEach,
    int Quantity);

public record SellingFees(decimal CommissionRate, decimal PaymentRate, decimal PerSaleFee);

public record CollectionView(
    CollectionSummary Summary,
    IReadOnlyList<CollectionEntry> Items,
    IReadOnlyList<ValuePoint> History,
    IReadOnlyList<SetProgress> Sets,
    IReadOnlyList<CollectionSignal> Signals,
    SellingFees Fees,
    IReadOnlyDictionary<CardCondition, decimal> ConditionFactors);

public record OwnedCard(string CardId, int Quantity, IReadOnlyList<string> Variants);

public record MissingCard(
    string CardId,
    string Name,
    string Number,
    string? ImageUrl,
    bool Secret,
    string? Variant,
    decimal? Price,
    decimal? Low,
    /// <summary>"Good time", "Wait", or null when there's no signal either way.</summary>
    string? Timing,
    string? TimingReason,
    string? TcgplayerUrl);

public record SetChecklist(
    string SetId,
    int PrintedTotal,
    int Total,
    IReadOnlyList<OwnedCard> Owned,
    IReadOnlyList<MissingCard> Missing,
    decimal CostToCompleteBase,
    decimal CostToCompleteAll);

public record WishlistEntry(
    Guid Id,
    string CardId,
    string Name,
    string Number,
    string SetId,
    string SetName,
    string? ImageUrl,
    string? Variant,
    string VariantLabel,
    decimal? TargetPrice,
    decimal? SuggestedTarget,
    decimal? Market,
    decimal? Low,
    bool AtOrBelowTarget,
    string? TcgplayerUrl,
    DateTimeOffset AddedAt);

public record CardOwnership(IReadOnlyList<CollectionEntry> Items, WishlistEntry? Wish);

public record AddItemRequest(string CardId, string Variant, CardCondition Condition = CardCondition.NearMint, int Quantity = 1, decimal? CostEach = null, DateOnly? AcquiredOn = null, string? Notes = null);
public record UpdateItemRequest(int? Quantity = null, CardCondition? Condition = null, decimal? CostEach = null, bool ClearCost = false, DateOnly? AcquiredOn = null, string? Notes = null);
public record WishRequest(string CardId, string? Variant = null, decimal? TargetPrice = null);
