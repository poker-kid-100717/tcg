using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using PokemonTCG.API.Data;

namespace PokemonTCG.API.Pricing;

/// <summary>
/// Read side of the price guide. Card and set data come from the Pokémon TCG
/// API (cached, so repeat visits don't hit it); price history, movers and the
/// most valuable cards come from the daily snapshots in Postgres.
/// </summary>
public class PriceGuideService(PokemonTcgClient client, AppDbContext db, HybridCache cache, TimeProvider clock, CatalogReader catalog)
{
    private static readonly HybridCacheEntryOptions Hours12 = new() { Expiration = TimeSpan.FromHours(12) };
    private static readonly HybridCacheEntryOptions Hour = new() { Expiration = TimeSpan.FromHours(1) };
    private static readonly HybridCacheEntryOptions Minutes10 = new() { Expiration = TimeSpan.FromMinutes(10) };

    // Sets, cards and search read the local catalog first (filled by the daily snapshot) and only go to the card
    // database for what the catalog doesn't have yet, such as a fresh install before its first snapshot.
    public async Task<IReadOnlyList<SetSummary>> GetSetsAsync(CancellationToken cancellationToken) =>
        await catalog.GetSetsAsync(cancellationToken) ?? await cache.GetOrCreateAsync<IReadOnlyList<SetSummary>>(
            "sets",
            async ct => (await client.GetSetsAsync(ct)).Select(SetSummary.From).ToList(),
            Hours12,
            cancellationToken: cancellationToken);

    public async Task<SetDetail?> GetSetAsync(string setId, CancellationToken cancellationToken) =>
        await catalog.GetSetAsync(setId, cancellationToken) ?? await cache.GetOrCreateAsync(
            $"set:{setId}",
            async ct =>
            {
                var set = await client.GetSetAsync(setId, ct);
                if (set is null) return null;

                var cards = (await client.GetSetCardsAsync(setId, ct))
                    .Select(CardSummary.From)
                    .OrderBy(c => NumberSortKey(c.Number))
                    .ThenBy(c => c.Number, StringComparer.Ordinal)
                    .ToList();
                var priced = cards.Where(c => c.MarketPrice is not null).ToList();
                var stats = new SetStats(
                    cards.Count,
                    priced.Count,
                    priced.Sum(c => c.MarketPrice!.Value),
                    priced.MaxBy(c => c.MarketPrice));
                return new SetDetail(SetSummary.From(set), stats, cards);
            },
            Hour,
            cancellationToken: cancellationToken);

    public async Task<CardDetail?> GetCardAsync(string cardId, CancellationToken cancellationToken)
    {
        var since = Today().AddDays(-365);
        Task<List<PricePoint>> History(string id) => db.PriceSnapshots.AsNoTracking()
            .Where(s => s.CardId == id && s.Date >= since)
            .OrderBy(s => s.Date)
            .Select(s => new PricePoint(s.Date, s.Variant, s.Market))
            .ToListAsync(cancellationToken);

        if (await catalog.GetCardAsync(cardId, cancellationToken) is { } local)
        {
            return local with { History = await History(cardId) };
        }

        var card = await cache.GetOrCreateAsync(
            $"card:{cardId}", async ct => await client.GetCardAsync(cardId, ct), Hour, cancellationToken: cancellationToken);
        if (card is null) return null;
        var history = await History(card.Id);

        var prices = (card.Tcgplayer?.Prices ?? new Dictionary<string, TcgPrice>())
            .OrderBy(p => Prices.VariantOrder(p.Key))
            .Select(p => new VariantPrice(p.Key, Prices.VariantLabel(p.Key), p.Value.Low, p.Value.Mid, p.Value.High, p.Value.Market))
            .ToList();

        return new CardDetail(
            card.Id, card.Name, card.Supertype, card.Subtypes ?? [], card.Hp, card.Types ?? [], card.Number,
            card.Artist, card.Rarity, card.FlavorText, card.Images?.Small, card.Images?.Large,
            SetSummary.From(card.Set), prices, card.Tcgplayer?.Updated, card.Tcgplayer?.Url, history);
    }

