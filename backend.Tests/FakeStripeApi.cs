using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using PokemonTCG.API.Product;

namespace PokemonTcgMarketplace.Backend.Tests;

/// <summary>
/// Stands in for api.stripe.com: subscriptions a test sets up are what "Stripe" reports when the webhook handler re-reads
/// them. Also answers Checkout, the Customer Portal and cancellation, and records every call. No network, no real keys.
/// </summary>
public sealed class FakeStripeApi : HttpMessageHandler
{
    public const string SecretKey = "sk_test_fixture_not_a_real_key";
    public const string WebhookSecret = "whsec_fixture_not_a_real_secret";
    public const string MonthlyPrice = "price_fixture_monthly";
    public const string AnnualPrice = "price_fixture_annual";
    public const string StoreFinderPrice = "price_fixture_storefinder";

    private readonly ConcurrentDictionary<string, object> _subscriptions = new();

    public ConcurrentQueue<(HttpMethod Method, string Path, string Body)> Calls { get; } = new();

    /// <summary>Makes Stripe report this subscription from now on (as a later state replaces an earlier one in Stripe).</summary>
    public void SetSubscription(string id, string customerId, string status, string priceId, Guid? userId = null, bool cancelAtPeriodEnd = false) =>
        _subscriptions[id] = new
        {
            id,
            @object = "subscription",
            customer = customerId,
            status,
            cancel_at_period_end = cancelAtPeriodEnd,
            metadata = userId is null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["user_id"] = userId.Value.ToString() },
            items = new
            {
                data = new[]
                {
                    new { price = new { id = priceId }, current_period_end = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds() },
                },
            },
        };

    /// <summary>A signed webhook request exactly as Stripe sends one.</summary>
    public static HttpRequestMessage Webhook(string type, object dataObject, string? eventId = null, string secret = WebhookSecret, long? timestamp = null)
    {
        var payload = JsonSerializer.Serialize(new
        {
            id = eventId ?? $"evt_{Guid.NewGuid():N}",
            @object = "event",
            type,
            created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            data = new { @object = dataObject },
        });
        var t = timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signature = Convert.ToHexString(StripeBillingService.Sign(payload, t, secret)).ToLowerInvariant();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/billing/webhook")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Stripe-Signature", $"t={t},v1={signature}");
        return request;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        Calls.Enqueue((request.Method, path, body));

        if (request.Headers.Authorization?.Parameter != SecretKey) return Json(HttpStatusCode.Unauthorized, new { error = new { message = "Invalid API key" } });

        if (path.StartsWith("/v1/subscriptions/"))
        {
            var id = Uri.UnescapeDataString(path["/v1/subscriptions/".Length..]);
            if (!_subscriptions.TryGetValue(id, out var subscription)) return Json(HttpStatusCode.NotFound, new { error = new { message = "No such subscription" } });
            if (request.Method == HttpMethod.Delete)
            {
                var doc = JsonSerializer.SerializeToNode(subscription)!;
                doc["status"] = "canceled";
                _subscriptions[id] = doc;
                return Json(HttpStatusCode.OK, doc);
            }
            return Json(HttpStatusCode.OK, subscription);
        }
        return path switch
        {
            "/v1/checkout/sessions" => Json(HttpStatusCode.OK, new { id = "cs_test_fixture", url = "https://checkout.stripe.test/c/cs_test_fixture" }),
            "/v1/billing_portal/sessions" => Json(HttpStatusCode.OK, new { id = "bps_test_fixture", url = "https://billing.stripe.test/p/session" }),
            _ => Json(HttpStatusCode.NotFound, new { error = new { message = "Unknown endpoint" } }),
        };
    }

    private static HttpResponseMessage Json(HttpStatusCode status, object body) =>
        new(status) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
}
