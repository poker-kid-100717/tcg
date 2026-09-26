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
    public string SiteUrl { get; set; } = "https://tcg-portfolio-sample.app";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(StripeSecretKey) &&
        !string.IsNullOrWhiteSpace(StripeWebhookSecret) &&
        !string.IsNullOrWhiteSpace(ProMonthlyPriceId) &&
        !string.IsNullOrWhiteSpace(ProAnnualPriceId);

    public bool StoreFinderIsConfigured =>
        !string.IsNullOrWhiteSpace(StripeSecretKey) &&
        !string.IsNullOrWhiteSpace(StripeWebhookSecret) &&
        !string.IsNullOrWhiteSpace(StoreFinderMonthlyPriceId);
}

public sealed class EntitlementService(ProductStore store, BillingOptions billing)
{
    public async Task<AccountView> GetAccountAsync(Guid userId, CancellationToken cancellationToken)
    {
        var subscription = await store.GetSubscriptionAsync(userId, cancellationToken);

        // Until Stripe is configured, the production app runs as a founding preview:
        // paid workflows can be exercised without fake checkout or fake stock data.
        if (!billing.IsConfigured)
        {
            return new AccountView(
                userId,
                false,
                false,
                true,
                true,
                "preview",
                null,
                "Founding preview");
        }

        var active = subscription?.Status is "active" or "trialing";
        var plan = active ? subscription!.Plan : "free";
        var isPro = active && plan is "monthly" or "annual" or "complete";
        var hasStoreFinder = active && plan is "storefinder" or "complete";

        return new AccountView(
            userId,
            true,
            billing.StoreFinderIsConfigured,
            isPro,
            hasStoreFinder,
            plan,
            subscription?.Status,
            "Live billing");
    }
}
