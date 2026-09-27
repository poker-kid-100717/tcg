namespace PokemonTCG.API.Product;

public sealed class BillingOptions
{
    public const string SectionName = "Billing";

    public string StripeSecretKey { get; set; } = "";
    public string StripeWebhookSecret { get; set; } = "";
    public string ProMonthlyPriceId { get; set; } = "";
    public string ProAnnualPriceId { get; set; } = "";
    public string StoreFinderMonthlyPriceId { get; set; } = "";
    public string CompleteMonthlyPriceId { get; set; } = "";
    /// <summary>The public site. Checkout and portal return URLs are built from it, never from request input.</summary>
    public string SiteUrl { get; set; } = "https://tcg-portfolio-sample.app";

    /// <summary>Display prices for the pricing page. Billing itself always uses the Stripe price IDs.</summary>
    public decimal ProMonthlyPrice { get; set; } = 9.99m;
    public decimal ProAnnualPrice { get; set; } = 79m;
    public decimal StoreFinderMonthlyPrice { get; set; } = 4.99m;
    public decimal CompleteMonthlyPrice { get; set; } = 12.99m;
    public string Currency { get; set; } = "USD";

    /// <summary>Which plan a Stripe price ID bills for, or null for a price this app doesn't sell.</summary>
    public string? PlanForPrice(string? priceId) =>
        string.IsNullOrWhiteSpace(priceId) ? null
        : priceId == ProMonthlyPriceId ? "monthly"
        : priceId == ProAnnualPriceId ? "annual"
        : priceId == StoreFinderMonthlyPriceId ? "storefinder"
        : priceId == CompleteMonthlyPriceId ? "complete"
        : null;

    public bool StripeConfigured =>
        !string.IsNullOrWhiteSpace(StripeSecretKey) &&
        !string.IsNullOrWhiteSpace(StripeWebhookSecret);

    public bool IsConfigured =>
        StripeConfigured &&
        !string.IsNullOrWhiteSpace(ProMonthlyPriceId) &&
        !string.IsNullOrWhiteSpace(ProAnnualPriceId);

    public bool StoreFinderIsConfigured =>
        StripeConfigured &&
        !string.IsNullOrWhiteSpace(StoreFinderMonthlyPriceId);
}

/// <summary>
/// Entitlements come only from subscription state that Stripe webhooks have confirmed (and the webhook handler re-reads
/// from Stripe). A checkout redirect never grants anything, and without billing configured nobody is Pro.
/// </summary>
public sealed class EntitlementService(ProductStore store, BillingOptions billing)
{
    /// <summary>Statuses that keep access: paid up, trialing, or a failed payment Stripe is still retrying.</summary>
    private static readonly HashSet<string> Entitled = ["active", "trialing", "past_due"];

    public async Task<AccountView> GetAccountAsync(Guid userId, CancellationToken cancellationToken)
    {
        var subscription = await store.GetSubscriptionAsync(userId, cancellationToken);
        var entitled = subscription is not null && Entitled.Contains(subscription.Status);
        var plan = entitled ? subscription!.Plan : "free";
        var isPro = entitled && plan is "monthly" or "annual" or "complete";
        var hasStoreFinder = entitled && plan is "storefinder" or "complete";

        return new AccountView(
            userId,
            billing.IsConfigured,
            billing.StoreFinderIsConfigured,
            isPro,
            hasStoreFinder,
            plan,
            subscription?.Status,
            billing.IsConfigured ? "Live billing" : "Billing not configured")
        {
            CurrentPeriodEnd = subscription?.CurrentPeriodEnd,
            CancelAtPeriodEnd = subscription?.CancelAtPeriodEnd ?? false,
            PaymentIssue = subscription?.Status is "past_due" or "unpaid",
            CanManageBilling = billing.StripeConfigured && !string.IsNullOrWhiteSpace(subscription?.CustomerId),
        };
    }
}
