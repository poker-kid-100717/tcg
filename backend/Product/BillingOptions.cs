using PokemonTCG.API.Email;

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
    public string SiteUrl { get; set; } = "https://pokemon.tcg-portfolio-sample.app";

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

/// <summary>Product features that depend on something outside this API.</summary>
public sealed class FeatureOptions
{
    public const string SectionName = "Features";

    /// <summary>
    /// Store Finder is offered only while Inventory.Service has a retailer source to show. In
    /// production the Worker turns this on when the Best Buy API key is configured.
    /// </summary>
    public bool StoreFinder { get; set; }
}

public sealed class EntitlementService(
    ProductStore store,
    BillingOptions billing,
    FeatureOptions features,
    IEmailSender email)
{
    public async Task<AccountView> GetAccountAsync(AppUser user, CancellationToken cancellationToken)
    {
        var subscription = await store.GetSubscriptionAsync(user.Id, cancellationToken);

        // Until Stripe is configured, the production app runs as a founding preview:
        // paid workflows can be exercised without fake checkout or fake stock data.
        if (!billing.IsConfigured)
        {
            return new AccountView(
                user.Id,
                false,
                false,
                true,
                features.StoreFinder,
                "preview",
                null,
                "Founding preview",
                user.SignedIn,
                user.Email,
                user.EmailAlerts,
                email.IsConfigured,
                features.StoreFinder);
        }

        var active = subscription?.Status is "active" or "trialing";
        var plan = active ? subscription!.Plan : "free";
        var isPro = active && plan is "monthly" or "annual" or "complete";
        var hasStoreFinder = active && plan is "storefinder" or "complete";

        return new AccountView(
            user.Id,
            true,
            billing.StoreFinderIsConfigured && features.StoreFinder,
            isPro,
            hasStoreFinder,
            plan,
            subscription?.Status,
            "Live billing",
            user.SignedIn,
            user.Email,
            user.EmailAlerts,
            email.IsConfigured,
            features.StoreFinder);
    }
}
