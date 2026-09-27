using Microsoft.EntityFrameworkCore;
using PokemonTCG.API.Data;
using PokemonTCG.API.Market;
using PokemonTCG.API.Market.Intelligence;
using PokemonTCG.API.Market.Providers;
using PokemonTCG.API.Pricing;

namespace PokemonTCG.API.Product;

/// <summary>
/// Builds the Pro Market Intelligence view for a printing from the recorded market reference prices (or Scrydex's
/// history when it's configured) plus verified sold comps, using the deterministic confidence, liquidity and signal
/// models in Market/Intelligence. Scrydex comps are stored in market_observations with provenance, so they're
/// deduplicated and kept apart by printing, grade and grading company.
/// </summary>
public sealed class MarketIntelligenceService(
    AppDbContext db,
    ScrydexClient scrydex,
    CompIngestionService comps,
    FreshnessOptions freshness,
    TimeProvider clock,
    ILogger<MarketIntelligenceService> logger)
{
    private const int HistoryDays = 120;

    public async Task<MarketIntelligenceView?> GetAsync(string cardId, string? requestedVariant, CancellationToken cancellationToken)
    {
        var latestDate = await db.PriceSnapshots.AsNoTracking()
            .Where(s => s.CardId == cardId && s.Market != null)
            .MaxAsync(s => (DateOnly?)s.Date, cancellationToken);
        if (latestDate is null) return null;

        var variant = requestedVariant?.Trim();
        if (string.IsNullOrWhiteSpace(variant))
        {
            variant = await db.PriceSnapshots.AsNoTracking()
                .Where(s => s.CardId == cardId && s.Date == latestDate && s.Market != null)
                .OrderByDescending(s => s.Market)
                .Select(s => s.Variant)
                .FirstOrDefaultAsync(cancellationToken);
        }
        if (string.IsNullOrWhiteSpace(variant) || variant.Length > 40) return null;

        var since = latestDate.Value.AddDays(-HistoryDays);
        var local = await db.PriceSnapshots.AsNoTracking()
            .Where(s => s.CardId == cardId && s.Variant == variant && s.Date >= since && s.Market != null)
            .OrderBy(s => s.Date)
            .Select(s => new { s.Date, s.Market, s.Low, s.Mid, s.High, s.Provider })
            .ToListAsync(cancellationToken);
        if (local.Count == 0) return null;

        var now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        // Scrydex, when configured, supplies the reference history and verified sold listings.
        ScrydexMarketData? provider = null;
        if (scrydex.IsConfigured)
        {
            provider = await scrydex.GetMarketDataAsync(cardId, variant, 90, cancellationToken);
            if (provider is not null)
            {
                try
                {
                    var request = new CompRequest(cardId, variant, null);
                    await comps.StoreAsync(MarketProvider.Scrydex, provider.SoldComps.Select(c => ScrydexCompProvider.ToRecord(request, c)).ToList(), cancellationToken);
                }
                catch (DbUpdateException ex)
                {
                    logger.LogWarning(ex, "Couldn't store Scrydex comps for {Card}", cardId);
                }
            }
        }

        var usingScrydex = provider is { History.Count: >= 2 };
        var series = usingScrydex
            ? new PriceSeries(provider!.History.Select(p => new PricePointDay(p.Date, p.Market, p.Low)))
            : new PriceSeries(local.Select(p => new PricePointDay(p.Date, p.Market, p.Low, p.Mid, p.High)));
        var referenceProvider = usingScrydex ? MarketProvider.Scrydex : local[^1].Provider;
        var referenceName = usingScrydex ? "Scrydex" : referenceProvider == MarketProvider.TCGPlayer ? "TCGplayer API" : "Pokémon TCG API (TCGplayer market prices)";
        var latestLocal = local[^1];
        var metrics = MarketMetrics.From(series, today);

        // Verified sales: raw, USD, this exact printing, from the stored comps.
        var compSource = scrydex.IsConfigured ? "Scrydex sold listings" : null;
        List<(DateTimeOffset SoldAt, decimal Price)>? sales = null;
        List<MarketObservation> recent = [];
        if (compSource is not null)
        {
            var from = now.AddDays(-90);
            recent = await db.MarketObservations.AsNoTracking()
                .Where(o => o.CardId == cardId && o.Variant == variant && o.Kind == ObservationKind.Sale && o.Provider == MarketProvider.Scrydex
                            && o.Grade == null && o.GradingCompany == null && o.Language == "en" && o.Currency == "USD" && o.ObservedAt >= from)
                .OrderByDescending(o => o.ObservedAt)
                .ToListAsync(cancellationToken);
            sales = recent.Select(o => (o.ObservedAt, o.Price)).ToList();
        }
        var liquidity = Liquidity.Compute(sales, now, referenceName, compSource);
        var sold30 = recent.Where(o => o.ObservedAt >= now.AddDays(-30)).Select(o => (double)o.Price).ToList();
        decimal? medianSold = Stats.Median(sold30) is { } m ? Math.Round((decimal)m, 2) : null;

        var quotes = medianSold is { } median && sold30.Count >= 3
            ? new List<MarketConfidence.SourceQuote> { new("Verified sales", median) }
            : [];
        var confidence = MarketConfidence.Compute(metrics, quotes, referenceName);
        var signals = Signals.Evaluate(series, metrics, confidence.Score);
        var fresh = freshness.Of(metrics.AsOf, today);

        var verified = compSource is null
            ? VerifiedCompsView.None("No sold-comp source is connected, so there are no verified sales for this card.")
            : new VerifiedCompsView(true, compSource,
                sold30.Count == 0 ? "No matching raw sales in the last 30 days." : $"{sold30.Count} raw sales in the last 30 days.",
                sold30.Count, medianSold, recent.FirstOrDefault()?.Price, recent.FirstOrDefault() is { } last ? DateOnly.FromDateTime(last.ObservedAt.UtcDateTime) : null,
                liquidity.MedianDaysBetweenSales);

        decimal? D(double? value, int digits = 1) => value is null ? null : Math.Round((decimal)value.Value, digits);
        var reasons = confidence.Factors.Select(f => f.Detail).ToList();

        return new MarketIntelligenceView(
            cardId,
            variant,
            Prices.VariantLabel(variant),
            usingScrydex ? "Scrydex market history and sold listings" : $"{referenceName} market reference prices (daily snapshots)",
            metrics.AsOf ?? latestLocal.Date,
            metrics.Market,
            metrics.Low,
            metrics.Mid,
            metrics.High,
            D(metrics.Change7),
            D(metrics.Change30),
            D(metrics.Volatility30),
            metrics.Low is { } low && metrics.High is { } high && metrics.Market is > 0 ? Math.Round((high - low) / metrics.Market.Value * 100m, 1) : null,
            metrics.Observations30,
            confidence.Score,
            confidence.Band switch { ConfidenceBand.High => "High", ConfidenceBand.Medium => "Medium", ConfidenceBand.Low => "Low", _ => "Insufficient data" },
            fresh.State is FreshnessState.Stale or FreshnessState.NoData,
            liquidity.Status == LiquidityStatus.VeryActive ? "Very active" : liquidity.Status.ToString(),
            liquidity.Explanation,
            liquidity.Sales30,
            medianSold,
            recent.FirstOrDefault()?.Price,
            recent.FirstOrDefault() is { } lastSale ? DateOnly.FromDateTime(lastSale.ObservedAt.UtcDateTime) : null,
            recent.Take(5).Select(o => new SoldCompView(o.ProviderReference ?? o.Id.ToString(), o.Provider.ToString(), null, o.Price,
                DateOnly.FromDateTime(o.ObservedAt.UtcDateTime), o.SourceUrl)).ToList(),
            reasons)
        {
            ConfidenceExplanation = confidence.Explanation,
            ConfidenceFactors = confidence.Factors,
            ReferenceProvider = referenceProvider,
            Freshness = fresh,
            VolatilityLevel = MarketConfidence.Describe(metrics.Volatility),
            Change90Percent = D(metrics.Change90),
            BelowHigh30Percent = D(metrics.BelowHigh30),
            LowVsMarketPercent = D(metrics.LowVsMarket),
            High30 = metrics.High30,
            Low30 = metrics.Low30,
            VerifiedComps = verified,
            LiquidityDetail = liquidity,
            Signals = signals,
            WhyMoving = WhyMoving.Explain(metrics, signals),
            History = series.Points.Select(p => new ReferencePoint(p.Date, p.Market!.Value, p.Low)).ToList(),
        };
    }
}
