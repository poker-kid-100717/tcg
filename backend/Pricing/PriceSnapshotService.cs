using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using PokemonTCG.API.Data;

namespace PokemonTCG.API.Pricing;

public class SnapshotOptions
{
    public const string SectionName = "Snapshots";

    /// <summary>Prices below this market value aren't stored; they're mostly bulk commons.</summary>
    public decimal MinMarketPrice { get; set; } = 0.50m;

    /// <summary>Snapshots older than this are deleted at the end of each run.</summary>
    public int RetentionDays { get; set; } = 400;

    /// <summary>Pause between upstream pages, to stay well inside the API's rate limit.</summary>
    public int PageDelayMilliseconds { get; set; } = 250;
}

public record SnapshotResult(int RunId, SnapshotStatus Status, int CardsSeen, int PricesWritten, string? Error);

/// <summary>
/// Collects today's TCGplayer prices for every card into price_snapshots.
/// Cloudflare runs it once a day through a Cron Trigger (see worker/index.ts).
/// Each page is written with one bulk upsert per table, so a run is a few
/// dozen round trips rather than tens of thousands, and rerunning a day
/// overwrites that day's rows instead of duplicating them.
/// </summary>
public class PriceSnapshotService(
    PokemonTcgClient client,
    AppDbContext db,
    SnapshotOptions options,
    TimeProvider clock,
    ILogger<PriceSnapshotService> logger)
{
    // One run at a time per process; a second trigger while one is running is refused.
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public async Task<SnapshotResult?> RunAsync(CancellationToken cancellationToken)
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

    private async Task<SnapshotResult> RunExclusiveAsync(CancellationToken cancellationToken)
    {
        var run = new SnapshotRun { StartedAt = clock.GetUtcNow(), Status = SnapshotStatus.Running };
        db.SnapshotRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            for (var page = 1; ; page++)
            {
                var response = await client.GetAllCardsPageAsync(page, cancellationToken);
                if (response.Data.Count == 0) break;

                run.CardsSeen += response.Data.Count;
                run.PricesWritten += await WritePageAsync(response.Data, today, cancellationToken);

                if (run.CardsSeen >= response.TotalCount) break;
                await Task.Delay(options.PageDelayMilliseconds, cancellationToken);
            }

            var cutoff = today.AddDays(-options.RetentionDays);
            await db.PriceSnapshots.Where(s => s.Date < cutoff).ExecuteDeleteAsync(cancellationToken);

            run.Status = SnapshotStatus.Succeeded;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Price snapshot run {RunId} failed after {Cards} cards", run.Id, run.CardsSeen);
            run.Status = SnapshotStatus.Failed;
            run.Error = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
        }

        run.FinishedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(CancellationToken.None);
        logger.LogInformation("Price snapshot run {RunId} {Status}: {Cards} cards, {Prices} prices",
            run.Id, run.Status, run.CardsSeen, run.PricesWritten);
        return new SnapshotResult(run.Id, run.Status, run.CardsSeen, run.PricesWritten, run.Error);
    }

    private async Task<int> WritePageAsync(IReadOnlyList<TcgCard> cards, DateOnly today, CancellationToken cancellationToken)
    {
        var priced = cards.Where(c => c.Tcgplayer?.Prices is { Count: > 0 }).ToList();
        var rows = (
            from card in priced
            from price in card.Tcgplayer!.Prices!
            where price.Value.Market >= options.MinMarketPrice
            select (card, variant: price.Key, price: price.Value, date: card.Tcgplayer!.Updated ?? today)
        ).ToList();
        if (rows.Count == 0) return 0;

        var tracked = rows.Select(r => r.card).DistinctBy(c => c.Id).ToList();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO cards (id, name, number, set_id, set_name, rarity, image_small, tcgplayer_url)
            SELECT * FROM unnest(@ids, @names, @numbers, @set_ids, @set_names, @rarities, @images, @urls)
            ON CONFLICT (id) DO UPDATE SET
                name = excluded.name, number = excluded.number, set_id = excluded.set_id,
                set_name = excluded.set_name, rarity = excluded.rarity,
                image_small = excluded.image_small, tcgplayer_url = excluded.tcgplayer_url
            """,
            [
                Text("ids", tracked.Select(c => c.Id)),
                Text("names", tracked.Select(c => c.Name)),
                Text("numbers", tracked.Select(c => c.Number)),
                Text("set_ids", tracked.Select(c => c.Set.Id)),
                Text("set_names", tracked.Select(c => c.Set.Name)),
                Text("rarities", tracked.Select(c => c.Rarity)),
                Text("images", tracked.Select(c => c.Images?.Small)),
                Text("urls", tracked.Select(c => c.Tcgplayer?.Url)),
            ],
            cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO price_snapshots (card_id, variant, date, market, low, mid, high)
            SELECT * FROM unnest(@card_ids, @variants, @dates, @markets, @lows, @mids, @highs)
            ON CONFLICT (card_id, variant, date) DO UPDATE SET
                market = excluded.market, low = excluded.low, mid = excluded.mid, high = excluded.high
            """,
            [
                Text("card_ids", rows.Select(r => r.card.Id)),
                Text("variants", rows.Select(r => r.variant)),
                new NpgsqlParameter("dates", NpgsqlDbType.Array | NpgsqlDbType.Date) { Value = rows.Select(r => r.date).ToArray() },
                Money("markets", rows.Select(r => r.price.Market)),
                Money("lows", rows.Select(r => r.price.Low)),
                Money("mids", rows.Select(r => r.price.Mid)),
                Money("highs", rows.Select(r => r.price.High)),
            ],
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return rows.Count;
    }

    private static NpgsqlParameter Text(string name, IEnumerable<string?> values) =>
        new(name, NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = values.ToArray() };

    private static NpgsqlParameter Money(string name, IEnumerable<decimal?> values) =>
        new(name, NpgsqlDbType.Array | NpgsqlDbType.Numeric) { Value = values.ToArray() };
}
