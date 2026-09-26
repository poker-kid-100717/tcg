using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PokemonTCG.API.Data;

namespace PokemonTCG.API.Pricing.Predictions;

public record CardPrediction(
    string CardId,
    string Name,
    string Number,
    string SetId,
    string SetName,
    string? ImageUrl,
    string? TcgplayerUrl,
    string Variant,
    string VariantLabel,
    decimal Current,
    decimal Predicted,
    decimal Low,
    decimal High,
    double ChangePercent,
    IReadOnlyList<PredictionReason> Reasons);

/// <summary>Predictions from the current model. Empty unless its status is Published.</summary>
public record PredictionList(PredictionStatus Status, DateOnly? AsOf, int HorizonDays, IReadOnlyList<CardPrediction> Cards);

public record FeatureWeight(string Feature, string Label, double Weight);

/// <summary>How one week's published predictions did once their horizon passed. Errors are typical % misses.</summary>
public record RealizedAccuracy(DateOnly AsOf, int Count, double TypicalErrorPercent, double NoChangeErrorPercent, double? DirectionAccuracyPercent);

public record ModelSummary(
    PredictionStatus Status,
    DateOnly? AsOf,
    DateTimeOffset? TrainedAt,
    int HorizonDays,
    int TrainingRows,
    int ValidationRows,
    double? TypicalErrorPercent,
    double? NoChangeErrorPercent,
    double? DirectionAccuracyPercent,
    double? RangeLowPercent,
    double? RangeHighPercent,
    IReadOnlyList<FeatureWeight> Importance,
    IReadOnlyList<RealizedAccuracy> TrackRecord,
    string? Message);