    public async Task<SearchResults> SearchAsync(string query, int page, int pageSize, CancellationToken cancellationToken) =>
        await catalog.SearchAsync(query, page, pageSize, cancellationToken) ?? await cache.GetOrCreateAsync(
            $"search:{query.ToLowerInvariant()}:{page}:{pageSize}",
            async ct =>
            {
                var result = await client.SearchCardsAsync(query, page, pageSize, ct);
                return new SearchResults(result.Data.Select(CardSummary.From).ToList(), page, pageSize, result.TotalCount);
            },
            Minutes10,
            cancellationToken: cancellationToken);

    /// <summary>
    /// Biggest price changes between the latest snapshot and the one closest
    /// to <paramref name="days"/> earlier. Prices under <paramref name="minPrice"/>
    /// are left out on both ends, since a 50-cent card doubling isn't news.
    /// </summary>
    public async Task<MarketMovers> GetMoversAsync(int days, int limit, decimal minPrice, CancellationToken cancellationToken)
    {
        var latest = await db.PriceSnapshots.MaxAsync(s => (DateOnly?)s.Date, cancellationToken);
        if (latest is null) return new MarketMovers(null, null, [], []);

        var target = latest.Value.AddDays(-days);
        var baseline = await db.PriceSnapshots.Where(s => s.Date <= target).MaxAsync(s => (DateOnly?)s.Date, cancellationToken);
        if (baseline is null) return new MarketMovers(null, latest, [], []);

        var changes =
            from now in db.PriceSnapshots.AsNoTracking()
            where now.Date == latest && now.Market >= minPrice
            join then in db.PriceSnapshots.AsNoTracking().Where(s => s.Date == baseline && s.Market >= minPrice)
                on new { now.CardId, now.Variant } equals new { then.CardId, then.Variant }
            join card in db.Cards.AsNoTracking() on now.CardId equals card.Id
            where now.Market != then.Market
            select new
            {
                Card = card,
                now.Variant,
                From = then.Market!.Value,
                To = now.Market!.Value,
                Ratio = (now.Market!.Value - then.Market!.Value) / then.Market!.Value,
            };

        // Filter and sort on the anonymous shape (which EF translates), then build the record.
        // A card's printings often move together, so fetch extra and keep each card's biggest move.
        var gainers = await changes.Where(c => c.Ratio > 0).OrderByDescending(c => c.Ratio).Take(limit * 4)
            .Select(c => new Change(c.Card, c.Variant, c.From, c.To, c.Ratio)).ToListAsync(cancellationToken);
        var losers = await changes.Where(c => c.Ratio < 0).OrderBy(c => c.Ratio).Take(limit * 4)
            .Select(c => new Change(c.Card, c.Variant, c.From, c.To, c.Ratio)).ToListAsync(cancellationToken);

        return new MarketMovers(
            baseline,
            latest,
            gainers.DistinctBy(c => c.Card.Id).Take(limit).Select(ToMove).ToList(),
            losers.DistinctBy(c => c.Card.Id).Take(limit).Select(ToMove).ToList());
    }

    private sealed record Change(Card Card, string Variant, decimal From, decimal To, decimal Ratio);

    private static PriceMove ToMove(Change m) => new(
        m.Card.Id, m.Card.Name, m.Card.Number, m.Card.SetId, m.Card.SetName, m.Card.ImageSmall, m.Card.TcgplayerUrl,
        m.Variant, Prices.VariantLabel(m.Variant), m.From, m.To, Math.Round(m.Ratio * 100, 1));

