using PokemonTCG.API.Data;
using PokemonTCG.API.Pricing;

namespace PokemonTCG.API.Collections;

/// <summary>
/// How complete a set is, and what the rest would cost, for each kind of goal. Pure: callers load the set's cards,
/// their latest prices and what the collector owns.
/// </summary>
public static class SetCompletion
{
    /// <summary>Printings in the order collectors list them in a binder.</summary>
    private static readonly string[] VariantOrder = ["normal", "holofoil", "reverseHolofoil", "1stEdition", "1stEditionNormal", "1stEditionHolofoil", "unlimited", "unlimitedHolofoil"];

    public static bool IsSecret(Card card, CardSet set) => CollectionService.IsSecret(card.Number, set.PrintedTotal);

    /// <summary>A card's printings: every one with a listed price, or a single "any printing" slot when none is known.</summary>
    public static IReadOnlyList<string?> Printings(IReadOnlyDictionary<string, LatestPrice>? prices) =>
        prices is { Count: > 0 }
            ? prices.Keys.OrderBy(v => Array.IndexOf(VariantOrder, v) is var i and >= 0 ? i : VariantOrder.Length).ThenBy(v => v, StringComparer.Ordinal).Cast<string?>().ToList()
            : [null];

    public static MasterChecklist Master(
        CardSet set,
        IEnumerable<Card> cards,
        IReadOnlyDictionary<string, Dictionary<string, LatestPrice>> prices,
        IReadOnlyCollection<(string CardId, string Variant)> owned)
    {
        var ownedCards = owned.Select(o => o.CardId).ToHashSet();
        var total = 0;
        var missing = new List<MissingPrinting>();
        foreach (var card in Ordered(cards))
        {
            var cardPrices = prices.GetValueOrDefault(card.Id);
            foreach (var variant in Printings(cardPrices))
            {
                total++;
                var have = variant is null ? ownedCards.Contains(card.Id) : owned.Contains((card.Id, variant));
                if (have) continue;
                var price = variant is null ? null : cardPrices![variant];
                missing.Add(new MissingPrinting(card.Id, card.Name, card.Number, card.ImageSmall, IsSecret(card, set), variant,
                    variant is null ? "Any printing" : Prices.VariantLabel(variant), price?.Market ?? price?.Mid, CatalogReader.TcgplayerUrl(card)));
            }
        }
        return new MasterChecklist(total - missing.Count, total, missing.Sum(m => m.Price ?? 0), missing.Count(m => m.Price is null), missing);
    }

    /// <summary>Progress toward a goal. Main and full sets count cards (any printing); a master set counts printings.</summary>
    public static GoalProgress Goal(
        SetGoalKind kind,
        CardSet set,
        IReadOnlyList<Card> cards,
        IReadOnlyDictionary<string, Dictionary<string, LatestPrice>> prices,
        IReadOnlyCollection<(string CardId, string Variant)> owned)
    {
        int have, total, unpriced;
        decimal cost;
        if (kind == SetGoalKind.MasterSet)
        {
            var master = Master(set, cards, prices, owned);
            (have, total, cost, unpriced) = (master.Owned, master.Total, master.CostToComplete, master.Unpriced);
        }
        else
        {
            var ownedCards = owned.Select(o => o.CardId).ToHashSet();
            var inGoal = cards.Where(c => kind == SetGoalKind.FullSet || !IsSecret(c, set)).ToList();
            var missingPrices = inGoal.Where(c => !ownedCards.Contains(c.Id))
                .Select(c => prices.GetValueOrDefault(c.Id)?.Values.Select(p => p.Market ?? p.Mid).Where(p => p is not null).Min())
                .ToList();
            have = inGoal.Count - missingPrices.Count;
            // A main set is as big as its printed total, even before every card is in the catalog.
            total = kind == SetGoalKind.MainSet && set.PrintedTotal > 0 ? Math.Max(set.PrintedTotal, inGoal.Count) : inGoal.Count;
            cost = missingPrices.Sum(p => p ?? 0);
            unpriced = missingPrices.Count(p => p is null);
        }
        return new GoalProgress(set.Id, set.Name, set.SymbolUrl, set.LogoUrl, kind, have, total,
            total == 0 ? 0 : Math.Round(Math.Min(100m, have * 100m / total), 1), cost, unpriced);
    }

    private static IEnumerable<Card> Ordered(IEnumerable<Card> cards) =>
        cards.OrderBy(c => CatalogReader.NumberSortKey(c.Number)).ThenBy(c => c.Number, StringComparer.Ordinal);
}