public static class PredictionEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static void MapPredictions(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/predictions", async (string? direction, int? limit, decimal? minPrice, AppDbContext db, CancellationToken ct) =>
        {
            var run = await CurrentRunAsync(db, ct);
            if (run is not { Status: PredictionStatus.Published })
            {
                return new PredictionList(run?.Status ?? PredictionStatus.InsufficientHistory, run?.AsOf, run?.HorizonDays ?? 0, []);
            }

            var take = Math.Clamp(limit ?? 12, 1, 50);
            var min = minPrice ?? 2m;
            var query = db.PricePredictions.AsNoTracking().Where(p => p.RunId == run.Id && p.Current >= min);
            query = direction == "down"
                ? query.Where(p => p.ChangePercent < 0).OrderBy(p => p.ChangePercent)
                : query.Where(p => p.ChangePercent > 0).OrderByDescending(p => p.ChangePercent);
            // A card's printings are often predicted together; fetch extra and keep each card's strongest.
            var rows = await (from p in query.Take(take * 4)
                              join c in db.Cards.AsNoTracking() on p.CardId equals c.Id
                              select new { p, c }).ToListAsync(ct);
            var ordered = direction == "down" ? rows.OrderBy(r => r.p.ChangePercent) : rows.OrderByDescending(r => r.p.ChangePercent);
            return new PredictionList(run.Status, run.AsOf, run.HorizonDays,
                ordered.DistinctBy(r => r.c.Id).Take(take).Select(r => ToCard(r.p, r.c)).ToList());
        });

        api.MapGet("/cards/{id}/predictions", async (string id, AppDbContext db, CancellationToken ct) =>
        {
            var run = await CurrentRunAsync(db, ct);
            if (run is not { Status: PredictionStatus.Published })
            {
                return new PredictionList(run?.Status ?? PredictionStatus.InsufficientHistory, run?.AsOf, run?.HorizonDays ?? 0, []);
            }
            var rows = await (from p in db.PricePredictions.AsNoTracking()
                              where p.RunId == run.Id && p.CardId == id
                              join c in db.Cards.AsNoTracking() on p.CardId equals c.Id
                              select new { p, c }).ToListAsync(ct);
            return new PredictionList(run.Status, run.AsOf, run.HorizonDays,
                rows.OrderBy(r => Prices.VariantOrder(r.p.Variant)).Select(r => ToCard(r.p, r.c)).ToList());
        });

        api.MapGet("/predictions/model", async (AppDbContext db, CancellationToken ct) =>
        {
            var run = await CurrentRunAsync(db, ct);
            var track = await db.PredictionRuns.AsNoTracking()
                .Where(r => r.Checkpoint && r.RealizedCount > 0 && r.AsOf != null)
                .OrderByDescending(r => r.AsOf)
                .Take(12)
                .ToListAsync(ct);

            return new ModelSummary(
                run?.Status ?? PredictionStatus.InsufficientHistory,
                run?.AsOf,
                run?.FinishedAt,
                run?.HorizonDays ?? 0,
                run?.TrainingRows ?? 0,
                run?.ValidationRows ?? 0,
                TypicalMiss(run?.Mae),
                TypicalMiss(run?.BaselineMae),
                AsPercent(run?.DirectionAccuracy),
                run?.ResidualLow is { } low ? Math.Round((Math.Exp(low) - 1) * 100, 1) : null,
                run?.ResidualHigh is { } high ? Math.Round((Math.Exp(high) - 1) * 100, 1) : null,
                Importance(run?.Importance),
                track.Select(r => new RealizedAccuracy(
                    r.AsOf!.Value, r.RealizedCount!.Value, TypicalMiss(r.RealizedMae) ?? 0, TypicalMiss(r.RealizedBaselineMae) ?? 0,
                    AsPercent(r.RealizedDirectionAccuracy))).ToList(),
                run?.Message ?? (run is null ? "The model hasn't run yet." : null));
        });

        app.MapPost("/internal/predictions", async Task<IResult> (PredictionService predictions, CancellationToken ct) =>
            await predictions.RunAsync(ct) is { } result
                ? Results.Ok(result)
                : Results.Conflict(new { message = "A prediction run is already in progress." }));
    }

    /// <summary>The latest run that finished (a failed or skipped run keeps the previous model's answer showing).</summary>
    private static Task<PredictionRun?> CurrentRunAsync(AppDbContext db, CancellationToken ct) =>
        db.PredictionRuns.AsNoTracking()
            .Where(r => r.FinishedAt != null && r.Status != PredictionStatus.Failed && r.Status != PredictionStatus.Running && r.Status != PredictionStatus.Skipped)
            .OrderByDescending(r => r.Id)
            .FirstOrDefaultAsync(ct);

    private static CardPrediction ToCard(PricePrediction p, Card c) => new(
        c.Id, c.Name, c.Number, c.SetId, c.SetName, c.ImageSmall, c.TcgplayerUrl, p.Variant, Prices.VariantLabel(p.Variant),
        p.Current, p.Predicted, p.Low, p.High, p.ChangePercent,
        JsonSerializer.Deserialize<List<PredictionReason>>(p.Reasons, Json) ?? []);

    /// <summary>A mean absolute log error as the typical % a prediction misses by.</summary>
    private static double? TypicalMiss(double? logError) => logError is { } e ? Math.Round((Math.Exp(e) - 1) * 100, 1) : null;

    private static double? AsPercent(double? share) => share is { } s ? Math.Round(s * 100, 1) : null;

    private static IReadOnlyList<FeatureWeight> Importance(string? json)
    {
        if (json is null) return [];
        var labels = PriceFeatures.All.ToDictionary(f => f.Name, f => f.Label);
        return (JsonSerializer.Deserialize<List<ImportanceEntry>>(json, Json) ?? [])
            .Where(e => e.Weight > 0)
            .Select(e => new FeatureWeight(e.Feature, labels.GetValueOrDefault(e.Feature, e.Feature), Math.Round(e.Weight * 100, 1)))
            .ToList();
    }

    private sealed record ImportanceEntry(string Feature, double Weight);
}
