using PokemonTCG.API.Email;

namespace PokemonTCG.API.Product;

/// <summary>
/// Emails each signed-in collector one digest of their new watchlist alerts. Runs after the daily
/// price snapshot, right after <see cref="AlertEvaluationService"/> records the alerts.
/// </summary>
public sealed class AlertEmailService(
    ProductStore store,
    IEmailSender email,
    BillingOptions site,
    TimeProvider clock,
    ILogger<AlertEmailService> logger)
{
    /// <summary>
    /// Older alerts are never emailed. A send that fails is retried by the next daily run while the
    /// alert is still inside this window.
    /// </summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(2);

    /// <returns>The number of digests sent.</returns>
    public async Task<int> SendPendingAsync(CancellationToken cancellationToken)
    {
        if (!email.IsConfigured) return 0;

        var now = clock.GetUtcNow();
        var since = now - MaxAge;
        var pending = await store.GetPendingAlertEmailsAsync(since, cancellationToken);
        var sent = 0;

        foreach (var group in pending.GroupBy(p => p.UserId))
        {
            var alerts = group.ToList();
            var first = alerts[0];
            var message = EmailTemplates.AlertDigest(
                first.Email,
                alerts.Select(a => a.Message).ToList(),
                site.SiteUrl,
                first.UnsubscribeToken,
                idempotencyKey: $"alerts-{first.UserId:N}-{alerts.Max(a => a.Id)}");

            try
            {
                await email.SendAsync(message, cancellationToken);
                await store.MarkAlertEmailsProcessedAsync(alerts.Select(a => a.Id).ToArray(), now, cancellationToken);
                sent++;
            }
            catch (EmailDeliveryException ex)
            {
                logger.LogWarning(ex, "Alert email for user {UserId} was not sent; the next run retries it", first.UserId);
            }
        }

        await store.SkipUnsendableAlertEmailsAsync(since, now, cancellationToken);
        if (sent > 0) logger.LogInformation("Sent {Count} watchlist alert emails", sent);
        return sent;
    }
}
