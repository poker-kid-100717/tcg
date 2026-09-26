using Microsoft.EntityFrameworkCore;
using PokemonTCG.API.Data;
using PokemonTCG.API.Pricing;

namespace PokemonTCG.API.Product;

public sealed class MarketIntelligenceService(
    AppDbContext db,
    ScrydexClient scrydex,
    TimeProvider clock)
{
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
        if (string.IsNullOrWhiteSpace(variant)) return null;

        var since = latestDate.Value.AddDays(-90);
        var localRows = await db.PriceSnapshots.AsNoTracking()
            .Where(s => s.CardId == cardId && s.Variant == variant && s.Date >= since && s.Market != null)
            .OrderBy(s => s.Date)
            .Select(s => new IntelligencePoint(s.Date, s.Market!.Value, s.Low, s.Mid, s.High))
            .ToListAsync(cancellationToken);
        if (localRows.Count == 0) return null;

        var provider = await scrydex.GetMarketDataAsync(cardId, variant, 90, cancellationToken);
        var providerRows = provider?.History
            .Where(p => p.Market > 0)
            .Select(p => new IntelligencePoint(p.Date, p.Market, p.Low, null, null))
            .OrderBy(p => p.Date)
            .ToList() ?? [];

        var rows = providerRows.Count >= 2 ? providerRows : localRows;
        var latest = rows[^1];

        // Local TCGplayer snapshots may include mid/high values that Scrydex price-history
        // intentionally does not. Only use them when the selected market reference is local,
        // avoiding a misleading mixed-source spread.
        var usingScrydex = providerRows.Count >= 2;
        var low = latest.Low;
        var mid = usingScrydex ? null : latest.Mid;
        var high = usingScrydex ? null : latest.High;

        var change7 = ChangeFrom(rows, latest.Date.AddDays(-7), latest.Market);
        var change30 = ChangeFrom(rows, latest.Date.AddDays(-30), latest.Market);
        var volatility = Volatility(rows);
        var spread = !usingScrydex && low is { } lowValue && high is { } highValue && latest.Market > 0
            ? Math.Round((highValue - lowValue) / latest.Market * 100m, 1)
            : null;

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var rawComps = provider?.SoldComps
            .Where(c => c.IsRaw && c.Currency.Equals("USD", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(c => c.SoldAt)
            .ToList() ?? [];
        var rawComps30 = rawComps.Where(c => c.SoldAt >= today.AddDays(-30)).ToList();
        var medianSold30 = Median(rawComps30.Select(c => c.Price));
        var lastComp = rawComps.FirstOrDefault();

        var ageDays = Math.Max(0, today.DayNumber - latest.Date.DayNumber);
        var isStale = ageDays > 2;

        var score = 10;
        score += Math.Min(25, rows.Count);
        score += ageDays switch { <= 1 => 20, <= 3 => 12, <= 7 => 5, _ => 0 };

        if (rows.Count >= 3 && volatility is { } vol)
        {
            score += vol switch { <= 3m => 20, <= 8m => 15, <= 15m => 9, <= 25m => 4, _ => 0 };
        }

        if (usingScrydex) score += 10;

        if (rawComps30.Count > 0)
        {
            score += rawComps30.Count switch { >= 20 => 15, >= 10 => 12, >= 5 => 9, >= 2 => 5, _ => 2 };
            if (medianSold30 is { } median && latest.Market > 0)
            {
                var disagreement = Math.Abs(median / latest.Market - 1m) * 100m;
                score += disagreement switch { <= 5m => 10, <= 10m => 7, <= 20m => 3, _ => 0 };
            }
        }
        else if (spread is { } spreadValue)
        {
            score += spreadValue switch { <= 15m => 8, <= 30m => 5, <= 60m => 2, _ => 0 };
        }

        score = Math.Clamp(score, 0, 100);
        var band = score switch
        {
            >= 80 => "High",
            >= 60 => "Medium",
            >= 35 => "Low",
            _ => "Insufficient data",
        };

        var (liquidity, liquidityReason) = Liquidity(rawComps30.Count, scrydex.IsConfigured);

        var reasons = new List<string>
        {
            $"{rows.Count} daily market observations are available for this printing.",
            ageDays <= 1 ? "The latest market observation is current." : $"The latest market observation is {ageDays} days old.",
        };
        if (usingScrydex)
            reasons.Add("Scrydex is supplying the primary raw Near Mint market history for this view.");
        if (rawComps30.Count > 0)
            reasons.Add($"{rawComps30.Count} raw sold listings were observed in the last 30 days; the median was {medianSold30:C}.");
        else if (scrydex.IsConfigured)
            reasons.Add("No matching raw sold listings were returned for the last 30 days.");
        if (volatility is { } v)
            reasons.Add($"Observed day-to-day market volatility is {v:0.0}% over the available window.");
        if (spread is { } s)
            reasons.Add($"The current local low-to-high listing spread is {s:0.0}% of market price.");

        return new MarketIntelligenceView(
            cardId,
            variant,
            Prices.VariantLabel(variant),
            usingScrydex
                ? "Scrydex raw NM pricing and historical sold listings"
                : "TCGplayer pricing via Pokémon TCG API snapshots",
            latest.Date,
            latest.Market,
            low,
            mid,
            high,
            change7,
            change30,
            volatility,
            spread,
            rows.Count,
            score,
            band,
            isStale,
            liquidity,
            liquidityReason,
            scrydex.IsConfigured ? rawComps30.Count : null,
            medianSold30,
            lastComp?.Price,
            lastComp?.SoldAt,
            rawComps.Take(5)
                .Select(c => new SoldCompView(c.Id, c.Source, c.Title, c.Price, c.SoldAt, c.Url))
                .ToList(),
            reasons);
    }

    private static decimal? ChangeFrom(IReadOnlyList<IntelligencePoint> rows, DateOnly target, decimal current)
    {
        var baseline = rows.LastOrDefault(r => r.Date <= target);
        return baseline is null || baseline.Market <= 0
            ? null
            : Math.Round((current / baseline.Market - 1m) * 100m, 1);
    }

    private static decimal? Volatility(IReadOnlyList<IntelligencePoint> rows)
    {
        if (rows.Count < 3) return null;
        var returns = new List<double>();
        for (var i = 1; i < rows.Count; i++)
        {
            if (rows[i - 1].Market <= 0) continue;
            returns.Add((double)(rows[i].Market / rows[i - 1].Market - 1m) * 100d);
        }
        if (returns.Count < 2) return null;
        var average = returns.Average();
        var variance = returns.Sum(x => Math.Pow(x - average, 2)) / (returns.Count - 1);
        return Math.Round((decimal)Math.Sqrt(variance), 1);
    }

    private static decimal? Median(IEnumerable<decimal> values)
    {
        var sorted = values.Order().ToArray();
        if (sorted.Length == 0) return null;
        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 1
            ? sorted[middle]
            : Math.Round((sorted[middle - 1] + sorted[middle]) / 2m, 2);
    }

    private static (string Status, string Reason) Liquidity(int sold30, bool providerConfigured)
    {
        if (!providerConfigured)
            return ("Unknown", "Sold-listing data is not configured, so liquidity is intentionally not estimated.");

        return sold30 switch
        {
            >= 30 => ("Very active", $"{sold30} raw sold listings were observed in the last 30 days."),
            >= 10 => ("Active", $"{sold30} raw sold listings were observed in the last 30 days."),
            >= 3 => ("Moderate", $"{sold30} raw sold listings were observed in the last 30 days."),
            >= 1 => ("Thin", $"Only {sold30} raw sold listing{(sold30 == 1 ? "" : "s")} was observed in the last 30 days."),
            _ => ("Thin", "No matching raw sold listings were observed in the last 30 days."),
        };
    }

    private sealed record IntelligencePoint(DateOnly Date, decimal Market, decimal? Low, decimal? Mid, decimal? High);
}
