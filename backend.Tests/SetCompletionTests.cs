using PokemonTCG.API.Collections;
using PokemonTCG.API.Data;

namespace PokemonTcgMarketplace.Backend.Tests
{
    public class SetCompletionTests
    {
        private static readonly CardSet Set = new() { Id = "s", Name = "Set", PrintedTotal = 3, Total = 4 };

        private static Card Card(string number) => new() { Id = $"s-{number}", Name = $"Card {number}", Number = number, SetId = "s" };

        private static Dictionary<string, Dictionary<string, LatestPrice>> Prices(params (string CardId, string Variant, decimal Market)[] rows) =>
            rows.GroupBy(r => r.CardId).ToDictionary(g => g.Key, g => g.ToDictionary(r => r.Variant, r => new LatestPrice { CardId = r.CardId, Variant = r.Variant, Market = r.Market }));

        [Fact]
        public void A_card_with_no_known_printing_is_one_slot_that_any_copy_fills()
        {
            var cards = new[] { Card("1") };
            var empty = SetCompletion.Master(Set, cards, Prices(), new HashSet<(string, string)>());
            var slot = Assert.Single(empty.Missing);
            Assert.Equal((null, "Any printing", 1), (slot.Variant, slot.VariantLabel, empty.Unpriced));

            var filled = SetCompletion.Master(Set, cards, Prices(), new HashSet<(string, string)> { ("s-1", "holofoil") });
            Assert.Equal((1, 1), (filled.Owned, filled.Total));
        }

        [Fact]
        public void Printings_are_listed_in_binder_order()
        {
            var prices = Prices(("s-1", "reverseHolofoil", 1m), ("s-1", "normal", 1m), ("s-1", "holofoil", 1m));
            Assert.Equal(["normal", "holofoil", "reverseHolofoil"], SetCompletion.Printings(prices["s-1"]));
        }

        [Fact]
        public void A_main_set_is_as_big_as_its_printed_total_even_before_the_catalog_has_every_card()
        {
            // Only 2 of the 3 main-set cards are in the catalog yet, plus a secret rare.
            var cards = new[] { Card("1"), Card("2"), Card("4") };
            var prices = Prices(("s-2", "normal", 3m), ("s-2", "reverseHolofoil", 5m), ("s-4", "holofoil", 50m));
            var owned = new HashSet<(string, string)> { ("s-1", "normal") };

            var main = SetCompletion.Goal(SetGoalKind.MainSet, Set, cards, prices, owned);
            Assert.Equal((1, 3, 3m), (main.Owned, main.Total, main.CostToComplete)); // cheapest printing of card 2

            var full = SetCompletion.Goal(SetGoalKind.FullSet, Set, cards, prices, owned);
            Assert.Equal((1, 3, 53m), (full.Owned, full.Total, full.CostToComplete));

            // Card 1 has no price, so its only slot is "any printing", which the normal copy fills.
            var master = SetCompletion.Goal(SetGoalKind.MasterSet, Set, cards, prices, owned);
            Assert.Equal((1, 4, 58m), (master.Owned, master.Total, master.CostToComplete));
        }
    }
}
