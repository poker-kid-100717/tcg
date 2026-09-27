using Microsoft.EntityFrameworkCore;
using PokemonTCG.API.Data;

namespace PokemonTCG.API.Market.Intelligence;

/// <summary>
/// After each successful snapshot, evaluates every priced printing's signals from its reference history and stores
/// the ones that fired, dated by the snapshot day. The signal center and signal alerts read these rows, so a signal
/// shown to a user and a signal that alerted them are the same computation.
/// </summary>
public sealed class SignalRefreshService(AppDbContext db, TimeProvider clock, ILogger<SignalRefreshService> logger)
{
    /// <summary>Signals need up to 37 daily moves; 60 days of prices covers every rule.</summary>
    private const int LookbackDays = 60;
    private const int BatchSize = 400;

    /// <summary>Cheaper printings are noise for signals (a $0.10 card "up 50%").</summary>
    public const decimal MinPrice = 2m;

    public async Task<int> RefreshAsync(CancellationToken cancellationToken)
    {
        var asOf = await db.PriceSnapshots.AsNoTracking().MaxAsync(s => (DateOnly?)s.Date, cancellationToken);
        if (asOf is null) return 0;
        var since = asOf.Value.AddDays(-LookbackDays);
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

        var keys = await db.PriceSnapshots.AsNoTracking()
            .Where(s => s.Date == asOf && s.Market >= MinPrice)
            .OrderBy(s => s.CardId).ThenBy(s => s.Variant)
            .Select(s => new { s.CardId, s.Variant, s.Provider })
            .ToListAsync(cancellationToken);

        var rows = new List<MarketSignal>();
        foreach (var batch in keys.Chunk(BatchSize))
        {
            var cardIds = batch.Select(k => k.CardId).Distinct().ToList();
            var history = await db.PriceSnapshots.AsNoTracking()
                .Where(s => cardIds.Contains(s.CardId) && s.Date >= since && s.Market != null)
                .OrderBy(s => s.Date)
                .Select(s => new { s.CardId, s.Variant, s.Date, s.Market, s.Low, s.Mid, s.High })
                .ToListAsync(cancellationToken);
            var byPrinting = history.ToLookup(h => (h.CardId, h.Variant));

            foreach (var key in batch)
            {
                var series = new PriceSeries(byPrinting[(key.CardId, key.Variant)].Select(p => new PricePointDay(p.Date, p.Market, p.Low, p.Mid, p.High)));
                var metrics = MarketMetrics.From(series, today);
                var confidence = MarketConfidence.Compute(metrics).Score;
                foreach (var signal in Signals.Evaluate(series, metrics, confidence))
                {
                    rows.Add(new MarketSignal
                    {
                        CardId = key.CardId,
                        Variant = key.Variant,
                        AsOf = asOf.Value,
                        Kind = signal.Kind,
                        Value = signal.Value,
                        LookbackDays = signal.LookbackDays,
                        Reason = signal.Reason.Length > 500 ? signal.Reason[..500] : signal.Reason,
                        Confidence = confidence ?? 0,
                        Provider = key.Provider,
                    });
                }
            }
        }

        // Replace the day's signals as a unit, so a re-run never leaves a mix of old and new.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.MarketSignals.Where(s => s.AsOf == asOf).ExecuteDeleteAsync(cancellationToken);
        db.MarketSignals.AddRange(rows);
        await db.SaveChangesAsync(cancellationToken);
        // Keep 90 days of signal history.
        await db.MarketSignals.Where(s => s.AsOf < asOf.Value.AddDays(-90)).ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        db.ChangeTracker.Clear();

        logger.LogInformation("Stored {Count} market signals across {Printings} printings for {AsOf}", rows.Count, keys.Count, asOf);
        return rows.Count;
    }
}
