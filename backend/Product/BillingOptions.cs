namespace PokemonTCG.API.Product;

public sealed class BillingOptions
{
    public const string SectionName = "Billing";

    public string StripeSecretKey { get; set; } = "";
    public string StripeWebhookSecret { get; set; } = "";
    public string ProMonthlyPriceId { get; set; } = "";
    public string ProAnnualPriceId { get; set; } = "";
    public string SiteUrl { get; set; } = "https://tcg-portfolio-sample.app";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(StripeSecretKey) &&
        !string.IsNullOrWhiteSpace(StripeWebhookSecret) &&
        !string.IsNullOrWhiteSpace(ProMonthlyPriceId) &&
        !string.IsNullOrWhiteSpace(ProAnnualPriceId);
}

public sealed class EntitlementService(ProductStore store, BillingOptions billing)
{
    public async Task<AccountView> GetAccountAsync(Guid userId, CancellationToken cancellationToken)
    {
        var subscription = await store.GetSubscriptionAsync(userId, cancellationToken);

        // Until Stripe is configured, the production app runs as a founding preview:
        // every visitor can exercise the entire paid workflow without fake billing.
        if (!billing.IsConfigured)
        {
            return new AccountView(userId, false, true, "preview", null, "Founding preview");
        }

        var active = subscription?.Status is "active" or "trialing";
        return new AccountView(
            userId,
            true,
            active,
            active ? subscription!.Plan : "free",
            subscription?.Status,
            "Live billing");
    }
}
