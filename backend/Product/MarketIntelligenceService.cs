using Microsoft.EntityFrameworkCore;
using PokemonTCG.API.Data;
using PokemonTCG.API.Pricing;

namespace PokemonTCG.API.Product;

public sealed class MarketIntelligenceService(AppDbContext db, TimeProvider clock)
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
        var rows = await db.PriceSnapshots.AsNoTracking()
            .Where(s => s.CardId == cardId && s.Variant == variant && s.Date >= since && s.Market != null)
            .OrderBy(s => s.Date)
            .Select(s => new IntelligencePoint(s.Date, s.Market!.Value, s.Low, s.Mid, s.High))
            .ToListAsync(cancellationToken);
        if (rows.Count == 0) return null;

        var latest = rows[^1];
        var change7 = ChangeFrom(rows, latest.Date.AddDays(-7), latest.Market);
        var change30 = ChangeFrom(rows, latest.Date.AddDays(-30), latest.Market);
        var volatility = Volatility(rows);
        var spread = latest.Low is { } low && latest.High is { } high && latest.Market > 0
            ? Math.Round((high - low) / latest.Market * 100m, 1)
            : null;

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var ageDays = Math.Max(0, today.DayNumber - latest.Date.DayNumber);
        var isStale = ageDays > 2;

        var score = 10;
        score += Math.Min(30, rows.Count);
        score += ageDays switch { <= 1 => 25, <= 3 => 15, <= 7 => 5, _ => 0 };

        if (rows.Count >= 3 && volatility is { } vol)
        {
            score += vol switch { <= 3m => 25, <= 8m => 18, <= 15m => 10, <= 25m => 5, _ => 0 };
        }

        if (spread is { } spreadValue)
        {
            score += spreadValue switch { <= 15m => 10, <= 30m => 6, <= 60m => 2, _ => 0 };
        }

        score = Math.Clamp(score, 0, 100);
        var band = score switch
        {
            >= 80 => "High",
            >= 60 => "Medium",
            >= 35 => "Low",
            _ => "Insufficient data",
        };

        var reasons = new List<string>
        {
            $"{rows.Count} daily market observations are available for this printing.",
            ageDays <= 1 ? "The latest observation is current." : $"The latest observation is {ageDays} days old.",
        };
        if (volatility is { } v)
            reasons.Add($"Observed day-to-day price volatility is {v:0.0}% over the available window.");
        if (spread is { } s)
            reasons.Add($"The current low-to-high listing spread is {s:0.0}% of market price.");

        return new MarketIntelligenceView(
            cardId,
            variant,
            Prices.VariantLabel(variant),
            "TCGplayer pricing via Pokémon TCG API",
            latest.Date,
            latest.Market,
            latest.Low,
            latest.Mid,
            latest.High,
            change7,
            change30,
            volatility,
            spread,
            rows.Count,
            score,
            band,
            isStale,
            "Unknown",
            "The current source exposes market/listing prices, not verified transaction-level sales. Liquidity is intentionally not estimated.",
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

    private sealed record IntelligencePoint(DateOnly Date, decimal Market, decimal? Low, decimal? Mid, decimal? High);
}
