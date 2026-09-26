using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PokemonTCG.API.Data;

namespace PokemonTCG.API.Pricing.Predictions;

public record PredictionReason(string Feature, string Text, double EffectPercent);

public record PredictionRunResult(int RunId, PredictionStatus Status, int TrainingRows, int ValidationRows, double? Mae, double? BaselineMae, int Predictions, string? Message);

/// <summary>
/// Trains the price model on the recorded history, checks it on the most recent weeks it didn't see, and,
/// if it beats simply predicting "no change", stores a prediction for every printing in the latest snapshot.
/// Runs daily after the price snapshot (a Cron Trigger calls <c>/internal/predictions</c>).
/// </summary>
public class PredictionService(AppDbContext db, PredictionOptions options, TimeProvider clock, ILogger<PredictionService> logger)
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>A card's outcome is its first price on or up to this many days after the horizon date.</summary>
    private const int OutcomeGraceDays = 5;

    public async Task<PredictionRunResult?> RunAsync(CancellationToken cancellationToken)
    {
        if (!await Gate.WaitAsync(0, cancellationToken)) return null;
        try
        {
            return await RunExclusiveAsync(cancellationToken);
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task<PredictionRunResult> RunExclusiveAsync(CancellationToken cancellationToken)
    {
        var run = new PredictionRun { StartedAt = clock.GetUtcNow(), Status = PredictionStatus.Running, HorizonDays = options.HorizonDays };
        db.PredictionRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            await TrainAndPredictAsync(run, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Prediction run {RunId} failed", run.Id);
            run.Status = PredictionStatus.Failed;
            run.Message = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
        }

        run.FinishedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(CancellationToken.None);
        logger.LogInformation("Prediction run {RunId} {Status}: trained on {Train}, validated on {Validation}, MAE {Mae} vs {Baseline}",
            run.Id, run.Status, run.TrainingRows, run.ValidationRows, run.Mae, run.BaselineMae);
        return new PredictionRunResult(run.Id, run.Status, run.TrainingRows, run.ValidationRows, run.Mae, run.BaselineMae, run.Predictions, run.Message);
    }

    private async Task TrainAndPredictAsync(PredictionRun run, CancellationToken cancellationToken)
    {
        var horizon = options.HorizonDays;

        // The snapshot writes page by page, so while it's running (or after it failed part-way) the newest day is
        // incomplete. Training on it would predict for only some cards and skew each day's premiums and set ranks.
        var lastSnapshot = await db.SnapshotRuns.AsNoTracking().OrderByDescending(r => r.Id).FirstOrDefaultAsync(cancellationToken);
        if (lastSnapshot is not null && lastSnapshot.Status != SnapshotStatus.Succeeded)
        {
            run.Status = PredictionStatus.Skipped;
            run.Message = lastSnapshot.Status == SnapshotStatus.Running
                ? "Today's price snapshot is still running; the current predictions stay until it finishes."
                : "Today's price snapshot failed; the current predictions stay until a snapshot succeeds.";
            return;
        }

        var latest = await db.PriceSnapshots.MaxAsync(s => (DateOnly?)s.Date, cancellationToken);
        var earliest = await db.PriceSnapshots.MinAsync(s => (DateOnly?)s.Date, cancellationToken);
        if (latest is null || earliest is null)
        {
            run.Status = PredictionStatus.InsufficientHistory;
            run.Message = "No prices have been recorded yet.";
            return;
        }
        run.AsOf = latest;

        await ScorePastRunsAsync(latest.Value, cancellationToken);

        // Sample every few days back from the latest snapshot. Training rows need a known price `horizon`
        // days later; validation uses the most recent of those dates, and training only dates whose
        // outcome was already known before validation starts, so no outcome leaks across the split.
        var dates = new List<DateOnly>();
        for (var d = latest.Value; d >= earliest.Value; d = d.AddDays(-options.SampleEveryDays)) dates.Add(d);
        dates.Reverse();
        // A date is labelled only once its whole outcome window (horizon + grace days) has passed, so printings
        // that get their outcome price during the grace days aren't missing from the newest validation dates.
        var labelled = dates.Where(d => d.AddDays(horizon + OutcomeGraceDays) <= latest.Value).ToList();
        var validationDates = labelled.TakeLast(Math.Max(1, (int)Math.Ceiling(labelled.Count * options.ValidationFraction))).ToHashSet();
        var validationStart = validationDates.Count > 0 ? validationDates.Min() : latest.Value;
        // A label can come from up to OutcomeGraceDays after the horizon (when a card has no price on the exact day),
        // so a training date's whole outcome window has to close before validation starts.
        var trainingDates = labelled.Where(d => d.AddDays(horizon + OutcomeGraceDays) <= validationStart).ToHashSet();

        // Cap rows per day in the database, so memory stays bounded however much history there is. The latest day
        // is never capped: every printing on it gets a prediction.
        var perDay = Math.Max(1, options.MaxTrainingRows / Math.Max(1, labelled.Count));
        var samples = await LoadSamplesAsync(dates.ToArray(), latest.Value, perDay, cancellationToken);
        var species = await LoadSpeciesAsync(cancellationToken);
        var premiums = samples.GroupBy(s => s.Date).ToDictionary(g => g.Key, g => PriceFeatures.ComputePremiums(g));
        ModelInput Input(SampleRow s) => new()
        {
            Features = PriceFeatures.Vector(s, premiums[s.Date], species),
            Label = (float)Math.Clamp(s.Label ?? 0, -options.LabelClip, options.LabelClip),
        };

        var training = samples.Where(s => trainingDates.Contains(s.Date) && s.Label is not null).ToList();
        var validation = samples.Where(s => validationDates.Contains(s.Date) && s.Label is not null).ToList();
        run.TrainingRows = training.Count;
        run.ValidationRows = validation.Count;

        if (training.Count < options.MinTrainingRows || validation.Count < options.MinValidationRows)
        {
            var days = latest.Value.DayNumber - earliest.Value.DayNumber + 1;
            var needed = 2 * horizon + options.SampleEveryDays * 2;
            run.Status = PredictionStatus.InsufficientHistory;
            run.Message = days < needed
                ? $"Needs about {needed} days of prices to train and test a {horizon}-day model; {days} recorded so far."
                : $"Not enough priced cards to train on yet ({training.Count} training and {validation.Count} validation rows).";
            return;
        }

        // 1. Train on the older dates and measure on the held-out recent ones.
        var trial = PriceModel.Train(training.Select(Input).ToList(), options);
        var validationInputs = validation.Select(Input).ToList();
        var scores = trial.Predict(validationInputs).Select(o => (double)o.Score).ToArray();
        // Clipping keeps glitches out of the training loss, but the model is judged on what actually happened.
        var actual = validation.Select(s => s.Label!.Value).ToArray();
        run.Mae = actual.Zip(scores, (a, p) => Math.Abs(a - p)).Average();
        run.BaselineMae = actual.Average(Math.Abs);
        run.DirectionAccuracy = DirectionAccuracy(actual, scores);
        var residuals = actual.Zip(scores, (a, p) => a - p).Order().ToArray();
        run.ResidualLow = Quantile(residuals, 0.1);
        run.ResidualHigh = Quantile(residuals, 0.9);
        run.Importance = ImportanceJson(trial.Importance());

        if (run.Mae >= run.BaselineMae * (1 - options.MinImprovement))
        {
            run.Status = PredictionStatus.Withheld;
            run.Message = "The model didn't beat predicting no change on the held-out weeks, so no predictions are shown.";
            return;
        }

        // 2. It works: retrain on everything with a known outcome and predict from the latest prices.
        var final = PriceModel.Train(samples.Where(s => s.Label is not null && labelled.Contains(s.Date)).Select(Input).ToList(), options);
        run.Importance = ImportanceJson(final.Importance());
        var current = samples.Where(s => s.Date == latest.Value).ToList();
        var outputs = final.Predict(current.Select(Input).ToList());

        var predictions = current.Zip(outputs, (s, o) => ToPrediction(run, s, o, premiums[s.Date], species)).ToList();
        run.Checkpoint = !await db.PredictionRuns.AnyAsync(
            r => r.Checkpoint && r.Status == PredictionStatus.Published && r.FinishedAt != null && r.AsOf > latest.Value.AddDays(-7),
            cancellationToken);
        db.PricePredictions.AddRange(predictions);
        run.Predictions = predictions.Count;
        // Published and finished in one save: a run is never left published but unfinished (and so never served).
        run.Status = PredictionStatus.Published;
        run.FinishedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);

        // Older runs' predictions aren't shown any more; keep only the weekly checkpoints still waiting to be scored.
        await db.PricePredictions
            .Where(p => p.RunId != run.Id && db.PredictionRuns.Any(r => r.Id == p.RunId && !r.Checkpoint))
            .ExecuteDeleteAsync(cancellationToken);
    }

    private PricePrediction ToPrediction(PredictionRun run, SampleRow row, ModelOutput output, Premiums premiums, IReadOnlyDictionary<int, Species> species)
    {
        var vector = PriceFeatures.Vector(row, premiums, species);
        var reasons = output.FeatureContributions
            .Select((c, i) => (Contribution: c, Feature: i))
            .Where(c => Math.Abs(c.Contribution) >= 0.002)
            .OrderByDescending(c => Math.Abs(c.Contribution))
            .Take(3)
            .Select(c => new PredictionReason(
                PriceFeatures.All[c.Feature].Name,
                PriceFeatures.Describe(c.Feature, row, vector[c.Feature], species),
                Math.Round((Math.Exp(c.Contribution) - 1) * 100, 1)))
            .ToList();

        var market = (double)row.Market;
        decimal Price(double log) => Math.Round((decimal)(market * Math.Exp(log)), 2);
        return new PricePrediction
        {
            RunId = run.Id,
            CardId = row.CardId,
            Variant = row.Variant,
            Current = row.Market,
            Predicted = Price(output.Score),
            Low = Price(output.Score + (run.ResidualLow ?? 0)),
            High = Price(output.Score + (run.ResidualHigh ?? 0)),
            ChangePercent = Math.Round((Math.Exp(output.Score) - 1) * 100, 1),
            Reasons = JsonSerializer.Serialize(reasons, Json),
        };
    }

    /// <summary>Scores checkpoint runs whose horizon has passed against the prices that actually happened.</summary>
    private async Task ScorePastRunsAsync(DateOnly latest, CancellationToken cancellationToken)
    {
        var due = await db.PredictionRuns
            .Where(r => r.Checkpoint && r.RealizedCount == null && r.AsOf != null)
            .ToListAsync(cancellationToken);

        // Wait until the whole outcome window has passed, so a card missing a price on the exact day still counts.
        foreach (var past in due.Where(r => r.AsOf!.Value.AddDays(r.HorizonDays + OutcomeGraceDays) <= latest))
        {
            var target = past.AsOf!.Value.AddDays(past.HorizonDays);
            var until = target.AddDays(OutcomeGraceDays);
            var outcomes = await db.Database.SqlQuery<Outcome>($"""
                SELECT p.current AS "Current", p.predicted AS "Predicted", f.market AS "Actual"
                FROM price_predictions p
                JOIN LATERAL (
                    SELECT s.market FROM price_snapshots s
                    WHERE s.card_id = p.card_id AND s.variant = p.variant AND s.date >= {target} AND s.date < {until} AND s.market > 0
                    ORDER BY s.date LIMIT 1
                ) f ON true
                WHERE p.run_id = {past.Id} AND p.current > 0
                """).ToListAsync(cancellationToken);

            var actual = outcomes.Select(o => Math.Log((double)(o.Actual / o.Current))).ToArray();
            var predicted = outcomes.Select(o => Math.Log((double)(o.Predicted / o.Current))).ToArray();
            past.RealizedCount = outcomes.Count;
            if (outcomes.Count > 0)
            {
                past.RealizedMae = actual.Zip(predicted, (a, p) => Math.Abs(a - p)).Average();
                past.RealizedBaselineMae = actual.Average(Math.Abs);
                past.RealizedDirectionAccuracy = DirectionAccuracy(actual, predicted);
            }
            await db.PricePredictions.Where(p => p.RunId == past.Id).ExecuteDeleteAsync(cancellationToken);
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private sealed class Outcome
    {
        public decimal Current { get; init; }
        public decimal Predicted { get; init; }
        public decimal Actual { get; init; }
    }

    private Task<List<SampleRow>> LoadSamplesAsync(DateOnly[] dates, DateOnly latest, int perDay, CancellationToken cancellationToken)
    {
        var horizon = options.HorizonDays;
        var grace = OutcomeGraceDays;
        var minPrice = options.MinPrice;
        return db.Database.SqlQuery<SampleRow>($"""
            WITH sample_dates AS (SELECT unnest({dates}) AS d),
            daily AS (
                SELECT s.card_id, s.variant, s.date, s.market, s.low, s.mid, s.high,
                       ln(s.market / lag(s.market) OVER (PARTITION BY s.card_id, s.variant ORDER BY s.date))::float8 AS r1,
                       (s.date - DATE '2000-01-01')::float8 AS x,
                       ln(s.market)::float8 AS y
                FROM price_snapshots s
                WHERE s.market > 0
                  AND s.date >= (SELECT min(d) FROM sample_dates) - 31
                  AND s.date <= (SELECT max(d) FROM sample_dates)
            ),
            windowed AS (
                SELECT card_id, variant, date, market, low, mid, high,
                       stddev_samp(r1) OVER w AS vol30, regr_slope(y, x) OVER w AS slope30, regr_r2(y, x) OVER w AS fit30
                FROM daily
                WINDOW w AS (PARTITION BY card_id, variant ORDER BY date RANGE BETWEEN INTERVAL '30 days' PRECEDING AND CURRENT ROW)
            ),
            ranked AS (
                SELECT w.*, c.set_id,
                       rank() OVER (PARTITION BY w.date, c.set_id ORDER BY w.market DESC) AS set_rank,
                       w.market / sum(w.market) OVER (PARTITION BY w.date, c.set_id) AS set_share
                FROM windowed w
                JOIN sample_dates sd ON sd.d = w.date
                JOIN cards c ON c.id = w.card_id
                WHERE w.market >= {minPrice}
            ),
            capped AS (
                -- A stable pseudo-random subset per day (set ranks above are computed on the full day first).
                SELECT *, row_number() OVER (PARTITION BY date ORDER BY md5(card_id || variant || date::text)) AS pick
                FROM ranked
            )
            SELECT s.card_id AS "CardId", s.variant AS "Variant", s.date AS "Date", s.market AS "Market",
                   s.low AS "Low", s.mid AS "Mid", s.high AS "High",
                   m7.market AS "Market7", m30.market AS "Market30", m90.market AS "Market90",
                   s.vol30 AS "Volatility30", s.slope30 AS "Slope30", s.fit30 AS "Fit30", f.market AS "Future",
                   s.set_id AS "SetId", c.set_name AS "SetName", s.set_rank AS "SetRank", s.set_share AS "SetShare",
                   c.number AS "Number", c.rarity AS "Rarity", c.supertype AS "Supertype", c.subtypes AS "Subtypes",
                   c.national_dex AS "NationalDex", c.artist AS "Artist", c.set_released AS "SetReleased",
                   c.set_printed_total AS "SetPrintedTotal"
            FROM capped s
            JOIN cards c ON c.id = s.card_id
            LEFT JOIN LATERAL (SELECT p.market FROM price_snapshots p WHERE p.card_id = s.card_id AND p.variant = s.variant
                               AND p.date <= s.date - 7 AND p.date > s.date - 14 AND p.market > 0 ORDER BY p.date DESC LIMIT 1) m7 ON true
            LEFT JOIN LATERAL (SELECT p.market FROM price_snapshots p WHERE p.card_id = s.card_id AND p.variant = s.variant
                               AND p.date <= s.date - 30 AND p.date > s.date - 40 AND p.market > 0 ORDER BY p.date DESC LIMIT 1) m30 ON true
            LEFT JOIN LATERAL (SELECT p.market FROM price_snapshots p WHERE p.card_id = s.card_id AND p.variant = s.variant
                               AND p.date <= s.date - 90 AND p.date > s.date - 110 AND p.market > 0 ORDER BY p.date DESC LIMIT 1) m90 ON true
            LEFT JOIN LATERAL (SELECT p.market FROM price_snapshots p WHERE p.card_id = s.card_id AND p.variant = s.variant
                               AND p.date >= s.date + {horizon} AND p.date < s.date + {horizon} + {grace} AND p.market > 0
                               ORDER BY p.date LIMIT 1) f ON true
            WHERE s.date = {latest} OR s.pick <= {perDay}
            ORDER BY s.date, s.card_id, s.variant
            """).ToListAsync(cancellationToken);
    }

    private async Task<IReadOnlyDictionary<int, Species>> LoadSpeciesAsync(CancellationToken cancellationToken)
    {
        // A Pokémon's plainest card name ("Charizard", not "Dark Charizard" or "Charizard ex") names it; its cards'
        // set release dates let the model count its printings as of each sample date.
        var rows = await db.Database.SqlQuery<SpeciesRow>($"""
            SELECT national_dex AS "Dex", (array_agg(name ORDER BY length(name), name))[1] AS "Name",
                   array_agg(set_released ORDER BY set_released) FILTER (WHERE set_released IS NOT NULL) AS "Released"
            FROM cards WHERE national_dex IS NOT NULL GROUP BY national_dex
            """).ToListAsync(cancellationToken);
        return rows.ToDictionary(r => r.Dex, r => new Species(r.Name, (r.Released ?? []).Select(d => d.DayNumber).ToArray()));
    }

    private sealed class SpeciesRow
    {
        public int Dex { get; init; }
        public required string Name { get; init; }
        public DateOnly[]? Released { get; init; }
    }

    /// <summary>Of the moves bigger than 5% either way, the share whose direction was predicted correctly.</summary>
    private static double? DirectionAccuracy(double[] actual, double[] predicted)
    {
        var threshold = Math.Log(1.05);
        var big = actual.Zip(predicted).Where(x => Math.Abs(x.First) > threshold).ToList();
        return big.Count == 0 ? null : big.Count(x => Math.Sign(x.First) == Math.Sign(x.Second)) / (double)big.Count;
    }

    private static double Quantile(double[] sorted, double q) =>
        sorted.Length == 0 ? 0 : sorted[Math.Clamp((int)Math.Round(q * (sorted.Length - 1)), 0, sorted.Length - 1)];

    private static string ImportanceJson(double[] weights) => JsonSerializer.Serialize(
        weights.Select((w, i) => new { feature = PriceFeatures.All[i].Name, weight = Math.Round(w, 4) }).OrderByDescending(w => w.weight),
        Json);
}
