using Microsoft.EntityFrameworkCore;
using PokemonTCG.API.Data;
using PokemonTCG.API.Market.Intelligence;

namespace PokemonTCG.API.Product;

public sealed class AlertOptions
{
    public const string SectionName = "Alerts";

    /// <summary>A rule that fired won't fire again for this long, even if its condition clears and returns.</summary>
    public int CooldownHours { get; set; } = 24;
}

/// <summary>A new alert, handed to each delivery channel.</summary>
public sealed record AlertNotification(Guid UserId, long WatchlistItemId, string Kind, string EventKey, string Message, decimal? CurrentValue, DateTimeOffset At);

/// <summary>A way to tell a user about an alert. In-app is always on; email or push would be further senders.</summary>
public interface INotificationSender
{
    string Channel { get; }

    /// <summary>Delivers the alert. Must be idempotent for the same <see cref="AlertNotification.EventKey"/>. Returns false when it was already delivered.</summary>
    Task<bool> SendAsync(AlertNotification notification, CancellationToken cancellationToken);
}

/// <summary>The in-app alert feed (dashboard and alerts list). Its unique event key makes delivery exactly-once.</summary>
public sealed class InAppNotificationSender(ProductStore store) : INotificationSender
{
    public const string ChannelName = "in_app";
    public string Channel => ChannelName;

    public Task<bool> SendAsync(AlertNotification n, CancellationToken cancellationToken) =>
        store.AddAlertIfMissingAsync(n.UserId, n.WatchlistItemId, n.Kind, n.EventKey, n.Message, n.CurrentValue, n.At, cancellationToken);
}

/// <summary>
/// Evaluates every enabled watchlist rule after a snapshot. Rules fire on the edge: when their condition becomes true
/// (not every day it stays true), at most once per cooldown, and never twice for the same day (idempotent event keys),
/// so re-running an evaluation is always safe. Only Pro accounts receive alerts.
/// </summary>
public sealed class AlertEvaluationService(
    AppDbContext db,
    ProductStore store,
    EntitlementService entitlements,
    IEnumerable<INotificationSender> senders,
    AlertOptions options,
    TimeProvider clock,
    ILogger<AlertEvaluationService> logger)
{
    public const string Below = "below";
    public const string Above = "above";
    public const string Move = "move";
    public const string Sleeper = "sleeper";
    public const string TrendingDown = "trending_down";
    public const string UnusualMove = "unusual_move";

    public async Task<int> EvaluateAsync(CancellationToken cancellationToken)
    {
        var targets = await store.GetEnabledWatchTargetsAsync(cancellationToken);
        if (targets.Count == 0) return 0;
        var states = await store.GetRuleStatesAsync(cancellationToken);
        var now = clock.GetUtcNow();
        var cooldown = TimeSpan.FromHours(Math.Max(0, options.CooldownHours));
        var inApp = senders.Single(s => s.Channel == InAppNotificationSender.ChannelName);
        var others = senders.Where(s => s != inApp).ToList();
        var pro = new Dictionary<Guid, bool>();
        var created = 0;

        foreach (var target in targets)
        {
            if (!pro.TryGetValue(target.UserId, out var isPro))
                pro[target.UserId] = isPro = (await entitlements.GetAccountAsync(target.UserId, cancellationToken)).IsPro;
            if (!isPro) continue;

            var latest = await db.PriceSnapshots.AsNoTracking()
                .Where(s => s.CardId == target.CardId && s.Variant == target.Variant && s.Market != null)
                .OrderByDescending(s => s.Date)
                .Select(s => new { s.Date, Market = s.Market!.Value })
                .FirstOrDefaultAsync(cancellationToken);
            if (latest is null) continue;
            var signals = await db.MarketSignals.AsNoTracking()
                .Where(s => s.CardId == target.CardId && s.Variant == target.Variant && s.AsOf == latest.Date)
                .ToDictionaryAsync(s => s.Kind, cancellationToken);

            foreach (var rule in Rules(target, latest.Market, signals))
            {
                states.TryGetValue((target.Id, rule.Kind), out var state);
                var fires = rule.Active && state is not { Active: true }
                    && (state?.LastFiredAt is not { } last || now - last >= cooldown);
                DateTimeOffset? firedAt = null;
                if (fires)
                {
                    var notification = new AlertNotification(target.UserId, target.Id, rule.Kind, $"{target.Id}:{rule.Kind}:{latest.Date:yyyy-MM-dd}",
                        rule.Message, latest.Market, now);
                    if (await inApp.SendAsync(notification, cancellationToken))
                    {
                        created++;
                        firedAt = now;
                        foreach (var sender in others)
                        {
                            try { await sender.SendAsync(notification, cancellationToken); }
                            catch (Exception ex) when (ex is not OperationCanceledException)
                            {
                                logger.LogWarning(ex, "The {Channel} channel failed for alert {Key}", sender.Channel, notification.EventKey);
                            }
                        }
                    }
                }
                if (state is null || state.Active != rule.Active || firedAt is not null)
                    await store.SetRuleStateAsync(target.Id, rule.Kind, rule.Active, firedAt, latest.Date, now, cancellationToken);
            }
        }

        if (created > 0) logger.LogInformation("Created {Count} watchlist alerts", created);
        return created;
    }

    private sealed record Rule(string Kind, bool Active, string Message);

    /// <summary>The rules this watchlist item has switched on, and whether each one's condition holds now.</summary>
    private static IEnumerable<Rule> Rules(ProductStore.WatchTarget t, decimal market, IReadOnlyDictionary<string, MarketSignal> signals)
    {
        if (t.TargetBelow is { } below)
            yield return new Rule(Below, market <= below, $"{t.CardName} is at ${market:0.00}, at or below your ${below:0.00} target.");
        if (t.TargetAbove is { } above)
            yield return new Rule(Above, market >= above, $"{t.CardName} is at ${market:0.00}, at or above your ${above:0.00} target.");
        if (t.MovePercent is { } move && move > 0 && t.BaselinePrice is { } baseline && baseline > 0)
        {
            var moved = (market / baseline - 1m) * 100m;
            yield return new Rule(Move, Math.Abs(moved) >= move,
                $"{t.CardName} moved {moved:+0.0;-0.0}% since you started watching it (${baseline:0.00} → ${market:0.00}).");
        }
        if (t.AlertSleeper)
            yield return SignalRule(Sleeper, Signals.Sleeper, "looks like a sleeper");
        if (t.AlertTrendingDown)
            yield return SignalRule(TrendingDown, Signals.SustainedDowntrend, "is in a sustained downtrend");
        if (t.AlertUnusualMove)
            yield return SignalRule(UnusualMove, Signals.UnusualMove, "made an unusual move");

        Rule SignalRule(string kind, string signal, string what) =>
            signals.TryGetValue(signal, out var s)
                ? new Rule(kind, true, $"{t.CardName} {what}: {s.Reason}")
                : new Rule(kind, false, "");
    }
}
