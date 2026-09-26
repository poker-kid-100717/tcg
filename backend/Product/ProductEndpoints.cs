using Microsoft.EntityFrameworkCore;
using PokemonTCG.API.Data;
using PokemonTCG.API.Pricing;

namespace PokemonTCG.API.Product;

public static class ProductEndpoints
{
    public static void MapProduct(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapPost("/session", async Task<IResult> (
            HttpContext context,
            SessionService sessions,
            EntitlementService entitlements,
            CancellationToken ct) =>
        {
            var user = await sessions.GetOrCreateAsync(context, ct);
            return Results.Ok(await entitlements.GetAccountAsync(user.Id, ct));
        });

        api.MapGet("/cards/{id}/intelligence", async Task<IResult> (
            string id,
            string? variant,
            HttpContext context,
            SessionService sessions,
            EntitlementService entitlements,
            MarketIntelligenceService intelligence,
            CancellationToken ct) =>
        {
            var user = await sessions.GetOrCreateAsync(context, ct);
            var account = await entitlements.GetAccountAsync(user.Id, ct);
            if (!account.IsPro) return ProRequired();
            return await intelligence.GetAsync(id, variant, ct) is { } value
                ? Results.Ok(value)
                : Results.NotFound();
        });

        api.MapGet("/watchlist", async (
            HttpContext context,
            SessionService sessions,
            ProductStore store,
            CancellationToken ct) =>
        {
            var user = await sessions.GetOrCreateAsync(context, ct);
            return Results.Ok(await store.GetWatchlistAsync(user.Id, ct));
        });

        api.MapPost("/watchlist", async Task<IResult> (
            CreateWatchlistRequest request,
            HttpContext context,
            SessionService sessions,
            EntitlementService entitlements,
            ProductStore store,
            AppDbContext db,
            TimeProvider clock,
            CancellationToken ct) =>
        {
            var user = await sessions.GetOrCreateAsync(context, ct);
            var account = await entitlements.GetAccountAsync(user.Id, ct);
            if (!account.IsPro && await store.CountWatchlistAsync(user.Id, ct) >= 3)
                return Results.Problem(statusCode: 403, title: "Free watchlist limit reached", detail: "Pro unlocks an unlimited watchlist.");

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
            SessionService sessions,
            ProductStore store,
            CancellationToken ct) =>
        {
            var error = ValidateThresholds(request.TargetBelow, request.TargetAbove, request.MovePercent);
            if (error is not null) return Results.ValidationProblem(new Dictionary<string, string[]> { ["alert"] = [error] });
            var user = await sessions.GetOrCreateAsync(context, ct);
            return await store.UpdateWatchlistAsync(user.Id, id, request, ct) ? Results.NoContent() : Results.NotFound();
        });

        api.MapDelete("/watchlist/{id:long}", async Task<IResult> (
            long id,
            HttpContext context,
            SessionService sessions,
            ProductStore store,
            CancellationToken ct) =>
        {
            var user = await sessions.GetOrCreateAsync(context, ct);
            return await store.DeleteWatchlistAsync(user.Id, id, ct) ? Results.NoContent() : Results.NotFound();
        });

        api.MapGet("/alerts", async (
            HttpContext context,
            SessionService sessions,
            ProductStore store,
            CancellationToken ct) =>
        {
            var user = await sessions.GetOrCreateAsync(context, ct);
            return Results.Ok(await store.GetAlertsAsync(user.Id, 50, ct));
        });

        api.MapPost("/alerts/{id:long}/read", async Task<IResult> (
            long id,
            HttpContext context,
            SessionService sessions,
            ProductStore store,
            TimeProvider clock,
            CancellationToken ct) =>
        {
            var user = await sessions.GetOrCreateAsync(context, ct);
            return await store.MarkAlertReadAsync(user.Id, id, clock.GetUtcNow(), ct) ? Results.NoContent() : Results.NotFound();
        });

        api.MapPost("/alerts/read-all", async (
            HttpContext context,
            SessionService sessions,
            ProductStore store,
            TimeProvider clock,
            CancellationToken ct) =>
        {
            var user = await sessions.GetOrCreateAsync(context, ct);
            await store.MarkAllAlertsReadAsync(user.Id, clock.GetUtcNow(), ct);
            return Results.NoContent();
        });

        api.MapGet("/dashboard", async (
            HttpContext context,
            SessionService sessions,
            EntitlementService entitlements,
            ProductStore store,
            CancellationToken ct) =>
        {
            var user = await sessions.GetOrCreateAsync(context, ct);
            var account = await entitlements.GetAccountAsync(user.Id, ct);
            var watchlist = await store.GetWatchlistAsync(user.Id, ct);
            var alerts = await store.GetAlertsAsync(user.Id, 20, ct);
            return Results.Ok(new DashboardView(account, watchlist, alerts, alerts.Count(a => a.ReadAt is null)));
        });

        api.MapGet("/master-sets", async (
            HttpContext context,
            SessionService sessions,
            MasterSetStore masterSets,
            CancellationToken ct) =>
        {
            var user = await sessions.GetOrCreateAsync(context, ct);
            return Results.Ok(await masterSets.GetSummariesAsync(user.Id, ct));
        });

        api.MapPost("/master-sets", async Task<IResult> (
            CreateMasterSetRequest request,
            HttpContext context,
            SessionService sessions,
            EntitlementService entitlements,
            MasterSetStore masterSets,
            MasterSetService service,
            CancellationToken ct) =>
        {
            var user = await sessions.GetOrCreateAsync(context, ct);
            var account = await entitlements.GetAccountAsync(user.Id, ct);
            if (!account.IsPro && await masterSets.CountAsync(user.Id, ct) >= 1)
                return Results.Problem(statusCode: 403, title: "Free master-set limit reached", detail: "Pro unlocks unlimited master sets.");

            var id = await service.CreateAsync(user.Id, request.SetId, ct);
            return id is { } value ? Results.Ok(new { id = value }) : Results.NotFound();
        });

        api.MapGet("/master-sets/{id:long}", async Task<IResult> (
            long id,
            HttpContext context,
            SessionService sessions,
            MasterSetStore masterSets,
            CancellationToken ct) =>
        {
            var user = await sessions.GetOrCreateAsync(context, ct);
            return await masterSets.GetDetailAsync(user.Id, id, ct) is { } value
                ? Results.Ok(value)
                : Results.NotFound();
        });

        api.MapPut("/master-sets/{id:long}/items", async Task<IResult> (
            long id,
            UpdateMasterSetItemRequest request,
            HttpContext context,
            SessionService sessions,
            MasterSetStore masterSets,
            TimeProvider clock,
            CancellationToken ct) =>
        {
            if (request.OwnedQuantity < 0)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["ownedQuantity"] = ["Owned quantity cannot be negative."] });
            if (request.AcquiredPrice < 0)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["acquiredPrice"] = ["Acquired price cannot be negative."] });
            var user = await sessions.GetOrCreateAsync(context, ct);
            return await masterSets.UpdateItemAsync(user.Id, id, request, clock.GetUtcNow(), ct)
                ? Results.NoContent()
                : Results.NotFound();
        });

        api.MapDelete("/master-sets/{id:long}", async Task<IResult> (
            long id,
            HttpContext context,
            SessionService sessions,
            MasterSetStore masterSets,
            CancellationToken ct) =>
        {
            var user = await sessions.GetOrCreateAsync(context, ct);
            return await masterSets.DeleteAsync(user.Id, id, ct) ? Results.NoContent() : Results.NotFound();
        });

        api.MapGet("/master-sets/{id:long}/advisor-context", async Task<IResult> (
            long id,
            HttpContext context,
            SessionService sessions,
            EntitlementService entitlements,
            MasterSetService service,
            CancellationToken ct) =>
        {
            var user = await sessions.GetOrCreateAsync(context, ct);
            var account = await entitlements.GetAccountAsync(user.Id, ct);
            if (!account.IsPro) return ProRequired();
            return await service.GetAdvisorContextAsync(user.Id, id, ct) is { } value
                ? Results.Ok(value)
                : Results.NotFound();
        });

        api.MapPost("/billing/checkout", async Task<IResult> (
            CheckoutRequest request,
            HttpContext context,
            SessionService sessions,
            EntitlementService entitlements,
            ProductStore store,
            StripeBillingService stripe,
            CancellationToken ct) =>
        {
            var user = await sessions.GetOrCreateAsync(context, ct);
            var account = await entitlements.GetAccountAsync(user.Id, ct);
            if (!account.BillingConfigured)
                return Results.Problem(statusCode: 503, title: "Billing is in preview mode", detail: "Stripe keys and Price IDs have not been configured yet.");

            if (request.Plan is not ("monthly" or "annual"))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["plan"] = ["Choose monthly or annual."] });

            var subscription = await store.GetSubscriptionAsync(user.Id, ct);
            var url = await stripe.CreateCheckoutAsync(user.Id, request.Plan, subscription?.CustomerId, ct);
            return Results.Ok(new BillingLink(url));
        });

        api.MapPost("/billing/portal", async Task<IResult> (
            HttpContext context,
            SessionService sessions,
            ProductStore store,
            BillingOptions options,
            StripeBillingService stripe,
            CancellationToken ct) =>
        {
            if (!options.IsConfigured)
                return Results.Problem(statusCode: 503, title: "Billing is in preview mode");
            var user = await sessions.GetOrCreateAsync(context, ct);
            var subscription = await store.GetSubscriptionAsync(user.Id, ct);
            if (string.IsNullOrWhiteSpace(subscription?.CustomerId))
                return Results.Problem(statusCode: 400, title: "No billing account exists yet.");
            return Results.Ok(new BillingLink(await stripe.CreatePortalAsync(subscription.CustomerId, ct)));
        });

        api.MapPost("/billing/webhook", async Task<IResult> (
            HttpRequest request,
            StripeBillingService stripe,
            CancellationToken ct) =>
            await stripe.ProcessWebhookAsync(request, ct) ? Results.Ok() : Results.BadRequest());
    }

    private static IResult ProRequired() =>
        Results.Problem(statusCode: 403, title: "TCG Signal Pro required", detail: "Upgrade to unlock Market Intelligence.");

    private static string? ValidateThresholds(decimal? below, decimal? above, decimal? move)
    {
        if (below < 0 || above < 0) return "Price targets cannot be negative.";
        if (move is < 1 or > 500) return "Move threshold must be between 1% and 500%.";
        if (below is not null && above is not null && below >= above) return "Below target must be less than above target.";
        return null;
    }
}
