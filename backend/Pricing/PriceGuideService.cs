using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using PokemonTCG.API.Data;

namespace PokemonTCG.API.Pricing;

/// <summary>
/// Read side of the price guide. Card and set data come from the Pokémon TCG
/// API (cached, so repeat visits don't hit it); price history, movers and the
/// most valuable cards come from the daily snapshots in Postgres.
/// </summary>
public class PriceGuideService(PokemonTcgClient client, AppDbContext db, HybridCache cache, TimeProvider clock)
{
    private static readonly HybridCacheEntryOptions Hours12 = new() { Expiration = TimeSpan.FromHours(12) };
    private static readonly HybridCacheEntryOptions Hour = new() { Expiration = TimeSpan.FromHours(1) };
    private static readonly HybridCacheEntryOptions Minutes10 = new() { Expiration = TimeSpan.FromMinutes(10) };

    public Task<IReadOnlyList<SetSummary>> GetSetsAsync(CancellationToken cancellationToken) =>
        cache.GetOrCreateAsync<IReadOnlyList<SetSummary>>(
            "sets",
            async ct => (await client.GetSetsAsync(ct)).Select(SetSummary.From).ToList(),
            Hours12,
            cancellationToken: cancellationToken).AsTask();

    public async Task<SetDetail?> GetSetAsync(string setId, CancellationToken cancellationToken) =>
        await cache.GetOrCreateAsync(
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
        var card = await cache.GetOrCreateAsync(
            $"card:{cardId}", async ct => await client.GetCardAsync(cardId, ct), Hour, cancellationToken: cancellationToken);
        if (card is null) return null;

        var since = Today().AddDays(-365);
        var history = await db.PriceSnapshots.AsNoTracking()
            .Where(s => s.CardId == card.Id && s.Date >= since)
            .OrderBy(s => s.Date)
            .Select(s => new PricePoint(s.Date, s.Variant, s.Market))
            .ToListAsync(cancellationToken);

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
        await cache.GetOrCreateAsync(
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

    private DateOnly Today() => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    /// <summary>Collector numbers are mostly numeric ("12") but not always ("TG05", "SV001").</summary>
    private static int NumberSortKey(string number)
    {
        var digits = new string(number.SkipWhile(c => !char.IsDigit(c)).TakeWhile(char.IsDigit).ToArray());
        var prefixed = number.Length > 0 && !char.IsDigit(number[0]);
        return (prefixed ? 100_000 : 0) + (int.TryParse(digits, out var n) ? n : 0);
    }
}
