using Microsoft.EntityFrameworkCore;
using PokemonTCG.API.Data;
using PokemonTCG.API.Market;
using PokemonTCG.API.Market.Intelligence;
using PokemonTCG.API.Pricing;

namespace PokemonTCG.API.Product;

public record SignalItemView(
    string CardId,
    string Variant,
    string VariantLabel,
    string CardName,
    string SetName,
    string? ImageUrl,
    decimal? Market,
    string Kind,
    string Name,
    double Value,
    string Unit,
    int LookbackDays,
    string Reason,
    int Confidence,
    string Source,
    DateOnly AsOf);

public record SignalCenterView(DataFreshness Freshness, IReadOnlyDictionary<string, int> Counts, IReadOnlyDictionary<string, string> Rules, IReadOnlyList<SignalItemView> Signals);

public record WatchlistMoverView(long WatchlistItemId, string CardId, string Variant, string CardName, decimal? Market, double? Change7Percent, double? Change30Percent);

public record MarketSummaryView(DataFreshness Freshness, int PrintingsPriced, int Up7, int Down7, IReadOnlyDictionary<string, int> SignalCounts);

/// <summary>
/// Reads the signals the post-snapshot refresh stored: the Pro signal center across the market, plus the dashboard's
/// watchlist movers, signals on watched cards and a market summary.
/// </summary>
public sealed class SignalCenterService(AppDbContext db, FreshnessOptions freshness, TimeProvider clock)
{
    private static readonly IReadOnlyDictionary<string, string> Names = new Dictionary<string, string>
    {
        [Signals.Momentum] = "Momentum",
        [Signals.Acceleration] = "Acceleration",
        [Signals.UnusualMove] = "Unusual move",
        [Signals.VolatilityExpansion] = "Volatility expansion",
        [Signals.New30DayHigh] = "New 30-day high",
        [Signals.New30DayLow] = "New 30-day low",
        [Signals.ThinSupply] = "Thin supply",
        [Signals.Sleeper] = "Sleeper",
        [Signals.SustainedDowntrend] = "Sustained downtrend",
    };

    private static string Unit(string kind) => kind switch
    {
        Signals.Acceleration => "pts",
        Signals.VolatilityExpansion => "×",
        _ => "%",
    };

    private DateOnly Today => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    private Task<DateOnly?> LatestSignalDayAsync(CancellationToken ct) =>
        db.MarketSignals.AsNoTracking().MaxAsync(s => (DateOnly?)s.AsOf, ct);