    /// <summary>The highest market prices in the latest snapshot, one entry per card (its priciest printing).</summary>
    public async Task<IReadOnlyList<ValuableCard>> GetMostValuableAsync(int limit, CancellationToken cancellationToken)
    {
        var latest = await db.PriceSnapshots.MaxAsync(s => (DateOnly?)s.Date, cancellationToken);
        if (latest is null) return [];

        var top = await (
                from s in db.PriceSnapshots.AsNoTracking()
                where s.Date == latest && s.Market != null
                join card in db.Cards.AsNoTracking() on s.CardId equals card.Id
                orderby s.Market descending
                select new { card, s.Variant, Market = s.Market!.Value })
            .Take(limit * 4)
            .ToListAsync(cancellationToken);

        return top.DistinctBy(t => t.card.Id).Take(limit).Select(t => new ValuableCard(t.card.Id, t.card.Name, t.card.Number, t.card.SetId, t.card.SetName,
            t.card.ImageSmall, t.card.TcgplayerUrl, Prices.VariantLabel(t.Variant), t.Market)).ToList();
    }

    /// <summary>
    /// Cards falling steadily over the last <paramref name="days"/> days, not just between two dates: a
    /// least-squares line through each printing's daily market prices (Postgres <c>regr_slope</c> and
    /// <c>regr_r2</c>) must slope down and fit well (r² ≥ 0.6, so one bad day doesn't count), with at
    /// least five points and a drop of 10% or more from the first to the latest price.
    /// </summary>
    public async Task<DownTrend> GetDownTrendingAsync(int days, int limit, decimal minPrice, CancellationToken cancellationToken)
    {
        var latest = await db.PriceSnapshots.MaxAsync(s => (DateOnly?)s.Date, cancellationToken);
        var history = await db.PriceSnapshots.Select(s => s.Date).Distinct().CountAsync(cancellationToken);
        if (latest is null) return new DownTrend(null, null, days, 0, []);

        var fetch = limit * 4;
        var rows = await db.Database.SqlQuery<TrendRow>($"""
            WITH latest AS (SELECT max(date) AS d FROM price_snapshots),
            w AS (
                SELECT s.card_id, s.variant, s.date, s.market, (s.date - l.d)::float8 AS x, s.market::float8 AS y
                FROM price_snapshots s CROSS JOIN latest l
                WHERE s.date >= l.d - {days} AND s.market IS NOT NULL
            ),
            fit AS (
                SELECT card_id, variant, count(*) AS n, regr_slope(y, x) AS slope, regr_r2(y, x) AS r2,
                       (array_agg(market ORDER BY date))[1] AS first_price,
                       (array_agg(market ORDER BY date DESC))[1] AS last_price,
                       max(date) AS last_date
                FROM w GROUP BY card_id, variant
            )
            SELECT f.card_id AS "CardId", f.variant AS "Variant", f.first_price AS "From", f.last_price AS "To", f.r2 AS "Fit"
            FROM fit f CROSS JOIN latest l
            WHERE f.n >= 5 AND f.slope < 0 AND f.r2 >= 0.6 AND f.last_date = l.d
              AND f.last_price >= {minPrice} AND f.last_price <= f.first_price * 0.9
            ORDER BY f.last_price / f.first_price, f.card_id
            LIMIT {fetch}
            """).ToListAsync(cancellationToken);

        var picked = rows.DistinctBy(r => r.CardId).Take(limit).ToList();
        var ids = picked.Select(r => r.CardId).ToList();
        var from = latest.Value.AddDays(-days);
        var cards = await db.Cards.AsNoTracking().Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, cancellationToken);
        var points = (await db.PriceSnapshots.AsNoTracking()
                .Where(s => ids.Contains(s.CardId) && s.Date >= from && s.Market != null)
                .OrderBy(s => s.Date)
                .Select(s => new { s.CardId, s.Variant, s.Date, Market = s.Market!.Value })
                .ToListAsync(cancellationToken))
            .ToLookup(s => (s.CardId, s.Variant));

