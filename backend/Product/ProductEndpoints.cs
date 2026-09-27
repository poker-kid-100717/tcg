using Microsoft.EntityFrameworkCore;
using PokemonTCG.API.Accounts;
using PokemonTCG.API.Data;
using PokemonTCG.API.Pricing;

namespace PokemonTCG.API.Product;

public static class ProductEndpoints
{
    public static void MapProduct(this IEndpointRouteBuilder app)
    {
        // Everything here is account-scoped: a signed-in user is required, and the user id always comes from the
        // server-side session, never from the request.
        var api = app.MapGroup("/api").RequireAuthorization();

        // The signed-in user's entitlements. The edge gateway reads this (GET) before forwarding Store Finder requests.
        api.MapMethods("/session", ["GET", "POST"], async Task<IResult> (
            HttpContext context,
            EntitlementService entitlements,
            CancellationToken ct) =>
            Results.Ok(await entitlements.GetAccountAsync(context.User.RequireUserId(), ct)));

        api.MapGet("/cards/{id}/intelligence", async Task<IResult> (
            string id,
            string? variant,
            MarketIntelligenceService intelligence,
            CancellationToken ct) =>
            await intelligence.GetAsync(id, variant, ct) is { } value
                ? Results.Ok(value)
                : Results.NotFound())
            .RequireAuthorization(AuthPolicies.RequirePro);

        api.MapGet("/watchlist", async (
            HttpContext context,
            ProductStore store,
            CancellationToken ct) =>
        {
            var user = new { Id = context.User.RequireUserId() };
            return Results.Ok(await store.GetWatchlistAsync(user.Id, ct));
        });

        api.MapPost("/watchlist", async Task<IResult> (
            CreateWatchlistRequest request,
            HttpContext context,
            EntitlementService entitlements,
            ProductStore store,
            AppDbContext db,
            TimeProvider clock,
            CancellationToken ct) =>
        {
            var user = new { Id = context.User.RequireUserId() };
            var account = await entitlements.GetAccountAsync(user.Id, ct);
            if (!account.IsPro && await store.CountWatchlistAsync(user.Id, ct) >= FreeWatchlistLimit)
                return Results.Problem(statusCode: 403, type: "pro_required", title: "Free watchlist limit reached", detail: "Pro unlocks an unlimited watchlist.");
            if (!account.IsPro && request.HasAlerts)
                return Results.Problem(statusCode: 403, type: "pro_required", title: "Alerts are part of TCG Signal Pro", detail: "Free accounts can watch up to 3 cards without alerts.");

            var error = ValidateThresholds(request.TargetBelow, request.TargetAbove, request.MovePercent);
            if (error is not null) return Results.ValidationProblem(new Dictionary<string, string[]> { ["alert"] = [error] });
            if (string.IsNullOrWhiteSpace(request.CardId) || string.IsNullOrWhiteSpace(request.Variant))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["card"] = ["Card and printing are required."] });

            var card = await db.Cards.AsNoTracking().SingleOrDefaultAsync(c => c.Id == request.CardId, ct);
            if (card is null) return Results.NotFound(new { message = "This card has not been indexed by the daily price snapshot yet." });

            var baseline = await db.PriceSnapshots.AsNoTracking()
                .Where(s => s.CardId == request.CardId && s.Variant == request.Variant && s.Market != null)
                .OrderByDescending(s => s.Date)
                .Select(s => s.Market)
                .FirstOrDefaultAsync(ct);
            if (baseline is null) return Results.ValidationProblem(new Dictionary<string, string[]> { ["variant"] = ["That printing has no recorded market price."] });

            var safe = request with
            {
                CardName = card.Name,
                SetName = card.SetName,
                ImageUrl = card.ImageSmall,
            };
            var id = await store.AddWatchlistAsync(user.Id, safe, baseline, clock.GetUtcNow(), ct);
            return Results.Ok(new { id });
        });

        api.MapPut("/watchlist/{id:long}", async Task<IResult> (
            long id,
            UpdateWatchlistRequest request,
            HttpContext context,
            EntitlementService entitlements,
            ProductStore store,
            CancellationToken ct) =>
        {
            var error = ValidateThresholds(request.TargetBelow, request.TargetAbove, request.MovePercent);
            if (error is not null) return Results.ValidationProblem(new Dictionary<string, string[]> { ["alert"] = [error] });
            var user = new { Id = context.User.RequireUserId() };
            if (request.HasAlerts && !(await entitlements.GetAccountAsync(user.Id, ct)).IsPro)
                return Results.Problem(statusCode: 403, type: "pro_required", title: "Alerts are part of TCG Signal Pro");
            return await store.UpdateWatchlistAsync(user.Id, id, request, ct) ? Results.NoContent() : Results.NotFound();
        });

        api.MapDelete("/watchlist/{id:long}", async Task<IResult> (
            long id,
            HttpContext context,
            ProductStore store,
            CancellationToken ct) =>
        {
            var user = new { Id = context.User.RequireUserId() };
            return await store.DeleteWatchlistAsync(user.Id, id, ct) ? Results.NoContent() : Results.NotFound();
        });

        api.MapGet("/alerts", async (
            HttpContext context,
            ProductStore store,
            CancellationToken ct) =>
        {
            var user = new { Id = context.User.RequireUserId() };
            return Results.Ok(await store.GetAlertsAsync(user.Id, 50, ct));
        });

        api.MapPost("/alerts/{id:long}/read", async Task<IResult> (
            long id,
            HttpContext context,
            ProductStore store,
            TimeProvider clock,
            CancellationToken ct) =>
        {
            var user = new { Id = context.User.RequireUserId() };
            return await store.MarkAlertReadAsync(user.Id, id, clock.GetUtcNow(), ct) ? Results.NoContent() : Results.NotFound();
        });

        api.MapPost("/alerts/read-all", async (
            HttpContext context,
            ProductStore store,
            TimeProvider clock,
            CancellationToken ct) =>
        {
            var user = new { Id = context.User.RequireUserId() };
            await store.MarkAllAlertsReadAsync(user.Id, clock.GetUtcNow(), ct);
            return Results.NoContent();
        });

        api.MapGet("/dashboard", async (
            HttpContext context,
            EntitlementService entitlements,
            ProductStore store,
            CancellationToken ct) =>
        {
            var user = new { Id = context.User.RequireUserId() };
            var account = await entitlements.GetAccountAsync(user.Id, ct);
            var watchlist = await store.GetWatchlistAsync(user.Id, ct);
            var alerts = await store.GetAlertsAsync(user.Id, 20, ct);
            return Results.Ok(new DashboardView(account, watchlist, alerts, alerts.Count(a => a.ReadAt is null)));
        });

        api.MapGet("/master-sets", async (
            HttpContext context,
            MasterSetStore masterSets,
            CancellationToken ct) =>
        {
            var user = new { Id = context.User.RequireUserId() };
            return Results.Ok(await masterSets.GetSummariesAsync(user.Id, ct));
        });

        api.MapPost("/master-sets", async Task<IResult> (
            CreateMasterSetRequest request,
            HttpContext context,
            EntitlementService entitlements,
            MasterSetStore masterSets,
            MasterSetService service,
            CancellationToken ct) =>
        {
            var user = new { Id = context.User.RequireUserId() };
            var account = await entitlements.GetAccountAsync(user.Id, ct);
            if (!account.IsPro && await masterSets.CountAsync(user.Id, ct) >= 1)
                return Results.Problem(statusCode: 403, title: "Free master-set limit reached", detail: "Pro unlocks unlimited master sets.");

            var id = await service.CreateAsync(user.Id, request.SetId, ct);
            return id is { } value ? Results.Ok(new { id = value }) : Results.NotFound();
        });

        api.MapGet("/master-sets/{id:long}", async Task<IResult> (
            long id,
            HttpContext context,
            MasterSetStore masterSets,
            CancellationToken ct) =>
        {
            var user = new { Id = context.User.RequireUserId() };
            return await masterSets.GetDetailAsync(user.Id, id, ct) is { } value
                ? Results.Ok(value)
                : Results.NotFound();
        });

        api.MapPut("/master-sets/{id:long}/items", async Task<IResult> (
            long id,
            UpdateMasterSetItemRequest request,
            HttpContext context,
            MasterSetStore masterSets,
            TimeProvider clock,
            CancellationToken ct) =>
        {
            if (request.OwnedQuantity < 0)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["ownedQuantity"] = ["Owned quantity cannot be negative."] });
            if (request.AcquiredPrice < 0)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["acquiredPrice"] = ["Acquired price cannot be negative."] });
            var user = new { Id = context.User.RequireUserId() };
            return await masterSets.UpdateItemAsync(user.Id, id, request, clock.GetUtcNow(), ct)
                ? Results.NoContent()
                : Results.NotFound();
        });

        api.MapDelete("/master-sets/{id:long}", async Task<IResult> (
            long id,
            HttpContext context,
            MasterSetStore masterSets,
            CancellationToken ct) =>
        {
            var user = new { Id = context.User.RequireUserId() };
            return await masterSets.DeleteAsync(user.Id, id, ct) ? Results.NoContent() : Results.NotFound();
        });

        api.MapGet("/master-sets/{id:long}/advisor-context", async Task<IResult> (
            long id,
            HttpContext context,
            EntitlementService entitlements,
            MasterSetService service,
            CancellationToken ct) =>
        {
            var user = new { Id = context.User.RequireUserId() };
            var account = await entitlements.GetAccountAsync(user.Id, ct);
            if (!account.IsPro) return ProRequired();
            return await service.GetAdvisorContextAsync(user.Id, id, ct) is { } value
                ? Results.Ok(value)
                : Results.NotFound();
        });

        api.MapPost("/billing/checkout", async Task<IResult> (
            CheckoutRequest request,
            HttpContext context,
            EntitlementService entitlements,
            ProductStore store,
            AccountStore accounts,
            BillingOptions options,
            StripeBillingService stripe,
            CancellationToken ct) =>
        {
            var userId = context.User.RequireUserId();
            var account = await entitlements.GetAccountAsync(userId, ct);
            var plan = request.Plan?.Trim().ToLowerInvariant();
            if (plan is not ("monthly" or "annual" or "storefinder" or "complete"))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["plan"] = ["Choose a supported subscription plan."] });

            if (plan is "monthly" or "annual" && !account.BillingConfigured)
                return Results.Problem(statusCode: 503, title: "Pro billing is not configured yet.");
            if (plan == "storefinder" && !account.StoreFinderBillingConfigured)
                return Results.Problem(statusCode: 503, title: "Store Finder billing is not configured yet.");
            if (plan == "complete" && (!options.StripeConfigured || string.IsNullOrWhiteSpace(options.CompleteMonthlyPriceId)))
                return Results.Problem(statusCode: 503, title: "Complete plan billing is not configured yet.");
            if (account.IsPro && plan is "monthly" or "annual" or "complete")
                return Results.Problem(statusCode: 409, title: "You already have TCG Signal Pro. Manage or change it from your account.");

            var subscription = await store.GetSubscriptionAsync(userId, ct);
            var email = (await accounts.GetAsync(userId, ct))?.Email;
            var url = await stripe.CreateCheckoutAsync(userId, plan, subscription?.CustomerId, email, ct);
            return Results.Ok(new BillingLink(url));
        }).RequireRateLimiting(AuthPolicies.BillingRateLimit);

        api.MapPost("/billing/portal", async Task<IResult> (
            HttpContext context,
            ProductStore store,
            BillingOptions options,
            StripeBillingService stripe,
            CancellationToken ct) =>
        {
            if (!options.StripeConfigured)
                return Results.Problem(statusCode: 503, title: "Billing is not configured yet.");
            var subscription = await store.GetSubscriptionAsync(context.User.RequireUserId(), ct);
            if (string.IsNullOrWhiteSpace(subscription?.CustomerId))
                return Results.Problem(statusCode: 400, title: "No billing account exists yet.");
            return Results.Ok(new BillingLink(await stripe.CreatePortalAsync(subscription.CustomerId, ct)));
        }).RequireRateLimiting(AuthPolicies.BillingRateLimit);

        api.MapGet("/billing/subscription", async (HttpContext context, EntitlementService entitlements, CancellationToken ct) =>
        {
            var account = await entitlements.GetAccountAsync(context.User.RequireUserId(), ct);
            return new SubscriptionView(account.Plan, account.SubscriptionStatus, account.IsPro, account.CurrentPeriodEnd,
                account.CancelAtPeriodEnd, account.PaymentIssue, account.CanManageBilling);
        });

        // Public: what the pricing page shows. Prices are configuration, so they can change without a code change.
        app.MapGet("/api/billing/plans", (BillingOptions options) => new[]
        {
            new PlanView("free", "Free", 0m, options.Currency, "forever", true),
            new PlanView("monthly", "Pro monthly", options.ProMonthlyPrice, options.Currency, "month", options.IsConfigured),
            new PlanView("annual", "Pro annual", options.ProAnnualPrice, options.Currency, "year", options.IsConfigured),
            new PlanView("storefinder", "Store Finder", options.StoreFinderMonthlyPrice, options.Currency, "month", options.StoreFinderIsConfigured),
            new PlanView("complete", "Complete", options.CompleteMonthlyPrice, options.Currency, "month",
                options.StripeConfigured && !string.IsNullOrWhiteSpace(options.CompleteMonthlyPriceId)),
        });

        // Public, authenticated by Stripe's signature. Duplicates answer 200 so Stripe stops retrying them.
        app.MapPost("/api/billing/webhook", async Task<IResult> (HttpRequest request, StripeBillingService stripe, CancellationToken ct) =>
        {
            using var reader = new StreamReader(request.Body, System.Text.Encoding.UTF8);
            var payload = await reader.ReadToEndAsync(ct);
            if (payload.Length > 1_000_000) return Results.BadRequest();
            return await stripe.ProcessWebhookAsync(payload, request.Headers["Stripe-Signature"].ToString(), ct) switch
            {
                StripeBillingService.WebhookOutcome.InvalidSignature => Results.BadRequest(),
                StripeBillingService.WebhookOutcome.NotConfigured => Results.Problem(statusCode: 503, title: "Billing is not configured."),
                _ => Results.Ok(),
            };
        });
    }

    public const int FreeWatchlistLimit = 3;

    private static IResult ProRequired() =>
        Results.Problem(statusCode: 403, type: "pro_required", title: "TCG Signal Pro required", detail: "Upgrade to unlock Market Intelligence.");

    private static string? ValidateThresholds(decimal? below, decimal? above, decimal? move)
    {
        if (below < 0 || above < 0) return "Price targets cannot be negative.";
        if (move is < 1 or > 500) return "Move threshold must be between 1% and 500%.";
        if (below is not null && above is not null && below >= above) return "Below target must be less than above target.";
        return null;
    }
}
