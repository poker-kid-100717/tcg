using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;
using PokemonTCG.API.Data;

namespace PokemonTCG.API.Pricing.Tcgplayer;

/// <summary>
/// With TCGplayer Developer API keys configured, the daily snapshot takes prices straight from TCGplayer: sets are
/// matched to TCGplayer groups and cards to products (<see cref="TcgplayerCatalogMatcher"/>), each matched group's
/// market prices overwrite the day's prices, and cards gain their TCGplayer product id for direct links.
/// Without keys this does nothing and prices keep coming from the Pokémon TCG API's daily copy of TCGplayer's.
/// </summary>
public class TcgplayerPriceSync(
    AppDbContext db,
    IServiceProvider services,
    IOptions<TcgplayerOptions> options,
    SnapshotOptions snapshotOptions,
    ILogger<TcgplayerPriceSync> logger)
{
    public bool IsConfigured => options.Value.IsConfigured;

    /// <summary>Where the app's prices come from, for the attribution line on the site.</summary>
    public string PriceSource => IsConfigured ? "TCGplayer API" : "Pokémon TCG API (TCGplayer market prices)";

    /// <summary>Runs inside the snapshot's transaction, after the catalog is written. Returns the dated prices written.</summary>
    public async Task<int> SyncAsync(DateOnly today, CancellationToken cancellationToken)
    {
        if (!IsConfigured) return 0;
        var client = services.GetRequiredService<TcgplayerClient>();

        var sets = await db.Sets.AsNoTracking()
            .Select(s => new CatalogSet(s.Id, s.Name, s.Series, s.ReleaseDate, s.PrintedTotal, s.Total)).ToListAsync(cancellationToken);
        var cards = await db.Cards.AsNoTracking()
            .Select(c => new CatalogCard(c.Id, c.SetId, c.Number, c.Name)).ToListAsync(cancellationToken);
        var groups = await client.GetGroupsAsync(options.Value.CategoryId, cancellationToken);

        // Products per matched group; a group that fails to load is skipped, not fatal, and simply keeps yesterday's link.
        var groupIds = TcgplayerCatalogMatcher.Match(sets, cards, groups, []).SetToGroup.Values.Distinct().ToList();
        var products = new List<TcgplayerProduct>();
        var prices = new List<TcgplayerProductPrice>();
        foreach (var groupId in groupIds)
        {
            try
            {
                products.AddRange(await client.GetGroupProductsAsync(groupId, cancellationToken));
                prices.AddRange(await client.GetGroupPricesAsync(groupId, cancellationToken));
            }
            catch (Exception ex) when (ex is TcgplayerApiException or HttpRequestException)
            {
                logger.LogWarning(ex, "TCGplayer group {GroupId} failed to load; its cards keep their previous prices", groupId);
            }
        }

        var match = TcgplayerCatalogMatcher.Match(sets, cards, groups, products);
        logger.LogInformation("TCGplayer: {Sets} of {TotalSets} sets and {Cards} of {TotalCards} cards matched; {Unmatched} left unmatched",
            match.SetToGroup.Count, sets.Count, match.CardToProduct.Count, cards.Count, match.Unmatched.Count);

        await WriteLinksAsync(match, cancellationToken);

        var cardByProduct = match.CardToProduct.GroupBy(p => p.Value).Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.Single().Key);
        var rows = prices
            .Where(p => cardByProduct.ContainsKey(p.ProductId) && (p.MarketPrice ?? p.LowPrice ?? p.MidPrice) is not null)
            .Select(p => (CardId: cardByProduct[p.ProductId], Variant: TcgplayerVariants.ToVariantKey(p.SubTypeName), Price: p))
            .DistinctBy(r => (r.CardId, r.Variant))
            .ToList();
        if (rows.Count == 0) return 0;

        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO latest_prices (card_id, variant, market, low, mid, high, updated_on)
            SELECT card_id, variant, market, low, mid, high, @today
            FROM unnest(@card_ids, @variants, @markets, @lows, @mids, @highs) AS t(card_id, variant, market, low, mid, high)
            ON CONFLICT (card_id, variant) DO UPDATE SET
                market = excluded.market, low = excluded.low, mid = excluded.mid, high = excluded.high, updated_on = excluded.updated_on
            """,
            Parameters(rows, today), cancellationToken);

        var dated = rows.Where(r => r.Price.MarketPrice >= snapshotOptions.MinMarketPrice).ToList();
        if (dated.Count == 0) return 0;
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO price_snapshots (card_id, variant, date, market, low, mid, high)
            SELECT card_id, variant, @today, market, low, mid, high
            FROM unnest(@card_ids, @variants, @markets, @lows, @mids, @highs) AS t(card_id, variant, market, low, mid, high)
            ON CONFLICT (card_id, variant, date) DO UPDATE SET
                market = excluded.market, low = excluded.low, mid = excluded.mid, high = excluded.high
            """,
            Parameters(dated, today), cancellationToken);
        return dated.Count;
    }

    private async Task WriteLinksAsync(TcgplayerCatalogMatch match, CancellationToken cancellationToken)
    {
        if (match.SetToGroup.Count > 0)
        {
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE sets SET tcgplayer_group_id = t.group_id FROM unnest(@ids, @groups) AS t(id, group_id) WHERE sets.id = t.id",
                [Text("ids", match.SetToGroup.Keys), Ints("groups", match.SetToGroup.Values)], cancellationToken);
        }
        if (match.CardToProduct.Count > 0)
        {
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE cards SET tcgplayer_product_id = t.product_id FROM unnest(@ids, @products) AS t(id, product_id) WHERE cards.id = t.id",
                [Text("ids", match.CardToProduct.Keys), Ints("products", match.CardToProduct.Values)], cancellationToken);
        }
    }

    private static object[] Parameters(IReadOnlyList<(string CardId, string Variant, TcgplayerProductPrice Price)> rows, DateOnly today) =>
    [
        new NpgsqlParameter("today", NpgsqlDbType.Date) { Value = today },
        Text("card_ids", rows.Select(r => r.CardId)),
        Text("variants", rows.Select(r => r.Variant)),
        Money("markets", rows.Select(r => r.Price.MarketPrice)),
        Money("lows", rows.Select(r => r.Price.LowPrice)),
        Money("mids", rows.Select(r => r.Price.MidPrice)),
        Money("highs", rows.Select(r => r.Price.HighPrice)),
    ];

    private static NpgsqlParameter Text(string name, IEnumerable<string?> values) =>
        new(name, NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = values.ToArray() };

    private static NpgsqlParameter Ints(string name, IEnumerable<int> values) =>
        new(name, NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = values.ToArray() };

    private static NpgsqlParameter Money(string name, IEnumerable<decimal?> values) =>
        new(name, NpgsqlDbType.Array | NpgsqlDbType.Numeric) { Value = values.ToArray() };
}
