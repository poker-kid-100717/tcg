using Microsoft.EntityFrameworkCore;
using PokemonTCG.API.Data;

namespace PokemonTCG.API.Pricing;

/// <summary>
/// Reads sets and cards from the local catalog (refreshed by each daily snapshot). Pages built from it answer in
/// milliseconds instead of waiting on the card database, which can take tens of seconds for a large set.
/// Every method returns null when the catalog doesn't have the data yet, so callers can fall back upstream.
/// </summary>
public class CatalogReader(AppDbContext db)
{
    public async Task<IReadOnlyList<SetSummary>?> GetSetsAsync(CancellationToken cancellationToken)
    {
        var sets = await db.Sets.AsNoTracking().OrderByDescending(s => s.ReleaseDate).ThenBy(s => s.Name).ToListAsync(cancellationToken);
        return sets.Count == 0 ? null : sets.Select(ToSummary).ToList();
    }

    public async Task<SetDetail?> GetSetAsync(string setId, CancellationToken cancellationToken)
    {
        var set = await db.Sets.AsNoTracking().FirstOrDefaultAsync(s => s.Id == setId, cancellationToken);
        if (set is null) return null;
        var cards = await db.Cards.AsNoTracking().Where(c => c.SetId == setId).ToListAsync(cancellationToken);
        if (cards.Count == 0) return null;
        var prices = await PricesForAsync(cards.Select(c => c.Id), cancellationToken);

        var summaries = cards.Select(c => ToSummary(c, prices.GetValueOrDefault(c.Id)))
            .OrderBy(c => NumberSortKey(c.Number))
            .ThenBy(c => c.Number, StringComparer.Ordinal)
            .ToList();
        var priced = summaries.Where(c => c.MarketPrice is not null).ToList();
        var stats = new SetStats(summaries.Count, priced.Count, priced.Sum(c => c.MarketPrice!.Value), priced.MaxBy(c => c.MarketPrice));
        return new SetDetail(ToSummary(set), stats, summaries);
    }

    /// <summary>Card detail without history (the caller adds it); null when the card isn't in the catalog.</summary>
    public async Task<CardDetail?> GetCardAsync(string cardId, CancellationToken cancellationToken)
    {
        var card = await db.Cards.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cardId, cancellationToken);
        if (card is null || card.ImageLarge is null) return null; // cards recorded before the catalog kept full details
        var set = await db.Sets.AsNoTracking().FirstOrDefaultAsync(s => s.Id == card.SetId, cancellationToken);
        var rows = await db.LatestPrices.AsNoTracking().Where(p => p.CardId == cardId).ToListAsync(cancellationToken);
        var prices = rows.OrderBy(p => Prices.VariantOrder(p.Variant))
            .Select(p => new VariantPrice(p.Variant, Prices.VariantLabel(p.Variant), p.Low, p.Mid, p.High, p.Market))
            .ToList();
        var setSummary = set is not null
            ? ToSummary(set)
            : new SetSummary(card.SetId, card.SetName, card.SetSeries ?? "", card.SetReleased, card.SetPrintedTotal ?? 0, card.SetPrintedTotal ?? 0, null, null);
        return new CardDetail(
            card.Id, card.Name, card.Supertype, card.Subtypes, card.Hp, card.Types, card.Number, card.Artist, card.Rarity,
            card.FlavorText, card.ImageSmall, card.ImageLarge, setSummary, prices,
            rows.Count == 0 ? null : rows.Max(p => p.UpdatedOn), TcgplayerUrl(card), []);
    }

    /// <summary>Cards whose name, or any word in it, starts with the query; newest sets first.</summary>
    public async Task<SearchResults?> SearchAsync(string query, int page, int pageSize, CancellationToken cancellationToken)
    {
        if (!await db.Sets.AnyAsync(cancellationToken)) return null;
        var term = query.Replace("\\", "").Replace("%", "").Replace("_", "");
        var matches = db.Cards.AsNoTracking().Where(c =>
            EF.Functions.ILike(c.Name, term + "%") || EF.Functions.ILike(c.Name, "% " + term + "%"));
        var total = await matches.CountAsync(cancellationToken);
        var cards = await matches.OrderByDescending(c => c.SetReleased).ThenBy(c => c.SetId).ThenBy(c => c.Number)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        var prices = await PricesForAsync(cards.Select(c => c.Id), cancellationToken);
        return new SearchResults(cards.Select(c => ToSummary(c, prices.GetValueOrDefault(c.Id))).ToList(), page, pageSize, total);
    }

    public async Task<Dictionary<string, Dictionary<string, TcgPrice>>> PricesForAsync(IEnumerable<string> cardIds, CancellationToken cancellationToken)
    {
        var ids = cardIds.Distinct().ToList();
        var rows = await db.LatestPrices.AsNoTracking().Where(p => ids.Contains(p.CardId)).ToListAsync(cancellationToken);
        return rows.GroupBy(p => p.CardId).ToDictionary(
            g => g.Key,
            g => g.ToDictionary(p => p.Variant, p => new TcgPrice(p.Low, p.Mid, p.High, p.Market, null)));
    }

    public static SetSummary ToSummary(CardSet set) =>
        new(set.Id, set.Name, set.Series, set.ReleaseDate, set.PrintedTotal, set.Total, set.LogoUrl, set.SymbolUrl);

    public static CardSummary ToSummary(Card card, IReadOnlyDictionary<string, TcgPrice>? prices) => new(
        card.Id, card.Name, card.Number, card.Rarity, card.ImageSmall, card.SetId, card.SetName,
        Prices.HeadlinePrice(prices), Prices.HeadlineVariant(prices), TcgplayerUrl(card));

    /// <summary>A card's TCGplayer page: the product itself once matched, otherwise the Pokémon TCG API's redirect.</summary>
    public static string? TcgplayerUrl(Card card) =>
        card.TcgplayerProductId is { } productId ? $"https://www.tcgplayer.com/product/{productId}" : card.TcgplayerUrl;

    /// <summary>Collector numbers are mostly numeric ("12") but not always ("TG05", "SV001").</summary>
    public static int NumberSortKey(string number)
    {
        var digits = new string(number.SkipWhile(c => !char.IsDigit(c)).TakeWhile(char.IsDigit).ToArray());
        var prefixed = number.Length > 0 && !char.IsDigit(number[0]);
        return (prefixed ? 100_000 : 0) + (int.TryParse(digits, out var n) ? n : 0);
    }
}