    public async Task<SignalCenterView> GetAsync(string? kind, int minConfidence, int limit, CancellationToken ct)
    {
        var asOf = await LatestSignalDayAsync(ct);
        if (asOf is null) return new SignalCenterView(freshness.Of(null, Today), new Dictionary<string, int>(), Signals.Rules, []);

        var day = db.MarketSignals.AsNoTracking().Where(s => s.AsOf == asOf);
        var counts = await day.GroupBy(s => s.Kind).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, ct);
        var filtered = day.Where(s => s.Confidence >= minConfidence);
        if (!string.IsNullOrWhiteSpace(kind)) filtered = filtered.Where(s => s.Kind == kind);
        var rows = await filtered.OrderByDescending(s => s.Confidence).ThenByDescending(s => Math.Abs(s.Value)).Take(limit).ToListAsync(ct);
        return new SignalCenterView(freshness.Of(asOf, Today), counts, Signals.Rules, await DescribeAsync(rows, ct));
    }

    /// <summary>Today's signals on the given printings (the user's watchlist).</summary>
    public async Task<IReadOnlyList<SignalItemView>> ForPrintingsAsync(IReadOnlyCollection<(string CardId, string Variant)> printings, CancellationToken ct)
    {
        if (printings.Count == 0 || await LatestSignalDayAsync(ct) is not { } asOf) return [];
        var cardIds = printings.Select(p => p.CardId).Distinct().ToList();
        var wanted = printings.ToHashSet();
        var rows = (await db.MarketSignals.AsNoTracking().Where(s => s.AsOf == asOf && cardIds.Contains(s.CardId)).ToListAsync(ct))
            .Where(s => wanted.Contains((s.CardId, s.Variant))).ToList();
        return await DescribeAsync(rows, ct);
    }

    public async Task<IReadOnlyList<WatchlistMoverView>> MoversAsync(IReadOnlyList<WatchlistItemView> watchlist, CancellationToken ct)
    {
        if (watchlist.Count == 0) return [];
        var cardIds = watchlist.Select(w => w.CardId).Distinct().ToList();
        var since = Today.AddDays(-45);
        var history = (await db.PriceSnapshots.AsNoTracking()
                .Where(s => cardIds.Contains(s.CardId) && s.Date >= since && s.Market != null)
                .Select(s => new { s.CardId, s.Variant, s.Date, s.Market })
                .ToListAsync(ct))
            .ToLookup(s => (s.CardId, s.Variant));
        return watchlist
            .Select(w =>
            {
                var series = new PriceSeries(history[(w.CardId, w.Variant)].Select(h => new PricePointDay(h.Date, h.Market)));
                return new WatchlistMoverView(w.Id, w.CardId, w.Variant, w.CardName, series.Latest?.Market,
                    Round(series.Change(7)), Round(series.Change(30)));
            })
            .Where(m => m.Change7Percent is not null || m.Change30Percent is not null)
            .OrderByDescending(m => Math.Abs(m.Change7Percent ?? m.Change30Percent ?? 0))
            .Take(5)
            .ToList();
    }

    public async Task<MarketSummaryView> SummaryAsync(CancellationToken ct)
    {
        var latest = await db.PriceSnapshots.AsNoTracking().MaxAsync(s => (DateOnly?)s.Date, ct);
        if (latest is null) return new MarketSummaryView(freshness.Of(null, Today), 0, 0, 0, new Dictionary<string, int>());
        var weekAgo = latest.Value.AddDays(-7);
        var moves = await db.PriceSnapshots.AsNoTracking()
            .Where(s => s.Date == latest && s.Market >= SignalRefreshService.MinPrice)
            .Join(db.PriceSnapshots.AsNoTracking().Where(p => p.Date == weekAgo && p.Market > 0),
                s => new { s.CardId, s.Variant }, p => new { p.CardId, p.Variant }, (s, p) => new { Now = s.Market, Then = p.Market })
            .GroupBy(_ => 1)
            .Select(g => new { Up = g.Count(x => x.Now > x.Then * 1.05m), Down = g.Count(x => x.Now < x.Then * 0.95m) })
            .FirstOrDefaultAsync(ct);
        var priced = await db.PriceSnapshots.AsNoTracking().CountAsync(s => s.Date == latest && s.Market != null, ct);
        var signalDay = await LatestSignalDayAsync(ct);
        var counts = signalDay is null
            ? new Dictionary<string, int>()
            : await db.MarketSignals.AsNoTracking().Where(s => s.AsOf == signalDay).GroupBy(s => s.Kind)
                .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, ct);
        return new MarketSummaryView(freshness.Of(latest, Today), priced, moves?.Up ?? 0, moves?.Down ?? 0, counts);
    }

    private async Task<IReadOnlyList<SignalItemView>> DescribeAsync(IReadOnlyList<MarketSignal> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return [];
        var cardIds = rows.Select(r => r.CardId).Distinct().ToList();
        var cards = await db.Cards.AsNoTracking().Where(c => cardIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Name, c.SetName, c.ImageSmall }).ToDictionaryAsync(c => c.Id, ct);
        var asOf = rows[0].AsOf;
        var prices = (await db.PriceSnapshots.AsNoTracking().Where(s => s.Date == asOf && cardIds.Contains(s.CardId))
            .Select(s => new { s.CardId, s.Variant, s.Market }).ToListAsync(ct))
            .ToDictionary(s => (s.CardId, s.Variant), s => s.Market);
        return rows.Select(r =>
        {
            cards.TryGetValue(r.CardId, out var card);
            prices.TryGetValue((r.CardId, r.Variant), out var market);
            return new SignalItemView(r.CardId, r.Variant, Prices.VariantLabel(r.Variant), card?.Name ?? r.CardId, card?.SetName ?? "", card?.ImageSmall,
                market, r.Kind, Names.GetValueOrDefault(r.Kind, r.Kind), r.Value, Unit(r.Kind), r.LookbackDays, r.Reason, r.Confidence,
                r.Provider == MarketProvider.TCGPlayer ? "TCGplayer API" : "Pokémon TCG API (TCGplayer market prices)", r.AsOf);
        }).ToList();
    }

    private static double? Round(double? value) => value is null ? null : Math.Round(value.Value, 1);
}