        var trending = picked.Where(r => cards.ContainsKey(r.CardId)).Select(r =>
        {
            var card = cards[r.CardId];
            return new TrendingCard(
                card.Id, card.Name, card.Number, card.SetId, card.SetName, card.ImageSmall, card.TcgplayerUrl,
                r.Variant, Prices.VariantLabel(r.Variant), r.From, r.To, Math.Round((r.To / r.From - 1) * 100, 1),
                Math.Round(r.Fit, 2), points[(r.CardId, r.Variant)].Select(p => new TrendPoint(p.Date, p.Market)).ToList());
        }).ToList();

        return new DownTrend(from, latest, days, history, trending);
    }

    private sealed class TrendRow
    {
        public required string CardId { get; init; }
        public required string Variant { get; init; }
        public decimal From { get; init; }
        public decimal To { get; init; }
        public double Fit { get; init; }
    }

    /// <summary>
    /// Possible sleepers: printings whose cheapest TCGplayer listing is at least <paramref name="minGap"/>
    /// above what the card has actually been selling for, while the sale price itself has stayed quiet
    /// (within 15% of 30 days ago, when there's that much history). Thin supply at the market price
    /// tends to come before the market price catching up. Gaps over 3× are left out as likely bad data.
    /// </summary>
    public async Task<Sleepers> GetSleepersAsync(int limit, decimal minPrice, decimal minGap, CancellationToken cancellationToken)
    {
        var latest = await db.PriceSnapshots.MaxAsync(s => (DateOnly?)s.Date, cancellationToken);
        if (latest is null) return new Sleepers(null, []);

        var baseline = await db.PriceSnapshots.Where(s => s.Date <= latest.Value.AddDays(-30)).MaxAsync(s => (DateOnly?)s.Date, cancellationToken);
        var factor = 1 + minGap;
        var fetch = limit * 4;
        var rows = await db.Database.SqlQuery<SleeperRow>($"""
            SELECT s.card_id AS "CardId", s.variant AS "Variant", s.market AS "Market", s.low AS "Low", s.mid AS "Mid",
                   b.market AS "Then"
            FROM price_snapshots s
            LEFT JOIN price_snapshots b ON b.card_id = s.card_id AND b.variant = s.variant AND b.date = {baseline}
            WHERE s.date = {latest.Value} AND s.market >= {minPrice}
              AND s.low >= s.market * {factor} AND s.low <= s.market * 3
              AND (b.market IS NULL OR abs(s.market / b.market - 1) <= 0.15)
            ORDER BY s.low / s.market DESC, s.card_id
            LIMIT {fetch}
            """).ToListAsync(cancellationToken);

        var picked = rows.DistinctBy(r => r.CardId).Take(limit).ToList();
        var ids = picked.Select(r => r.CardId).ToList();
        var cards = await db.Cards.AsNoTracking().Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, cancellationToken);

        return new Sleepers(latest, picked.Where(r => cards.ContainsKey(r.CardId)).Select(r =>
        {
            var card = cards[r.CardId];
            return new SleeperCard(
                card.Id, card.Name, card.Number, card.SetId, card.SetName, card.ImageSmall, card.TcgplayerUrl,
                r.Variant, Prices.VariantLabel(r.Variant), r.Market, r.Low, r.Mid,
                Math.Round((r.Low / r.Market - 1) * 100, 1),
                r.Then is { } then ? Math.Round((r.Market / then - 1) * 100, 1) : null);
        }).ToList());
    }

    private sealed class SleeperRow
    {
        public required string CardId { get; init; }
        public required string Variant { get; init; }
        public decimal Market { get; init; }
        public decimal Low { get; init; }
        public decimal? Mid { get; init; }
        public decimal? Then { get; init; }
    }

    private DateOnly Today() => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    /// <summary>Collector numbers are mostly numeric ("12") but not always ("TG05", "SV001").</summary>
    private static int NumberSortKey(string number)
    {
        var digits = new string(number.SkipWhile(c => !char.IsDigit(c)).TakeWhile(char.IsDigit).ToArray());
        var prefixed = number.Length > 0 && !char.IsDigit(number[0]);
        return (prefixed ? 100_000 : 0) + (int.TryParse(digits, out var n) ? n : 0);
    }
}
