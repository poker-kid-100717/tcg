using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PokemonTCG.API.Data;
using PokemonTCG.API.Market;
using PokemonTCG.API.Market.Intelligence;
using PokemonTCG.API.Pricing;

namespace PokemonTCG.API.Product;

/// <summary>A selling-cost estimate: a percentage of the sale plus a fixed amount per order. Configuration, not code.</summary>
public sealed class FeePreset
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal PercentFee { get; set; }
    public decimal FixedFee { get; set; }
    public string Note { get; set; } = "";
}

public sealed class DealOptions
{
    public const string SectionName = "Deals";

    /// <summary>
    /// Estimates for common exits. Marketplace fees change and vary by seller and category, so these are editable
    /// defaults (Deals:Presets:n:… in configuration) and the UI says to check the platform's current fees.
    /// </summary>
    public List<FeePreset> Presets { get; set; } =
    [
        new() { Id = "tcgplayer", Name = "TCGplayer marketplace (estimate)", PercentFee = 12.75m, FixedFee = 0.30m, Note = "Commission plus payment processing, approximate." },
        new() { Id = "ebay", Name = "eBay (estimate)", PercentFee = 13.25m, FixedFee = 0.40m, Note = "Final value fee for trading cards plus per-order fee, approximate." },
        new() { Id = "local", Name = "Local cash sale", PercentFee = 0m, FixedFee = 0m, Note = "No platform fees." },
    ];
}

public record DealRequest(
    string CardId,
    string Variant,
    decimal AskingPrice,
    decimal? InboundShipping = null,
    decimal? Tax = null,
    decimal? OutboundShipping = null,
    string? PresetId = null,
    decimal? CustomPercentFee = null,
    decimal? CustomFixedFee = null,
    decimal? ExpectedSalePrice = null);

public record DealMath(
    decimal Acquisition,
    decimal ExpectedSale,
    decimal SellingFees,
    decimal OutboundShipping,
    decimal Proceeds,
    decimal Net,
    decimal? NetMarginPercent,
    decimal? BreakEvenSalePrice);

public record DealAnalysisView(
    string CardId,
    string CardName,
    string Variant,
    string VariantLabel,
    decimal? MarketReference,
    string ReferenceSource,
    DataFreshness Freshness,
    int? ConfidenceScore,
    string ConfidenceBand,
    decimal AskingPrice,
    decimal? AskVsMarketPercent,
    string Position,
    FeePreset Fees,
    DealMath Math,
    IReadOnlyList<string> Warnings,
    string Summary);

/// <summary>
/// Deal math against the market reference. It reports what the entered costs imply; it never predicts a sale price
/// or promises a profit.
/// </summary>
public sealed class DealAnalyzerService(AppDbContext db, DealOptions options, FreshnessOptions freshness, TimeProvider clock)
{
    public IReadOnlyList<FeePreset> Presets => options.Presets;

    public static DealMath Compute(decimal asking, decimal inbound, decimal tax, decimal outbound, decimal expectedSale, decimal percentFee, decimal fixedFee)
    {
        var acquisition = asking + inbound + tax;
        var fees = expectedSale <= 0 ? 0 : Round(expectedSale * percentFee / 100m + fixedFee);
        var proceeds = expectedSale - fees - outbound;
        var net = proceeds - acquisition;
        decimal? margin = acquisition > 0 ? Round(net / acquisition * 100m) : null;
        // Sale price S where S - S·p - fixed - outbound = acquisition.
        decimal? breakEven = percentFee >= 100 ? null : Round((acquisition + outbound + fixedFee) / (1 - percentFee / 100m));
        return new DealMath(Round(acquisition), Round(expectedSale), fees, Round(outbound), Round(proceeds), Round(net), margin, breakEven);
    }

