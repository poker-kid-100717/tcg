using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using PokemonTCG.API.Data;
using PokemonTCG.API.Pricing.Tcgplayer;

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
    TcgplayerPriceSync tcgplayer,
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
            // The whole run is one transaction: a run that fails part-way leaves no half-recorded day behind
            // (the price model and movers would otherwise read it as a complete one).
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            await WriteSetsAsync(await client.GetSetsAsync(cancellationToken), cancellationToken);
            for (var page = 1; ; page++)
            {
                var response = await client.GetAllCardsPageAsync(page, cancellationToken);
                if (response.Data.Count == 0) break;

                run.CardsSeen += response.Data.Count;
                run.PricesWritten += await WritePageAsync(response.Data, today, cancellationToken);

                if (run.CardsSeen >= response.TotalCount) break;
                await Task.Delay(options.PageDelayMilliseconds, cancellationToken);
            }

            // With TCGplayer keys, the day's prices come straight from TCGplayer (overwriting the copies above).
            run.PricesWritten += await tcgplayer.SyncAsync(today, cancellationToken);

            var cutoff = today.AddDays(-options.RetentionDays);
            await db.PriceSnapshots.Where(s => s.Date < cutoff).ExecuteDeleteAsync(cancellationToken);

            // The run's success commits with its data: a crash after the commit can't leave complete prices behind a
            // run still marked Running (which would hold back the prediction refresh).
            run.Status = SnapshotStatus.Succeeded;
            run.FinishedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Price snapshot run {RunId} failed after {Cards} cards", run.Id, run.CardsSeen);
            // Everything written was rolled back, so nothing was recorded.
            run.CardsSeen = 0;
            run.PricesWritten = 0;
            run.Status = SnapshotStatus.Failed;
            run.Error = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
        }

        run.FinishedAt ??= clock.GetUtcNow();
        await db.SaveChangesAsync(CancellationToken.None);
        logger.LogInformation("Price snapshot run {RunId} {Status}: {Cards} cards, {Prices} prices",
            run.Id, run.Status, run.CardsSeen, run.PricesWritten);
        return new SnapshotResult(run.Id, run.Status, run.CardsSeen, run.PricesWritten, run.Error);
    }

    /// <summary>Refreshes the local set catalog, so set pages and the collection's set progress don't wait on the upstream API.</summary>
    private async Task WriteSetsAsync(IReadOnlyList<TcgSet> sets, CancellationToken cancellationToken)
    {
        if (sets.Count == 0) return;
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO sets (id, name, series, printed_total, total, release_date, logo_url, symbol_url)
            SELECT * FROM unnest(@ids, @names, @series, @printed, @totals, @released, @logos, @symbols)
            ON CONFLICT (id) DO UPDATE SET
                name = excluded.name, series = excluded.series, printed_total = excluded.printed_total,
                total = excluded.total, release_date = excluded.release_date,
                logo_url = excluded.logo_url, symbol_url = excluded.symbol_url
            """,
            [
                Text("ids", sets.Select(x => x.Id)),
                Text("names", sets.Select(x => x.Name)),
                Text("series", sets.Select(x => x.Series)),
                new NpgsqlParameter("printed", NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = sets.Select(x => x.PrintedTotal).ToArray() },
                new NpgsqlParameter("totals", NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = sets.Select(x => x.Total).ToArray() },
                new NpgsqlParameter("released", NpgsqlDbType.Array | NpgsqlDbType.Date) { Value = sets.Select(x => x.Released).ToArray() },
                Text("logos", sets.Select(x => x.Images?.Logo)),
                Text("symbols", sets.Select(x => x.Images?.Symbol)),
            ],
            cancellationToken);
    }

    /// <summary>
    /// Writes one page of cards: every card into the catalog, every printing's current price into latest_prices,
    /// and printings worth at least <see cref="SnapshotOptions.MinMarketPrice"/> into the dated price history.
    /// </summary>
    private async Task<int> WritePageAsync(IReadOnlyList<TcgCard> cards, DateOnly today, CancellationToken cancellationToken)
    {
        if (cards.Count == 0) return 0;
        var prices = (
            from card in cards
            where card.Tcgplayer?.Prices is { Count: > 0 }
            from price in card.Tcgplayer!.Prices!
            where price.Value.Market is not null || price.Value.Low is not null || price.Value.Mid is not null
            // Every row is dated the day it was recorded, not the upstream update date: each successful run is then a
            // complete day of prices, so day-to-day history, returns and per-day aggregates are over every printing.
            select (card, variant: price.Key, price: price.Value, date: today)
        ).ToList();
        var rows = prices.Where(r => r.price.Market >= options.MinMarketPrice).ToList();

        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO cards (id, name, number, set_id, set_name, rarity, image_small, tcgplayer_url,
                               supertype, subtypes, national_dex, artist, set_series, set_released, set_printed_total,
                               image_large, hp, types, flavor_text)
            SELECT id, name, number, set_id, set_name, rarity, image_small, tcgplayer_url,
                   supertype, string_to_array(subtypes, '|'), national_dex, artist, set_series, set_released, set_printed_total,
                   image_large, hp, string_to_array(types, '|'), flavor_text
            FROM unnest(@ids, @names, @numbers, @set_ids, @set_names, @rarities, @images, @urls,
                        @supertypes, @subtypes, @dex, @artists, @series, @released, @printed,
                        @large_images, @hps, @types, @flavors)
                AS t(id, name, number, set_id, set_name, rarity, image_small, tcgplayer_url,
                     supertype, subtypes, national_dex, artist, set_series, set_released, set_printed_total,
                     image_large, hp, types, flavor_text)
            ON CONFLICT (id) DO UPDATE SET
                name = excluded.name, number = excluded.number, set_id = excluded.set_id,
                set_name = excluded.set_name, rarity = excluded.rarity,
                image_small = excluded.image_small, tcgplayer_url = excluded.tcgplayer_url,
                supertype = excluded.supertype, subtypes = excluded.subtypes, national_dex = excluded.national_dex,
                artist = excluded.artist, set_series = excluded.set_series, set_released = excluded.set_released,
                set_printed_total = excluded.set_printed_total, image_large = excluded.image_large, hp = excluded.hp,
                types = excluded.types, flavor_text = excluded.flavor_text
            """,
            [
                Text("ids", cards.Select(c => c.Id)),
                Text("names", cards.Select(c => c.Name)),
                Text("numbers", cards.Select(c => c.Number)),
                Text("set_ids", cards.Select(c => c.Set.Id)),
                Text("set_names", cards.Select(c => c.Set.Name)),
                Text("rarities", cards.Select(c => c.Rarity)),
                Text("images", cards.Select(c => c.Images?.Small)),
                Text("urls", cards.Select(c => c.Tcgplayer?.Url)),
                Text("supertypes", cards.Select(c => c.Supertype)),
                // unnest can't take a jagged array, so list columns travel as one delimited string per card.
                Text("subtypes", cards.Select(c => string.Join('|', c.Subtypes ?? []))),
                new NpgsqlParameter("dex", NpgsqlDbType.Array | NpgsqlDbType.Integer)
                    { Value = cards.Select(c => c.NationalPokedexNumbers is [var first, ..] ? first : (int?)null).ToArray() },
                Text("artists", cards.Select(c => c.Artist)),
                Text("series", cards.Select(c => c.Set.Series)),
                new NpgsqlParameter("released", NpgsqlDbType.Array | NpgsqlDbType.Date) { Value = cards.Select(c => c.Set.Released).ToArray() },
                new NpgsqlParameter("printed", NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = cards.Select(c => (int?)c.Set.PrintedTotal).ToArray() },
                Text("large_images", cards.Select(c => c.Images?.Large)),
                Text("hps", cards.Select(c => c.Hp)),
                Text("types", cards.Select(c => string.Join('|', c.Types ?? []))),
                Text("flavors", cards.Select(c => c.FlavorText)),
            ],
            cancellationToken);

        if (prices.Count > 0)
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO latest_prices (card_id, variant, market, low, mid, high, updated_on)
                SELECT * FROM unnest(@card_ids, @variants, @markets, @lows, @mids, @highs, @dates)
                ON CONFLICT (card_id, variant) DO UPDATE SET
                    market = excluded.market, low = excluded.low, mid = excluded.mid, high = excluded.high,
                    updated_on = excluded.updated_on
                """,
                [
                    Text("card_ids", prices.Select(r => r.card.Id)),
                    Text("variants", prices.Select(r => r.variant)),
                    Money("markets", prices.Select(r => r.price.Market)),
                    Money("lows", prices.Select(r => r.price.Low)),
                    Money("mids", prices.Select(r => r.price.Mid)),
                    Money("highs", prices.Select(r => r.price.High)),
                    new NpgsqlParameter("dates", NpgsqlDbType.Array | NpgsqlDbType.Date) { Value = prices.Select(r => r.date).ToArray() },
                ],
                cancellationToken);
        }

        if (rows.Count == 0) return 0;
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
        return rows.Count;
    }

    private static NpgsqlParameter Text(string name, IEnumerable<string?> values) =>
        new(name, NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = values.ToArray() };

    private static NpgsqlParameter Money(string name, IEnumerable<decimal?> values) =>
        new(name, NpgsqlDbType.Array | NpgsqlDbType.Numeric) { Value = values.ToArray() };
}
