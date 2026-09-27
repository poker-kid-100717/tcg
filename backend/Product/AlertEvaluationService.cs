using Microsoft.EntityFrameworkCore;
using PokemonTCG.API.Data;

namespace PokemonTCG.API.Product;

public sealed class AlertEvaluationService(
    AppDbContext db,
    ProductStore store,
    TimeProvider clock,
    ILogger<AlertEvaluationService> logger)
{
    public async Task<int> EvaluateAsync(CancellationToken cancellationToken)
    {
        var targets = await store.GetEnabledWatchTargetsAsync(cancellationToken);
        var created = 0;

        foreach (var target in targets)
        {
            var prices = await db.PriceSnapshots.AsNoTracking()
                .Where(s => s.CardId == target.CardId && s.Variant == target.Variant && s.Market != null)
                .OrderByDescending(s => s.Date)
                .Take(2)
                .Select(s => new PriceAt(s.Date, s.Market!.Value))
                .ToListAsync(cancellationToken);
            if (prices.Count == 0) continue;

            var current = prices[0];
            var previous = prices.Count > 1 ? prices[1] : null;

            if (target.TargetBelow is { } below &&
                current.Market <= below &&
                (previous is null || previous.Market > below))
            {
                created += await Add(target, "below", $"below:{below:0.00}:{current.Date}",
                    $"{target.CardName} crossed below your {below:C} target at {current.Market:C}.",
                    current.Market, cancellationToken) ? 1 : 0;
            }

            if (target.TargetAbove is { } above &&
                current.Market >= above &&
                (previous is null || previous.Market < above))
            {
                created += await Add(target, "above", $"above:{above:0.00}:{current.Date}",
                    $"{target.CardName} crossed above your {above:C} target at {current.Market:C}.",
                    current.Market, cancellationToken) ? 1 : 0;
            }

            if (target.MovePercent is { } move && move > 0 && target.BaselinePrice is { } baseline && baseline > 0)
            {
                var currentMove = Math.Abs((current.Market / baseline - 1m) * 100m);
                var previousMove = previous is null ? 0m : Math.Abs((previous.Market / baseline - 1m) * 100m);
                if (currentMove >= move && previousMove < move)
                {
                    created += await Add(target, "move", $"move:{move:0.##}:{current.Date}",
                        $"{target.CardName} moved {currentMove:0.0}% from the price when you started watching it.",
                        current.Market, cancellationToken) ? 1 : 0;
                }
            }
        }

        if (created > 0) logger.LogInformation("Created {Count} watchlist alerts", created);
        return created;
    }

    private Task<bool> Add(
        ProductStore.WatchTarget target,
        string kind,
        string suffix,
        string message,
        decimal current,
        CancellationToken cancellationToken) =>
        store.AddAlertIfMissingAsync(
            target.UserId,
            target.Id,
            kind,
            $"{target.Id}:{suffix}",
            message,
            current,
            clock.GetUtcNow(),
            cancellationToken);

    private sealed record PriceAt(DateOnly Date, decimal Market);
}