    public async Task<DealAnalysisView?> AnalyzeAsync(DealRequest request, CancellationToken ct)
    {
        var card = await db.Cards.AsNoTracking().Where(c => c.Id == request.CardId).Select(c => new { c.Id, c.Name }).FirstOrDefaultAsync(ct);
        if (card is null) return null;

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var since = today.AddDays(-120);
        var history = await db.PriceSnapshots.AsNoTracking()
            .Where(s => s.CardId == request.CardId && s.Variant == request.Variant && s.Date >= since && s.Market != null)
            .OrderBy(s => s.Date)
            .Select(s => new { s.Date, s.Market, s.Low, s.Mid, s.High, s.Provider })
            .ToListAsync(ct);
        var series = new PriceSeries(history.Select(h => new PricePointDay(h.Date, h.Market, h.Low, h.Mid, h.High)));
        var metrics = MarketMetrics.From(series, today);
        var confidence = MarketConfidence.Compute(metrics);
        var market = metrics.Market;

        var preset = request.PresetId == "custom" || request.PresetId is null && request.CustomPercentFee is not null
            ? new FeePreset
            {
                Id = "custom", Name = "Custom", PercentFee = Math.Clamp(request.CustomPercentFee ?? 0, 0, 100),
                FixedFee = Math.Max(0, request.CustomFixedFee ?? 0), Note = "Your own fee estimate.",
            }
            : options.Presets.FirstOrDefault(p => p.Id == request.PresetId) ?? options.Presets.First();

        var expected = request.ExpectedSalePrice is { } e ? Math.Max(0, e) : market ?? 0;
        var math = Compute(Math.Max(0, request.AskingPrice), Math.Max(0, request.InboundShipping ?? 0), Math.Max(0, request.Tax ?? 0),
            Math.Max(0, request.OutboundShipping ?? 0), expected, preset.PercentFee, preset.FixedFee);

        decimal? askVsMarket = market is > 0 && request.AskingPrice > 0 ? Round((request.AskingPrice / market.Value - 1) * 100) : null;
        var position = askVsMarket switch
        {
            null => "No market reference",
            <= -10 => "Below market reference",
            >= 10 => "Above market reference",
            _ => "Near market reference",
        };

        var fresh = freshness.Of(metrics.AsOf, today);
        var warnings = new List<string>();
        if (market is null) warnings.Add("There's no market reference for this printing, so the sale price is whatever you entered.");
        if (fresh.State is FreshnessState.Stale) warnings.Add($"The market reference is {fresh.Label.ToLowerInvariant()}; it may not reflect today's market.");
        if (confidence.Band is ConfidenceBand.Low or ConfidenceBand.InsufficientData) warnings.Add(confidence.Explanation);
        if (request.ExpectedSalePrice is null && market is not null)
            warnings.Add("The expected sale uses the market reference, which is not a sold price. Cards often sell below it.");

        var band = confidence.Band switch
        {
            ConfidenceBand.High => "High", ConfidenceBand.Medium => "Medium", ConfidenceBand.Low => "Low", _ => "Insufficient data",
        };
        var source = history.Count > 0 && history[^1].Provider == MarketProvider.TCGPlayer ? "TCGplayer API" : "Pokémon TCG API (TCGplayer market prices)";
        var label = Prices.VariantLabel(request.Variant);
        var summary = string.Join(" ", new[]
        {
            $"{card.Name} ({label}) at {Money(request.AskingPrice)}",
            market is null ? "with no market reference." : $"vs a {Money(market.Value)} market reference ({position.ToLowerInvariant()}, {band.ToLowerInvariant()} confidence).",
            $"All-in cost {Money(math.Acquisition)}; selling at {Money(math.ExpectedSale)} with {preset.Name} fees nets {Money(math.Net)}",
            math.BreakEvenSalePrice is { } be ? $"(break-even {Money(be)})." : ".",
            "Estimate only, not financial advice. — TCG Signal",
        });

        return new DealAnalysisView(card.Id, card.Name, request.Variant, label, market, source, fresh, confidence.Score, band,
            request.AskingPrice, askVsMarket, position, preset, math, warnings, summary);
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
    private static string Money(decimal value) => value.ToString("C2", CultureInfo.GetCultureInfo("en-US"));
}
