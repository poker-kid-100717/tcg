using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using PokemonTCG.API.Data;
using PokemonTCG.API.Product;

namespace PokemonTCG.API.Pricing;

public static class PriceGuideEndpoints
{
    public static void MapPriceGuide(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/sets", (PriceGuideService guide, CancellationToken ct) => guide.GetSetsAsync(ct));

        api.MapGet("/sets/{id}", async Task<IResult> (string id, PriceGuideService guide, CancellationToken ct) =>
            await guide.GetSetAsync(id, ct) is { } set ? Results.Ok(set) : Results.NotFound());

        api.MapGet("/cards/{id}", async Task<IResult> (string id, PriceGuideService guide, CancellationToken ct) =>
            await guide.GetCardAsync(id, ct) is { } card ? Results.Ok(card) : Results.NotFound());

        api.MapGet("/cards", async Task<IResult> (string? q, int? page, int? pageSize, PriceGuideService guide, CancellationToken ct) =>
        {
            var query = q?.Trim() ?? "";
            if (query.Length < 2)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["q"] = ["Enter at least 2 characters."] });
            }
            return Results.Ok(await guide.SearchAsync(query, Math.Max(page ?? 1, 1), Math.Clamp(pageSize ?? 24, 1, 60), ct));
        });

        api.MapGet("/market/movers", (int? days, int? limit, decimal? minPrice, PriceGuideService guide, CancellationToken ct) =>
            guide.GetMoversAsync(Math.Clamp(days ?? 7, 1, 365), Math.Clamp(limit ?? 10, 1, 50), minPrice ?? 2m, ct));

        api.MapGet("/market/top", (int? limit, PriceGuideService guide, CancellationToken ct) =>
            guide.GetMostValuableAsync(Math.Clamp(limit ?? 12, 1, 50), ct));

        api.MapGet("/market/downtrend", (int? days, int? limit, decimal? minPrice, PriceGuideService guide, CancellationToken ct) =>
            guide.GetDownTrendingAsync(Math.Clamp(days ?? 30, 7, 365), Math.Clamp(limit ?? 12, 1, 50), minPrice ?? 2m, ct));

        api.MapGet("/market/sleepers", (int? limit, decimal? minPrice, PriceGuideService guide, CancellationToken ct) =>
            guide.GetSleepersAsync(Math.Clamp(limit ?? 12, 1, 50), minPrice ?? 2m, 0.10m, ct));

        api.MapGet("/market/status", async (AppDbContext db, CancellationToken ct) =>
        {
            var lastRun = await db.SnapshotRuns.AsNoTracking()
                .Where(r => r.Status == SnapshotStatus.Succeeded)
                .OrderByDescending(r => r.FinishedAt)
                .Select(r => new { r.FinishedAt, r.CardsSeen, r.PricesWritten })
                .FirstOrDefaultAsync(ct);
            var days = await db.PriceSnapshots.Select(s => s.Date).Distinct().CountAsync(ct);
            return new { LastSnapshotAt = lastRun?.FinishedAt, lastRun?.CardsSeen, lastRun?.PricesWritten, DaysOfHistory = days };
        });

        // Not under /api: the Worker only forwards /api and /health from the
        // internet, so this is reachable from the Worker's Cron Trigger (and
        // locally), never from a browser.
        app.MapPost("/internal/snapshots", async Task<IResult> (
            PriceSnapshotService snapshots,
            AlertEvaluationService alerts,
            CancellationToken ct) =>
        {
            var result = await snapshots.RunAsync(ct);
            if (result is null) return Results.Conflict(new { message = "A snapshot run is already in progress." });
            if (result.Status == SnapshotStatus.Succeeded) await alerts.EvaluateAsync(ct);
            return Results.Ok(result);
        });
    }
}

/// <summary>When the upstream card API is down, answer 502 with a clear message instead of a 500.</summary>
public class UpstreamUnavailableHandler(IProblemDetailsService problems, ILogger<UpstreamUnavailableHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not (HttpRequestException or TaskCanceledException { InnerException: TimeoutException })) return false;

        logger.LogWarning(exception, "Pokémon TCG API request failed");
        context.Response.StatusCode = StatusCodes.Status502BadGateway;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails =
            {
                Status = StatusCodes.Status502BadGateway,
                Title = "The card database isn't responding",
                Detail = "Card and price data come from the Pokémon TCG API, which didn't answer. Try again in a minute.",
            },
            Exception = exception,
        });
    }
}
