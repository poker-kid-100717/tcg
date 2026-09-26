using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PokemonTCG.API.Product;

public sealed class StripeBillingService(
    HttpClient http,
    BillingOptions options,
    ProductStore store,
    TimeProvider clock,
    ILogger<StripeBillingService> logger)
{
    public async Task<string> CreateCheckoutAsync(
        Guid userId,
        string plan,
        string? existingCustomerId,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var normalized = plan.Equals("annual", StringComparison.OrdinalIgnoreCase) ? "annual" :
            plan.Equals("monthly", StringComparison.OrdinalIgnoreCase) ? "monthly" :
            throw new ArgumentException("Plan must be monthly or annual.", nameof(plan));
        var priceId = normalized == "annual" ? options.ProAnnualPriceId : options.ProMonthlyPriceId;
        var site = options.SiteUrl.TrimEnd('/');

        var fields = new Dictionary<string, string>
        {
            ["mode"] = "subscription",
            ["success_url"] = $"{site}/dashboard?checkout=success",
            ["cancel_url"] = $"{site}/pro?checkout=cancelled",
            ["client_reference_id"] = userId.ToString(),
            ["line_items[0][price]"] = priceId,
            ["line_items[0][quantity]"] = "1",
            ["allow_promotion_codes"] = "true",
            ["metadata[user_id]"] = userId.ToString(),
            ["metadata[plan]"] = normalized,
            ["subscription_data[metadata][user_id]"] = userId.ToString(),
            ["subscription_data[metadata][plan]"] = normalized,
        };
        if (!string.IsNullOrWhiteSpace(existingCustomerId)) fields["customer"] = existingCustomerId;

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.stripe.com/v1/checkout/sessions")
        {
            Content = new FormUrlEncodedContent(fields),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.StripeSecretKey);
        using var response = await http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Stripe checkout failed ({(int)response.StatusCode}).");

        using var json = JsonDocument.Parse(body);
        return json.RootElement.GetProperty("url").GetString()
            ?? throw new InvalidOperationException("Stripe did not return a checkout URL.");
    }

    public async Task<string> CreatePortalAsync(string customerId, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var fields = new Dictionary<string, string>
        {
            ["customer"] = customerId,
            ["return_url"] = $"{options.SiteUrl.TrimEnd('/')}/pro",
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.stripe.com/v1/billing_portal/sessions")
        {
            Content = new FormUrlEncodedContent(fields),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.StripeSecretKey);
        using var response = await http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Stripe customer portal failed ({(int)response.StatusCode}).");

        using var json = JsonDocument.Parse(body);
        return json.RootElement.GetProperty("url").GetString()
            ?? throw new InvalidOperationException("Stripe did not return a portal URL.");
    }

    public async Task<bool> ProcessWebhookAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (!options.IsConfigured) return false;

        using var reader = new StreamReader(request.Body, Encoding.UTF8);
        var payload = await reader.ReadToEndAsync(cancellationToken);
        var signature = request.Headers["Stripe-Signature"].ToString();
        if (!VerifySignature(payload, signature))
        {
            logger.LogWarning("Rejected Stripe webhook with invalid signature");
            return false;
        }

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        var type = GetString(root, "type");
        if (string.IsNullOrWhiteSpace(type) ||
            !root.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("object", out var obj))
            return true;

        switch (type)
        {
            case "checkout.session.completed":
                await HandleCheckoutAsync(obj, cancellationToken);
                break;
            case "customer.subscription.created":
            case "customer.subscription.updated":
            case "customer.subscription.deleted":
                await HandleSubscriptionAsync(obj, cancellationToken);
                break;
            case "invoice.payment_failed":
                await HandlePaymentFailedAsync(obj, cancellationToken);
                break;
        }

        return true;
    }

    private async Task HandleCheckoutAsync(JsonElement obj, CancellationToken cancellationToken)
    {
        var userText = GetString(obj, "client_reference_id") ?? Metadata(obj, "user_id");
        if (!Guid.TryParse(userText, out var userId)) return;

        var customer = IdOrString(obj, "customer");
        var subscription = IdOrString(obj, "subscription");
        var plan = Metadata(obj, "plan") ?? "monthly";
        var email = obj.TryGetProperty("customer_details", out var details) ? GetString(details, "email") : null;

        await store.SetEmailAsync(userId, email, cancellationToken);
        await store.UpsertSubscriptionAsync(
            userId, customer, subscription, plan, "active", null, false, clock.GetUtcNow(), cancellationToken);
    }

    private async Task HandleSubscriptionAsync(JsonElement obj, CancellationToken cancellationToken)
    {
        var subscriptionId = GetString(obj, "id");
        var customerId = IdOrString(obj, "customer");
        var userText = Metadata(obj, "user_id");
        Guid? userId = Guid.TryParse(userText, out var parsed) ? parsed : null;
        userId ??= await store.FindUserByExternalAsync(customerId, subscriptionId, cancellationToken);
        if (userId is null) return;

        var status = GetString(obj, "status") ?? "inactive";
        var plan = Metadata(obj, "plan") ?? "";
        var cancelAtEnd = obj.TryGetProperty("cancel_at_period_end", out var cancel) && cancel.ValueKind == JsonValueKind.True;
        DateTimeOffset? periodEnd = null;
        if (obj.TryGetProperty("current_period_end", out var end) && end.TryGetInt64(out var epoch))
            periodEnd = DateTimeOffset.FromUnixTimeSeconds(epoch);

        await store.UpsertSubscriptionAsync(
            userId.Value, customerId, subscriptionId, plan, status, periodEnd, cancelAtEnd, clock.GetUtcNow(), cancellationToken);
    }

    private async Task HandlePaymentFailedAsync(JsonElement obj, CancellationToken cancellationToken)
    {
        var customerId = IdOrString(obj, "customer");
        var subscriptionId = IdOrString(obj, "subscription");
        var userId = await store.FindUserByExternalAsync(customerId, subscriptionId, cancellationToken);
        if (userId is null) return;

        var existing = await store.GetSubscriptionAsync(userId.Value, cancellationToken);
        await store.UpsertSubscriptionAsync(
            userId.Value,
            customerId,
            subscriptionId,
            existing?.Plan ?? "",
            "past_due",
            existing?.CurrentPeriodEnd,
            existing?.CancelAtPeriodEnd ?? false,
            clock.GetUtcNow(),
            cancellationToken);
    }

    private bool VerifySignature(string payload, string header)
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

        var signed = Encoding.UTF8.GetBytes($"{timestamp.Value}.{payload}");
        var key = Encoding.UTF8.GetBytes(options.StripeWebhookSecret);
        using var hmac = new HMACSHA256(key);
        var expected = hmac.ComputeHash(signed);

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

    private void EnsureConfigured()
    {
        if (!options.IsConfigured)
            throw new InvalidOperationException("Stripe billing is not configured.");
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string? Metadata(JsonElement element, string key) =>
        element.TryGetProperty("metadata", out var metadata) ? GetString(metadata, key) : null;

    private static string? IdOrString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.String) return value.GetString();
        return value.ValueKind == JsonValueKind.Object ? GetString(value, "id") : null;
    }
}
