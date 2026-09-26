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
public record RealizedAccuracy(DateOnly AsOf, int HorizonDays, int Count, double TypicalErrorPercent, double NoChangeErrorPercent, double? DirectionAccuracyPercent);

public record ModelSummary(
    PredictionStatus Status,
    DateOnly? AsOf,
    DateTimeOffset? TrainedAt,
    int HorizonDays,
    int TrainingRows,
    int ValidationRows,
    int HistoryDays,
    int ApproxHistoryDaysNeeded,
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

        api.MapGet("/predictions", async (string? direction, int? limit, decimal? minPrice, AppDbContext db, PredictionOptions options, CancellationToken ct) =>
        {
            var run = await CurrentRunAsync(db, ct);
            var take = Math.Clamp(limit ?? 12, 1, 50);
            var min = minPrice ?? 2m;

            if (run is not { Status: PredictionStatus.Published })
            {
                // A brand-new deployment cannot honestly train a 30-day model on one or two daily snapshots.
                // Keep the page useful without inventing model accuracy: show an explicitly labeled early signal
                // from observed price momentum (or, until a second day exists, listing-midpoint pressure).
                return await PreviewAsync(direction, take, min, options.HorizonDays, db, ct);
            }

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

        api.MapGet("/predictions/model", async (AppDbContext db, PredictionOptions options, CancellationToken ct) =>
        {
            var run = await CurrentRunAsync(db, ct);
            var earliest = await db.PriceSnapshots.MinAsync(s => (DateOnly?)s.Date, ct);
            var latest = await db.PriceSnapshots.MaxAsync(s => (DateOnly?)s.Date, ct);
            var historyDays = earliest is null || latest is null ? 0 : latest.Value.DayNumber - earliest.Value.DayNumber + 1;
            var historyNeeded = 2 * options.HorizonDays + options.SampleEveryDays * 2;
            var status = run is { Status: PredictionStatus.Published }
                ? PredictionStatus.Published
                : latest is not null ? PredictionStatus.Preview : run?.Status ?? PredictionStatus.InsufficientHistory;

            var track = await db.PredictionRuns.AsNoTracking()
                .Where(r => r.Checkpoint && r.RealizedCount > 0 && r.AsOf != null)
                .OrderByDescending(r => r.AsOf)
                .Take(12)
                .ToListAsync(ct);

            var message = run is { Status: PredictionStatus.Published }
                ? run.Message
                : latest is null
                    ? "Waiting for the first daily price snapshot."
                    : $"Early market signals are live while the validated {options.HorizonDays}-day model builds enough history ({historyDays} of about {historyNeeded} days collected).";

            return new ModelSummary(
                status,
                run?.AsOf ?? latest,
                run?.FinishedAt,
                run?.HorizonDays > 0 ? run.HorizonDays : options.HorizonDays,
                run?.TrainingRows ?? 0,
                run?.ValidationRows ?? 0,
                historyDays,
                historyNeeded,
                TypicalMiss(run?.Mae),
                TypicalMiss(run?.BaselineMae),
                AsPercent(run?.DirectionAccuracy),
                run?.ResidualLow is { } low ? Math.Round((Math.Exp(low) - 1) * 100, 1) : null,
                run?.ResidualHigh is { } high ? Math.Round((Math.Exp(high) - 1) * 100, 1) : null,
                Importance(run?.Importance),
                track.Select(r => new RealizedAccuracy(
                    r.AsOf!.Value, r.HorizonDays, r.RealizedCount!.Value, TypicalMiss(r.RealizedMae) ?? 0, TypicalMiss(r.RealizedBaselineMae) ?? 0,
                    AsPercent(r.RealizedDirectionAccuracy))).ToList(),
                message);
        });

        app.MapPost("/internal/predictions", async Task<IResult> (PredictionService predictions, CancellationToken ct) =>
            await predictions.RunAsync(ct) is { } result
                ? Results.Ok(result)
                : Results.Conflict(new { message = "A prediction run is already in progress." }));
    }


    /// <summary>
    /// Returns transparent early signals before a validated model can exist. Recent observed market movement is
    /// preferred; with only one daily snapshot, the TCGplayer listing midpoint versus market is used as a weaker
    /// pressure signal. These rows are deliberately marked Preview so the UI never presents them as model output.
    /// </summary>
    private static async Task<PredictionList> PreviewAsync(
        string? direction,
        int take,
        decimal minPrice,
        int horizonDays,
        AppDbContext db,
        CancellationToken ct)
    {
        var asOf = await db.PriceSnapshots.MaxAsync(s => (DateOnly?)s.Date, ct);
        if (asOf is null) return new PredictionList(PredictionStatus.InsufficientHistory, null, horizonDays, []);

        var from = asOf.Value.AddDays(-14);
        var rows = await db.Database.SqlQuery<PreviewRow>($"""
            WITH current_prices AS (
                SELECT s.card_id, s.variant, s.date, s.market, s.low, s.mid, s.high,
                       c.name, c.number, c.set_id, c.set_name, c.image_small, c.tcgplayer_url
                FROM price_snapshots s
                JOIN cards c ON c.id = s.card_id
                WHERE s.date = {asOf.Value} AND s.market >= {minPrice}
            )
            SELECT cur.card_id AS "CardId", cur.variant AS "Variant", cur.date AS "AsOf",
                   cur.market AS "Current", cur.low AS "Low", cur.mid AS "Mid", cur.high AS "High",
                   cur.name AS "Name", cur.number AS "Number", cur.set_id AS "SetId", cur.set_name AS "SetName",
                   cur.image_small AS "ImageUrl", cur.tcgplayer_url AS "TcgplayerUrl",
                   hist.date AS "ReferenceDate", hist.market AS "ReferenceMarket"
            FROM current_prices cur
            LEFT JOIN LATERAL (
                SELECT h.date, h.market
                FROM price_snapshots h
                WHERE h.card_id = cur.card_id
                  AND h.variant = cur.variant
                  AND h.date < cur.date
                  AND h.date >= {from}
                  AND h.market > 0
                ORDER BY h.date ASC
                LIMIT 1
            ) hist ON TRUE
            """).ToListAsync(ct);

        double Signal(PreviewRow row)
        {
            if (row.ReferenceMarket is > 0 && row.ReferenceDate is not null && row.ReferenceDate < row.AsOf)
                return Math.Round(((double)(row.Current / row.ReferenceMarket.Value) - 1d) * 100d, 1);

            if (row.Mid is > 0 && row.Current > 0)
                return Math.Round(((double)(row.Mid.Value / row.Current) - 1d) * 100d, 1);

            return 0d;
        }

        var scored = rows
            .Select(row => new { Row = row, Change = Signal(row) })
            .Where(x => direction == "down" ? x.Change <= -1d : x.Change >= 1d);

        scored = direction == "down"
            ? scored.OrderBy(x => x.Change)
            : scored.OrderByDescending(x => x.Change);

        var cards = scored
            .DistinctBy(x => x.Row.CardId)
            .Take(take)
            .Select(x =>
            {
                var row = x.Row;
                var hasHistory = row.ReferenceMarket is > 0 && row.ReferenceDate is not null && row.ReferenceDate < row.AsOf;
                var reason = hasHistory
                    ? $"Market price moved {Math.Abs(x.Change):0.0}% {(x.Change >= 0 ? "up" : "down")} since {row.ReferenceDate:MMM d}."
                    : $"TCGplayer listing midpoint is {Math.Abs(x.Change):0.0}% {(x.Change >= 0 ? "above" : "below")} market.";
                return new CardPrediction(
                    row.CardId, row.Name, row.Number, row.SetId, row.SetName, row.ImageUrl, row.TcgplayerUrl,
                    row.Variant, Prices.VariantLabel(row.Variant),
                    row.Current, row.Current, row.Low ?? row.Current, row.High ?? row.Current, x.Change,
                    [new PredictionReason(hasHistory ? "recentMomentum" : "listingPressure", reason, x.Change)]);
            })
            .ToList();

        return new PredictionList(PredictionStatus.Preview, asOf, horizonDays, cards);
    }

    private sealed class PreviewRow
    {
        public string CardId { get; init; } = "";
        public string Variant { get; init; } = "";
        public DateOnly AsOf { get; init; }
        public decimal Current { get; init; }
        public decimal? Low { get; init; }
        public decimal? Mid { get; init; }
        public decimal? High { get; init; }
        public string Name { get; init; } = "";
        public string Number { get; init; } = "";
        public string SetId { get; init; } = "";
        public string SetName { get; init; } = "";
        public string? ImageUrl { get; init; }
        public string? TcgplayerUrl { get; init; }
        public DateOnly? ReferenceDate { get; init; }
        public decimal? ReferenceMarket { get; init; }
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
