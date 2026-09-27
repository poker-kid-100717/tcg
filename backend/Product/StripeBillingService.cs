using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PokemonTCG.API.Product;

/// <summary>A subscription as Stripe reports it now.</summary>
public sealed record StripeSubscription(
    string Id,
    string? CustomerId,
    string Status,
    string? Plan,
    DateTimeOffset? CurrentPeriodEnd,
    bool CancelAtPeriodEnd,
    Guid? UserId);

/// <summary>
/// Stripe Checkout, the Customer Portal and webhooks. Stripe is the billing source of truth: webhook events are verified
/// by signature, recorded once (duplicates are skipped), and every subscription change is applied by re-reading the
/// subscription from Stripe, so the stored state is Stripe's current state whatever order events arrive in.
/// </summary>
public sealed class StripeBillingService(
    HttpClient http,
    BillingOptions options,
    ProductStore store,
    TimeProvider clock,
    ILogger<StripeBillingService> logger)
{
    private const string Api = "https://api.stripe.com/v1/";

    public async Task<string> CreateCheckoutAsync(Guid userId, string plan, string? existingCustomerId, string? email, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var priceId = plan switch
        {
            "monthly" => options.ProMonthlyPriceId,
            "annual" => options.ProAnnualPriceId,
            "storefinder" => options.StoreFinderMonthlyPriceId,
            "complete" => options.CompleteMonthlyPriceId,
            _ => throw new ArgumentException("Unknown subscription plan.", nameof(plan)),
        };
        if (string.IsNullOrWhiteSpace(priceId)) throw new InvalidOperationException($"Stripe price for {plan} is not configured.");

        var site = options.SiteUrl.TrimEnd('/');
        var fields = new Dictionary<string, string>
        {
            ["mode"] = "subscription",
            // Landing here grants nothing: entitlements change only when the verified webhook arrives.
            ["success_url"] = plan is "storefinder" or "complete" ? $"{site}/available-in-stores?checkout=success" : $"{site}/pro?checkout=success",
            ["cancel_url"] = $"{site}/pro?checkout=cancelled",
            ["client_reference_id"] = userId.ToString(),
            ["line_items[0][price]"] = priceId,
            ["line_items[0][quantity]"] = "1",
            ["allow_promotion_codes"] = "true",
            ["metadata[user_id]"] = userId.ToString(),
            ["subscription_data[metadata][user_id]"] = userId.ToString(),
        };
        if (!string.IsNullOrWhiteSpace(existingCustomerId)) fields["customer"] = existingCustomerId;
        else if (!string.IsNullOrWhiteSpace(email)) fields["customer_email"] = email;

        using var json = await SendAsync(HttpMethod.Post, "checkout/sessions", fields, cancellationToken);
        return json.RootElement.GetProperty("url").GetString() ?? throw new InvalidOperationException("Stripe did not return a checkout URL.");
    }

    public async Task<string> CreatePortalAsync(string customerId, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        using var json = await SendAsync(HttpMethod.Post, "billing_portal/sessions", new()
        {
            ["customer"] = customerId,
            ["return_url"] = $"{options.SiteUrl.TrimEnd('/')}/account",
        }, cancellationToken);
        return json.RootElement.GetProperty("url").GetString() ?? throw new InvalidOperationException("Stripe did not return a portal URL.");
    }

    /// <summary>Cancels immediately (used when an account is deleted).</summary>
    public async Task CancelNowAsync(string subscriptionId, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        using var _ = await SendAsync(HttpMethod.Delete, $"subscriptions/{Uri.EscapeDataString(subscriptionId)}", null, cancellationToken);
    }

    public async Task<StripeSubscription> GetSubscriptionAsync(string subscriptionId, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        using var json = await SendAsync(HttpMethod.Get, $"subscriptions/{Uri.EscapeDataString(subscriptionId)}", null, cancellationToken);
        return ReadSubscription(json.RootElement);
    }

    public enum WebhookOutcome { Processed, Duplicate, Ignored, InvalidSignature, NotConfigured }

    public async Task<WebhookOutcome> ProcessWebhookAsync(string payload, string signatureHeader, CancellationToken cancellationToken)
    {
        if (!options.StripeConfigured) return WebhookOutcome.NotConfigured;
        if (!VerifySignature(payload, signatureHeader))
        {
            logger.LogWarning("Rejected a Stripe webhook with an invalid signature");
            return WebhookOutcome.InvalidSignature;
        }

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        var eventId = GetString(root, "id");
        var type = GetString(root, "type");
        if (string.IsNullOrWhiteSpace(eventId) || string.IsNullOrWhiteSpace(type) ||
            !root.TryGetProperty("data", out var data) || !data.TryGetProperty("object", out var obj))
            return WebhookOutcome.Ignored;
        var created = root.TryGetProperty("created", out var c) && c.TryGetInt64(out var epoch)
            ? DateTimeOffset.FromUnixTimeSeconds(epoch)
            : clock.GetUtcNow();

        if (!await store.TryRecordStripeEventAsync(eventId, type, created, clock.GetUtcNow(), cancellationToken))
            return WebhookOutcome.Duplicate;

        try
        {
            var handled = type switch
            {
                "checkout.session.completed" => await HandleCheckoutAsync(obj, cancellationToken),
                "customer.subscription.created" or "customer.subscription.updated" or "customer.subscription.deleted"
                    or "customer.subscription.paused" or "customer.subscription.resumed"
                    => await RefreshAsync(GetString(obj, "id"), IdOrString(obj, "customer"), MetadataUser(obj), cancellationToken),
                // Payment failure (Stripe moves the subscription to past_due) and restored payment (back to active).
                "invoice.payment_failed" or "invoice.paid" or "invoice.payment_succeeded"
                    => await RefreshAsync(InvoiceSubscription(obj), IdOrString(obj, "customer"), null, cancellationToken),
                _ => false,
            };
            return handled ? WebhookOutcome.Processed : WebhookOutcome.Ignored;
        }
        catch
        {
            // Let Stripe's retry through instead of treating the failed attempt as done.
            await store.ForgetStripeEventAsync(eventId, CancellationToken.None);
            throw;
        }
    }

    private async Task<bool> HandleCheckoutAsync(JsonElement session, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(GetString(session, "client_reference_id") ?? Metadata(session, "user_id"), out var userId)) return false;
        return await RefreshAsync(IdOrString(session, "subscription"), IdOrString(session, "customer"), userId, cancellationToken);
    }

    /// <summary>Re-reads the subscription from Stripe and stores its current state for the user it belongs to.</summary>
    private async Task<bool> RefreshAsync(string? subscriptionId, string? customerId, Guid? knownUser, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(subscriptionId)) return false;
        var subscription = await GetSubscriptionAsync(subscriptionId, cancellationToken);
        var userId = knownUser ?? subscription.UserId
            ?? await store.FindUserByExternalAsync(subscription.CustomerId ?? customerId, subscription.Id, cancellationToken);
        if (userId is null)
        {
            logger.LogWarning("Stripe subscription {Subscription} doesn't belong to a known account", subscription.Id);
            return false;
        }

        var now = clock.GetUtcNow();
        await store.UpsertSubscriptionAsync(
            userId.Value, subscription.CustomerId ?? customerId, subscription.Id, subscription.Plan ?? "", subscription.Status,
            subscription.CurrentPeriodEnd, subscription.CancelAtPeriodEnd, now, cancellationToken, stateAt: now);
        return true;
    }

    private StripeSubscription ReadSubscription(JsonElement obj)
    {
        string? priceId = null;
        DateTimeOffset? periodEnd = null;
        if (obj.TryGetProperty("items", out var items) && items.TryGetProperty("data", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in list.EnumerateArray())
            {
                priceId ??= item.TryGetProperty("price", out var price) ? GetString(price, "id") : null;
                // Current Stripe API versions report the billing period per item.
                if (periodEnd is null && item.TryGetProperty("current_period_end", out var itemEnd) && itemEnd.TryGetInt64(out var e))
                    periodEnd = DateTimeOffset.FromUnixTimeSeconds(e);
            }
        }
        if (periodEnd is null && obj.TryGetProperty("current_period_end", out var end) && end.TryGetInt64(out var legacy))
            periodEnd = DateTimeOffset.FromUnixTimeSeconds(legacy);

        return new StripeSubscription(
            GetString(obj, "id") ?? "",
            IdOrString(obj, "customer"),
            GetString(obj, "status") ?? "incomplete",
            options.PlanForPrice(priceId),
            periodEnd,
            obj.TryGetProperty("cancel_at_period_end", out var cancel) && cancel.ValueKind == JsonValueKind.True,
            MetadataUser(obj));
    }

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, Dictionary<string, string>? fields, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, Api + path);
        if (fields is not null) request.Content = new FormUrlEncodedContent(fields);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.StripeSecretKey);
        using var response = await http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        // Never log Stripe's response body or the key: just the status.
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Stripe {method} {path.Split('/')[0]} failed ({(int)response.StatusCode}).");
        return JsonDocument.Parse(body);
    }

    public bool VerifySignature(string payload, string header)
    {
        if (string.IsNullOrWhiteSpace(header) || string.IsNullOrWhiteSpace(options.StripeWebhookSecret)) return false;

        long? timestamp = null;
        var signatures = new List<string>();
        foreach (var part in header.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pair = part.Split('=', 2);
            if (pair.Length != 2) continue;
            if (pair[0] == "t" && long.TryParse(pair[1], out var value)) timestamp = value;
            if (pair[0] == "v1") signatures.Add(pair[1]);
        }
        if (timestamp is null || signatures.Count == 0) return false;
        if (Math.Abs(clock.GetUtcNow().ToUnixTimeSeconds() - timestamp.Value) > 300) return false;

        var expected = Sign(payload, timestamp.Value, options.StripeWebhookSecret);
        foreach (var candidate in signatures)
        {
            try
            {
                var bytes = Convert.FromHexString(candidate);
                if (bytes.Length == expected.Length && CryptographicOperations.FixedTimeEquals(bytes, expected)) return true;
            }
            catch (FormatException)
            {
                // Ignore malformed signatures.
            }
        }
        return false;
    }

    /// <summary>Stripe's v1 signature: HMAC-SHA256 of "{timestamp}.{payload}" with the endpoint secret.</summary>
    public static byte[] Sign(string payload, long timestamp, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{payload}"));
    }

    private void EnsureConfigured()
    {
        if (!options.StripeConfigured) throw new InvalidOperationException("Stripe billing is not configured.");
    }

    private static string? InvoiceSubscription(JsonElement invoice)
    {
        if (IdOrString(invoice, "subscription") is { } direct) return direct;
        // Newer API versions nest it under parent.subscription_details.
        return invoice.TryGetProperty("parent", out var parent) && parent.TryGetProperty("subscription_details", out var details)
            ? IdOrString(details, "subscription")
            : null;
    }

    private static Guid? MetadataUser(JsonElement obj) => Guid.TryParse(Metadata(obj, "user_id"), out var id) ? id : null;

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string? Metadata(JsonElement element, string key) =>
        element.TryGetProperty("metadata", out var metadata) && metadata.ValueKind == JsonValueKind.Object ? GetString(metadata, key) : null;

    private static string? IdOrString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.String) return value.GetString();
        return value.ValueKind == JsonValueKind.Object ? GetString(value, "id") : null;
    }
}
