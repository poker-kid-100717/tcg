namespace PokemonTCG.API.Pricing;

// Response shapes for the price guide API.

public record SetSummary(
    string Id,
    string Name,
    string Series,
    DateOnly? ReleaseDate,
    int PrintedTotal,
    int Total,
    string? LogoUrl,
    string? SymbolUrl)
{
    public static SetSummary From(TcgSet set) =>
        new(set.Id, set.Name, set.Series, set.Released, set.PrintedTotal, set.Total, set.Images?.Logo, set.Images?.Symbol);
}

public record CardSummary(
    string Id,
    string Name,
    string Number,
    string? Rarity,
    string? ImageUrl,
    string SetId,
    string SetName,
    decimal? MarketPrice,
    string? PriceVariant,
    string? TcgplayerUrl)
{
    public static CardSummary From(TcgCard card) => new(
        card.Id, card.Name, card.Number, card.Rarity, card.Images?.Small, card.Set.Id, card.Set.Name,
        Prices.HeadlinePrice(card.Tcgplayer?.Prices), Prices.HeadlineVariant(card.Tcgplayer?.Prices), card.Tcgplayer?.Url);
}

public record SetStats(int CardCount, int PricedCount, decimal TotalMarketValue, CardSummary? MostValuable);

public record SetDetail(SetSummary Set, SetStats Stats, IReadOnlyList<CardSummary> Cards);

public record VariantPrice(string Variant, string Label, decimal? Low, decimal? Mid, decimal? High, decimal? Market);

public record PricePoint(DateOnly Date, string Variant, decimal? Market);

public record CardDetail(
    string Id,
    string Name,
    string? Supertype,
    IReadOnlyList<string> Subtypes,
    string? Hp,
    IReadOnlyList<string> Types,
    string Number,
    string? Artist,
    string? Rarity,
    string? FlavorText,
    string? ImageUrl,
    string? LargeImageUrl,
    SetSummary Set,
    IReadOnlyList<VariantPrice> Prices,
    DateOnly? PricesUpdated,
    string? TcgplayerUrl,
    IReadOnlyList<PricePoint> History);

public record SearchResults(IReadOnlyList<CardSummary> Cards, int Page, int PageSize, int TotalCount);

public record PriceMove(
    string CardId,
    string Name,
    string Number,
    string SetId,
    string SetName,
    string? ImageUrl,
    string? TcgplayerUrl,
    string Variant,
    string VariantLabel,
    decimal From,
    decimal To,
    decimal ChangePercent);

public record MarketMovers(DateOnly? From, DateOnly? To, IReadOnlyList<PriceMove> Gainers, IReadOnlyList<PriceMove> Losers);

public record ValuableCard(
    string CardId,
    string Name,
    string Number,
    string SetId,
    string SetName,
    string? ImageUrl,
    string? TcgplayerUrl,
    string VariantLabel,
    decimal Market);

public record TrendPoint(DateOnly Date, decimal Market);

/// <summary>A card whose price has fallen steadily across the window (see PriceGuideService.GetDownTrendingAsync).</summary>
public record TrendingCard(
    string CardId,
    string Name,
    string Number,
    string SetId,
    string SetName,
    string? ImageUrl,
    string? TcgplayerUrl,
    string Variant,
    string VariantLabel,
    decimal From,
    decimal To,
    decimal ChangePercent,
    double Fit,
    IReadOnlyList<TrendPoint> Points);

public record DownTrend(DateOnly? From, DateOnly? To, int Days, int DaysOfHistory, IReadOnlyList<TrendingCard> Cards);

/// <summary>
/// A card whose cheapest listing is well above what it has been selling for
/// (see PriceGuideService.GetSleepersAsync).
/// </summary>
public record SleeperCard(
    string CardId,
    string Name,
    string Number,
    string SetId,
    string SetName,
    string? ImageUrl,
    string? TcgplayerUrl,
    string Variant,
    string VariantLabel,
    decimal Market,
    decimal Low,
    decimal? Mid,
    decimal ListingGapPercent,
    decimal? Change30Percent);

public record Sleepers(DateOnly? AsOf, IReadOnlyList<SleeperCard> Cards);
