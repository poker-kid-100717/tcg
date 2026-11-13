using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using PokemonTCG.API.Data;

namespace PokemonTCG.API.Pricing;

public class SnapshotOptions
{
    public const string SectionName = "Snapshots";

    /// <summary>Card prices below this market value aren't stored; they're mostly bulk commons.</summary>
    public decimal MinMarketPrice { get; set; } = 0.50m;

    /// <summary>Snapshots older than this are deleted at the end of each run.</summary>
    public int RetentionDays { get; set; } = 400;

    /// <summary>Pause between card-database requests while filling the card catalog.</summary>
    public int PageDelayMilliseconds { get; set; } = 250;

    /// <summary>
    /// Sets whose cards are added to the catalog per run, newest first. Bounded so a run stays well inside
    /// the scheduler's time limit; the catalog fills over the first few days and then only new sets load.
    /// </summary>
    public int CatalogSetsPerRun { get; set; } = 25;
}

public record SnapshotResult(int RunId, SnapshotStatus Status, int CardsSeen, int PricesWritten, string? Error);

/// <summary>
/// Records today's TCGplayer prices, for cards and for sealed product, once a day (Cloudflare Cron Trigger,
/// see worker/index.ts).
///
/// Prices come from TCGCSV's daily TCGplayer mirror: one products file and one prices file per set,
/// a few hundred small requests in all. Card prices are filed under the card database's ids, which the
/// card and set pages use, by matching each TCGCSV group to a set and each product's printed number to a
/// card in our catalog. The catalog itself (names, images, the attributes the price model learns from)
/// is loaded from the card database a few sets per run and kept.
///
/// A day's prices are written in one transaction: a run that fails leaves no partial day behind for the
/// movers and the price model to misread.
/// </summary>
public class PriceSnapshotService(
    PokemonTcgClient client,
    TcgcsvClient tcgcsv,
    TcgcsvOptions tcgcsvOptions,
    AppDbContext db,
    SnapshotOptions options,
    TimeProvider clock,
    ILogger<PriceSnapshotService> logger)
{
    // One run at a time per process; a second trigger while one is running is refused.
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private sealed record CardPrice(string CardId, string Variant, decimal Market, decimal? Low, decimal? Mid, decimal? High);

    private sealed record SealedListing(
        int ProductId, int GroupId, string GroupName, string? SetId, string Name, string ProductType,
        string? ImageUrl, string? Url, DateOnly? Released);

    private sealed record SealedPrice(int ProductId, decimal Market, decimal? Low, decimal? Mid, decimal? High);

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

            var sets = await LoadSetsAsync(cancellationToken);
            var groups = await tcgcsv.GetGroupsAsync(cancellationToken);
            var setsByGroup = TcgcsvMapping.MatchSets(groups, sets);

            await FillCatalogAsync(setsByGroup.Values, cancellationToken);
            var catalog = await LoadCatalogAsync(setsByGroup.Values.Select(s => s.Id).ToArray(), cancellationToken);

            var cardPrices = new List<CardPrice>();
            var sealedListings = new List<SealedListing>();
            var sealedPrices = new List<SealedPrice>();
            var cardsSeen = 0;

            foreach (var group in groups)
            {
                var products = await tcgcsv.GetProductsAsync(group.GroupId, cancellationToken);
                await PauseAsync(cancellationToken);
                if (products.Count == 0) continue;
                var prices = (await tcgcsv.GetPricesAsync(group.GroupId, cancellationToken)).ToLookup(p => p.ProductId);
                await PauseAsync(cancellationToken);

                var set = setsByGroup.GetValueOrDefault(group.GroupId);
                var numbers = set is null ? null : catalog.GetValueOrDefault(set.Id);

                // Where two listings share a printed number (a stamped or promo copy alongside the regular
                // card), the plain listing comes first and wins.
                foreach (var product in products.OrderBy(p => p.Name.Contains('(') ? 1 : 0).ThenBy(p => p.ProductId))
                {
                    if (product.Number is { } number)
                    {
                        if (numbers is null || !numbers.TryGetValue(TcgcsvMapping.NormalizeNumber(number), out var cardId)) continue;
                        cardsSeen++;
                        foreach (var price in prices[product.ProductId])
                        {
                            if (price.MarketPrice is not { } market || market < options.MinMarketPrice) continue;
                            cardPrices.Add(new CardPrice(cardId, TcgcsvMapping.VariantKey(price.SubTypeName), market,
                                price.LowPrice, price.MidPrice, price.HighPrice));
                        }
                        continue;
                    }

                    if (SealedProducts.Classify(product.Name) is not { } type) continue;
                    sealedListings.Add(new SealedListing(
                        product.ProductId, group.GroupId, TcgcsvMapping.StripSeriesPrefix(group.Name), set?.Id, product.Name, type,
                        product.ImageUrl, product.Url, ReleaseDate(product) ?? group.Published));
                    var sealedPrice = prices[product.ProductId]
                        .OrderBy(p => string.Equals(p.SubTypeName, "Normal", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                        .FirstOrDefault(p => p.MarketPrice is not null);
                    if (sealedPrice?.MarketPrice is { } sealedMarket)
                        sealedPrices.Add(new SealedPrice(product.ProductId, sealedMarket, sealedPrice.LowPrice, sealedPrice.MidPrice, sealedPrice.HighPrice));
                }
            }

            var distinctCardPrices = cardPrices.DistinctBy(p => (p.CardId, p.Variant)).ToList();

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await WriteCardPricesAsync(distinctCardPrices, today, cancellationToken);
            await WriteSealedAsync(sealedListings, sealedPrices, today, cancellationToken);

            var cutoff = today.AddDays(-options.RetentionDays);
            await db.PriceSnapshots.Where(s => s.Date < cutoff).ExecuteDeleteAsync(cancellationToken);
            await db.Database.ExecuteSqlRawAsync("DELETE FROM sealed_prices WHERE date < {0}", [cutoff], cancellationToken);

            // The run's success commits with its data: a crash after the commit can't leave complete prices behind a
            // run still marked Running (which would hold back the prediction refresh).
            run.CardsSeen = cardsSeen;
            run.PricesWritten = distinctCardPrices.Count + sealedPrices.Count;
            run.Status = SnapshotStatus.Succeeded;
            run.FinishedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            logger.LogInformation(
                "Matched {Mapped} of {Groups} TCGCSV groups to sets; {Cards} cards, {CardPrices} card prices, {Sealed} sealed products, {SealedPrices} sealed prices",
                setsByGroup.Count, groups.Count, cardsSeen, distinctCardPrices.Count, sealedListings.Count, sealedPrices.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Price snapshot run {RunId} failed", run.Id);
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

    /// <summary>The card database's sets. If it is down, sealed prices are still recorded; card prices wait a day.</summary>
    private async Task<IReadOnlyList<TcgSet>> LoadSetsAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await client.GetSetsAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "The card database's set list is unavailable; recording sealed prices only");
            return [];
        }
    }

    /// <summary>Loads the cards of sets the catalog is missing or short of, newest sets first.</summary>
    private async Task FillCatalogAsync(IEnumerable<TcgSet> sets, CancellationToken cancellationToken)
    {
        var counts = new Dictionary<string, int>();
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync(cancellationToken);
        await using (var command = new NpgsqlCommand("SELECT set_id, count(*) FROM cards GROUP BY set_id", connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken)) counts[reader.GetString(0)] = (int)reader.GetInt64(1);
        }

        var missing = sets
            .Where(s => counts.GetValueOrDefault(s.Id) < s.Total)
            .OrderByDescending(s => s.Released)
            .Take(options.CatalogSetsPerRun)
            .ToList();

        foreach (var set in missing)
        {
            try
            {
                var cards = await client.GetSetCardsAsync(set.Id, cancellationToken);
                if (cards.Count > 0) await UpsertCardsAsync(cards, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Cards for set {SetId} could not be loaded; its card prices are skipped this run", set.Id);
            }
            await Task.Delay(options.PageDelayMilliseconds, cancellationToken);
        }
    }

    /// <summary>For each set: normalized printed number → card id.</summary>
    private async Task<Dictionary<string, Dictionary<string, string>>> LoadCatalogAsync(string[] setIds, CancellationToken cancellationToken)
    {
        var catalog = new Dictionary<string, Dictionary<string, string>>();
        if (setIds.Length == 0) return catalog;

        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT id, set_id, number FROM cards WHERE set_id = ANY(@sets)", connection);
        command.Parameters.Add(new NpgsqlParameter("sets", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = setIds });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var setId = reader.GetString(1);
            if (!catalog.TryGetValue(setId, out var numbers))
            {
                numbers = new Dictionary<string, string>();
                catalog[setId] = numbers;
            }
            numbers.TryAdd(TcgcsvMapping.NormalizeNumber(reader.GetString(2)), reader.GetString(0));
        }
        return catalog;
    }

    private static DateOnly? ReleaseDate(TcgcsvProduct product) =>
        DateTime.TryParse(product.PresaleInfo?.ReleasedOn, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var value)
            ? DateOnly.FromDateTime(value)
            : null;

    private Task PauseAsync(CancellationToken cancellationToken) =>
        tcgcsvOptions.RequestDelayMilliseconds > 0
            ? Task.Delay(tcgcsvOptions.RequestDelayMilliseconds, cancellationToken)
            : Task.CompletedTask;

    private async Task UpsertCardsAsync(IReadOnlyList<TcgCard> cards, CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO cards (id, name, number, set_id, set_name, rarity, image_small, tcgplayer_url,
                               supertype, subtypes, national_dex, artist, set_series, set_released, set_printed_total)
            SELECT id, name, number, set_id, set_name, rarity, image_small, tcgplayer_url,
                   supertype, string_to_array(subtypes, '|'), national_dex, artist, set_series, set_released, set_printed_total
            FROM unnest(@ids, @names, @numbers, @set_ids, @set_names, @rarities, @images, @urls,
                        @supertypes, @subtypes, @dex, @artists, @series, @released, @printed)
                AS t(id, name, number, set_id, set_name, rarity, image_small, tcgplayer_url,
                     supertype, subtypes, national_dex, artist, set_series, set_released, set_printed_total)
            ON CONFLICT (id) DO UPDATE SET
                name = excluded.name, number = excluded.number, set_id = excluded.set_id,
                set_name = excluded.set_name, rarity = excluded.rarity,
                image_small = excluded.image_small, tcgplayer_url = excluded.tcgplayer_url,
                supertype = excluded.supertype, subtypes = excluded.subtypes, national_dex = excluded.national_dex,
                artist = excluded.artist, set_series = excluded.set_series, set_released = excluded.set_released,
                set_printed_total = excluded.set_printed_total
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
                // unnest can't take a jagged array, so subtypes travel as one delimited string per card.
                Text("subtypes", cards.Select(c => string.Join('|', c.Subtypes ?? []))),
                new NpgsqlParameter("dex", NpgsqlDbType.Array | NpgsqlDbType.Integer)
                    { Value = cards.Select(c => c.NationalPokedexNumbers is [var first, ..] ? first : (int?)null).ToArray() },
                Text("artists", cards.Select(c => c.Artist)),
                Text("series", cards.Select(c => c.Set.Series)),
                new NpgsqlParameter("released", NpgsqlDbType.Array | NpgsqlDbType.Date) { Value = cards.Select(c => c.Set.Released).ToArray() },
                new NpgsqlParameter("printed", NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = cards.Select(c => (int?)c.Set.PrintedTotal).ToArray() },
            ],
            cancellationToken);
    }

    // Every row is dated the day it was recorded, not TCGplayer's update date: each successful run is then a
    // complete day of prices, so day-to-day history, returns and per-day aggregates cover every printing.
    private async Task WriteCardPricesAsync(IReadOnlyList<CardPrice> rows, DateOnly today, CancellationToken cancellationToken)
    {
        if (rows.Count == 0) return;
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO price_snapshots (card_id, variant, date, market, low, mid, high)
            SELECT card_id, variant, @date, market, low, mid, high
            FROM unnest(@card_ids, @variants, @markets, @lows, @mids, @highs) AS t(card_id, variant, market, low, mid, high)
            ON CONFLICT (card_id, variant, date) DO UPDATE SET
                market = excluded.market, low = excluded.low, mid = excluded.mid, high = excluded.high
            """,
            [
                new NpgsqlParameter("date", NpgsqlDbType.Date) { Value = today },
                Text("card_ids", rows.Select(r => r.CardId)),
                Text("variants", rows.Select(r => r.Variant)),
                Money("markets", rows.Select(r => (decimal?)r.Market)),
                Money("lows", rows.Select(r => r.Low)),
                Money("mids", rows.Select(r => r.Mid)),
                Money("highs", rows.Select(r => r.High)),
            ],
            cancellationToken);
    }

    private async Task WriteSealedAsync(
        IReadOnlyList<SealedListing> listings,
        IReadOnlyList<SealedPrice> prices,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        var distinct = listings.DistinctBy(l => l.ProductId).ToList();
        if (distinct.Count == 0) return;

        // The curated msrp column is left alone: it's maintained by hand, not by the feed.
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO sealed_products (product_id, group_id, group_name, set_id, name, product_type,
                                         image_url, tcgplayer_url, released_on, updated_at)
            SELECT product_id, group_id, group_name, set_id, name, product_type, image_url, tcgplayer_url, released_on, @now
            FROM unnest(@ids, @groups, @group_names, @set_ids, @names, @types, @images, @urls, @released)
                AS t(product_id, group_id, group_name, set_id, name, product_type, image_url, tcgplayer_url, released_on)
            ON CONFLICT (product_id) DO UPDATE SET
                group_id = excluded.group_id, group_name = excluded.group_name, set_id = excluded.set_id,
                name = excluded.name, product_type = excluded.product_type, image_url = excluded.image_url,
                tcgplayer_url = excluded.tcgplayer_url, released_on = excluded.released_on, updated_at = excluded.updated_at
            """,
            [
                new NpgsqlParameter("now", NpgsqlDbType.TimestampTz) { Value = clock.GetUtcNow() },
                Integers("ids", distinct.Select(l => l.ProductId)),
                Integers("groups", distinct.Select(l => l.GroupId)),
                Text("group_names", distinct.Select(l => l.GroupName)),
                Text("set_ids", distinct.Select(l => l.SetId)),
                Text("names", distinct.Select(l => l.Name)),
                Text("types", distinct.Select(l => l.ProductType)),
                Text("images", distinct.Select(l => l.ImageUrl)),
                Text("urls", distinct.Select(l => l.Url)),
                new NpgsqlParameter("released", NpgsqlDbType.Array | NpgsqlDbType.Date) { Value = distinct.Select(l => l.Released).ToArray() },
            ],
            cancellationToken);

        var distinctPrices = prices.DistinctBy(p => p.ProductId).ToList();
        if (distinctPrices.Count == 0) return;
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO sealed_prices (product_id, date, market, low, mid, high)
            SELECT product_id, @date, market, low, mid, high
            FROM unnest(@ids, @markets, @lows, @mids, @highs) AS t(product_id, market, low, mid, high)
            ON CONFLICT (product_id, date) DO UPDATE SET
                market = excluded.market, low = excluded.low, mid = excluded.mid, high = excluded.high
            """,
            [
                new NpgsqlParameter("date", NpgsqlDbType.Date) { Value = today },
                Integers("ids", distinctPrices.Select(p => p.ProductId)),
                Money("markets", distinctPrices.Select(p => (decimal?)p.Market)),
                Money("lows", distinctPrices.Select(p => p.Low)),
                Money("mids", distinctPrices.Select(p => p.Mid)),
                Money("highs", distinctPrices.Select(p => p.High)),
            ],
            cancellationToken);
    }

    private static NpgsqlParameter Text(string name, IEnumerable<string?> values) =>
        new(name, NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = values.ToArray() };

    private static NpgsqlParameter Integers(string name, IEnumerable<int> values) =>
        new(name, NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = values.ToArray() };

    private static NpgsqlParameter Money(string name, IEnumerable<decimal?> values) =>
        new(name, NpgsqlDbType.Array | NpgsqlDbType.Numeric) { Value = values.ToArray() };
}
