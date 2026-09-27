namespace PokemonTCG.API.Product;

public record CreateMasterSetRequest(string SetId);

public record UpdateMasterSetItemRequest(
    string CardId,
    string Variant,
    int OwnedQuantity,
    string? Condition,
    decimal? AcquiredPrice);

public record MasterSetSummaryView(
    long Id,
    string SetId,
    string SetName,
    string SetSeries,
    string? LogoUrl,
    int UniqueCards,
    int RequiredPrintings,
    int OwnedPrintings,
    decimal CompletionPercent,
    decimal OwnedMarketValue,
    decimal MissingMarketCost);

public record MasterSetItemView(
    string CardId,
    string Variant,
    string VariantLabel,
    string CardName,
    string CardNumber,
    string? Rarity,
    string? ImageUrl,
    string? TcgplayerUrl,
    decimal? CurrentMarketPrice,
    decimal? Change30Percent,
    int OwnedQuantity,
    string? Condition,
    decimal? AcquiredPrice,
    string Signal,
    string SignalReason);

public record MasterSetDetailView(
    MasterSetSummaryView Summary,
    IReadOnlyList<MasterSetItemView> Items);

public record AdvisorCardView(
    string CardId,
    string CardName,
    string CardNumber,
    string Variant,
    string VariantLabel,
    decimal? MarketPrice,
    decimal? Change30Percent,
    string Signal,
    string SignalReason,
    string? TcgplayerUrl);

public record MasterSetAdvisorContextView(
    long MasterSetId,
    string SetName,
    decimal CompletionPercent,
    int MissingPrintings,
    decimal MissingMarketCost,
    IReadOnlyList<AdvisorCardView> PriorityCards,
    string DeterministicSummary);

public record MasterSetAiAdviceView(
    string Text,
    string Provider,
    string Model,
    bool GeneratedByAi,
    DateTimeOffset GeneratedAt);
