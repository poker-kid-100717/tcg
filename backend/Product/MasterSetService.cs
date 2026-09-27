using PokemonTCG.API.Pricing;

namespace PokemonTCG.API.Product;

public sealed class MasterSetService(
    PokemonTcgClient cards,
    MasterSetStore store,
    TimeProvider clock)
{
    public async Task<long?> CreateAsync(Guid userId, string setId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(setId)) return null;
        if (await store.FindIdAsync(userId, setId, ct) is { } existing) return existing;

        var set = await cards.GetSetAsync(setId, ct);
        if (set is null) return null;
        var setCards = await cards.GetSetCardsAsync(setId, ct);
        var items = new List<MasterSetStore.SeedItem>();
        foreach (var card in setCards)
        {
            var prices = card.Tcgplayer?.Prices;
            if (prices is { Count: > 0 })
            {
                foreach (var price in prices.OrderBy(p => Prices.VariantOrder(p.Key)))
                {
                    items.Add(new MasterSetStore.SeedItem(
                        card.Id, price.Key, card.Name, card.Number, card.Rarity,
                        card.Images?.Small, card.Tcgplayer?.Url, price.Value.Market));
                }
            }
            else
            {
                items.Add(new MasterSetStore.SeedItem(
                    card.Id, "standard", card.Name, card.Number, card.Rarity,
                    card.Images?.Small, card.Tcgplayer?.Url, null));
            }
        }

        if (items.Count == 0) return null;
        return await store.CreateAsync(userId, set, items, clock.GetUtcNow(), ct);
    }

    public async Task<MasterSetAdvisorContextView?> GetAdvisorContextAsync(Guid userId, long id, CancellationToken ct)
    {
        var detail = await store.GetDetailAsync(userId, id, ct);
        if (detail is null) return null;

        var missing = detail.Items.Where(i => i.OwnedQuantity == 0).ToList();
        var priority = missing
            .OrderByDescending(i => i.CurrentMarketPrice ?? 0)
            .ThenBy(i => i.CardNumber)
            .Take(24)
            .Select(i => new AdvisorCardView(
                i.CardId, i.CardName, i.CardNumber, i.Variant, i.VariantLabel,
                i.CurrentMarketPrice, i.Change30Percent, i.Signal, i.SignalReason, i.TcgplayerUrl))
            .ToList();

        var top = priority.Take(3).ToList();
        var topText = top.Count == 0
            ? "No priced missing cards are available."
            : "Largest remaining cost drivers: " + string.Join(", ", top.Select(c => $"{c.CardName} ({c.VariantLabel}) {Money(c.MarketPrice)}")) + ".";
        var buyCandidates = missing.Count(i => i.Signal == "Buy candidate");
        var watch = missing.Count(i => i.Signal == "Watch");
        var summary =
            $"{detail.Summary.SetName} is {detail.Summary.CompletionPercent:0.#}% complete with {missing.Count} printings missing. " +
            $"Estimated priced cost to complete is {Money(detail.Summary.MissingMarketCost)}. {topText} " +
            $"{buyCandidates} missing printings are currently classified as buy candidates and {watch} as watch candidates. " +
            "Signals are descriptive, based on current market data and available 30-day history; they do not guarantee future prices.";

        return new MasterSetAdvisorContextView(
            id, detail.Summary.SetName, detail.Summary.CompletionPercent, missing.Count,
            detail.Summary.MissingMarketCost, priority, summary);
    }

    private static string Money(decimal? value) => value is null ? "unknown" : "$" + value.Value.ToString("0.00");
}
