using PokemonTCG.API.Pricing.Tcgplayer;

namespace PokemonTcgMarketplace.Backend.Tests.Tcgplayer
{
    public class TcgplayerCatalogMatcherTests
    {
        private static CatalogSet Set(string id, string name, string released, string series = "Scarlet & Violet", int total = 200) =>
            new(id, name, series, DateOnly.Parse(released), total, total);

        private static TcgplayerGroup Group(int id, string name, string published) =>
            new(id, name, null, false, DateTime.Parse(published), null, 3);

        private static TcgplayerProduct Product(int id, int groupId, string name, string? number) =>
            new(id, name, name, null, 3, groupId, null, null,
                number is null ? [] : [new TcgplayerExtendedData("Number", "Card Number", number)]);

        private static TcgplayerCatalogMatch MatchSets(IEnumerable<CatalogSet> sets, IEnumerable<TcgplayerGroup> groups) =>
            TcgplayerCatalogMatcher.Match(sets, [], groups, []);

        // ------------------------------------------------------------ sets

        [Theory]
        [InlineData("SV03.5: 151", "151")]
        [InlineData("SWSH07: Evolving Skies", "evolving skies")]
        [InlineData("SM - Guardians Rising", "guardians rising")]
        [InlineData("XY - Evolutions", "evolutions")]
        [InlineData("ME01: Mega Evolution", "mega evolution")]
        [InlineData("Scarlet & Violet", "scarlet and violet")]
        [InlineData("Pokémon GO", "go")]
        [InlineData("Crown Zenith: Galarian Gallery", "crown zenith galarian gallery")]
        [InlineData("Sun & Moon: Black Star Promos", "sun and moon black star promos")]
        [InlineData("EX Ruby & Sapphire", "ex ruby and sapphire")]
        public void Set_name_normalization(string name, string expected) =>
            Assert.Equal(expected, TcgplayerCatalogMatcher.NormalizeSetName(name));

        [Fact]
        public void Exact_set_name()
        {
            var result = MatchSets(
                [Set("sv1", "Scarlet & Violet", "2023/03/31")],
                [Group(1, "Scarlet and Violet", "2020-01-01"), Group(2, "Paldea Evolved", "2023-06-09")]);

            Assert.Equal(1, result.SetToGroup["sv1"]);
            Assert.Equal(SetMatchMethod.ExactName, Assert.Single(result.SetMatches).Method);
            Assert.Empty(result.Unmatched);
        }

        [Fact]
        public void Series_code_prefix_is_ignored()
        {
            var result = MatchSets(
                [Set("swsh7", "Evolving Skies", "2021/08/27"), Set("sm2", "Guardians Rising", "2017/05/05"), Set("sv3pt5", "151", "2023/09/22")],
                [Group(2848, "SWSH07: Evolving Skies", "2021-08-27"), Group(1919, "SM - Guardians Rising", "2017-05-05"), Group(23237, "SV03.5: 151", "2023-09-22")]);

            Assert.Equal(2848, result.SetToGroup["swsh7"]);
            Assert.Equal(1919, result.SetToGroup["sm2"]);
            Assert.Equal(23237, result.SetToGroup["sv3pt5"]);
            Assert.All(result.SetMatches, m => Assert.Equal(SetMatchMethod.ExactName, m.Method));
        }

        [Fact]
        public void Date_fallback_uses_token_overlap_within_the_window()
        {
            var result = MatchSets(
                [Set("sv3pt5", "151", "2023/09/22")],
                [
                    Group(23237, "SV: Scarlet & Violet 151", "2023-09-22"),
                    Group(99, "Obsidian Flames", "2023-08-11"),
                    Group(98, "Pokemon 151 Card Sleeves", "2022-01-01"), // shares a token, far outside the window
                ]);

            var match = Assert.Single(result.SetMatches);
            Assert.Equal(23237, match.GroupId);
            Assert.Equal(SetMatchMethod.ReleaseDate, match.Method);
        }

        [Fact]
        public void Date_fallback_requires_similar_names()
        {
            var result = MatchSets(
                [Set("sv3", "Obsidian Flames", "2023/08/11")],
                [Group(1, "Trick or Trade BOOster Bundle", "2023-08-15")]);

            Assert.Empty(result.SetToGroup);
            var item = Assert.Single(result.Unmatched);
            Assert.Equal(CatalogItemKind.Set, item.Kind);
            Assert.Contains("no TCGplayer group", item.Reason);
        }

        [Fact]
        public void Date_fallback_outside_window_is_rejected()
        {
            var result = MatchSets(
                [Set("sv3pt5", "151", "2023/09/22")],
                [Group(23237, "SV: Scarlet & Violet 151", "2023-10-03")]); // 11 days

            Assert.Empty(result.SetToGroup);
        }

        [Fact]
        public void Date_fallback_tie_is_reported_as_ambiguous()
        {
            var result = MatchSets(
                [Set("x", "Battle Academy", "2022/06/17")],
                [Group(1, "Battle Academy 2022 Deck Charizard", "2022-06-17"), Group(2, "Battle Academy 2022 Deck Pikachu", "2022-06-17")]);

            Assert.Empty(result.SetToGroup);
            Assert.Contains("ambiguous", Assert.Single(result.Unmatched).Reason);
        }

        [Fact]
        public void Never_two_sets_on_one_group_by_date()
        {
            var result = MatchSets(
                [Set("a", "Paldean Fates", "2024/01/26"), Set("b", "Paldean Fates Shiny", "2024/01/26")],
                [Group(1, "SV: Paldean Fates", "2024-01-26")]);

            Assert.Equal(1, result.SetToGroup["a"]); // exact name wins
            Assert.False(result.SetToGroup.ContainsKey("b"));
            Assert.Equal("b", Assert.Single(result.Unmatched).Id);
        }

        [Fact]
        public void Two_sets_with_the_same_normalized_name_are_split_by_date_or_reported()
        {
            var result = MatchSets(
                [Set("base1", "Base Set", "1999/01/09"), Set("base4", "Base Set", "2000/02/24")],
                [Group(604, "Base Set", "1999-01-09")]);

            Assert.Equal(604, result.SetToGroup["base1"]);
            Assert.Contains("matches several of our sets", Assert.Single(result.Unmatched).Reason);
        }

        [Fact]
        public void Trainer_gallery_subset_shares_the_parent_group()
        {
            var result = MatchSets(
                [Set("swsh9", "Brilliant Stars", "2022/02/25"), Set("swsh9tg", "Brilliant Stars Trainer Gallery", "2022/02/25")],
                [Group(2948, "SWSH09: Brilliant Stars", "2022-02-25")]);

            Assert.Equal(2948, result.SetToGroup["swsh9"]);
            Assert.Equal(2948, result.SetToGroup["swsh9tg"]);
            Assert.Equal(SetMatchMethod.Subset, result.SetMatches.Single(m => m.SetId == "swsh9tg").Method);
        }

        [Fact]
        public void Subset_with_its_own_group_uses_it()
        {
            var result = MatchSets(
                [Set("swsh12pt5", "Crown Zenith", "2023/01/20"), Set("swsh12pt5gg", "Crown Zenith Galarian Gallery", "2023/01/20")],
                [Group(17688, "Crown Zenith", "2023-01-20"), Group(17689, "Crown Zenith: Galarian Gallery", "2023-01-20")]);

            Assert.Equal(17688, result.SetToGroup["swsh12pt5"]);
            Assert.Equal(17689, result.SetToGroup["swsh12pt5gg"]);
            Assert.All(result.SetMatches, m => Assert.Equal(SetMatchMethod.ExactName, m.Method));
        }

        // ----------------------------------------------------------- cards

        [Theory]
        [InlineData("006/165", "6")]
        [InlineData("6", "6")]
        [InlineData("TG05/TG30", "TG5")]
        [InlineData("TG05", "TG5")]
        [InlineData("SWSH101", "SWSH101")]
        [InlineData("SWSH001", "SWSH1")]
        [InlineData("SV001", "SV1")]
        [InlineData("GG01/GG70", "GG1")]
        [InlineData("0", "0")]
        [InlineData("100/102", "100")]
        [InlineData(" 010 / 100 ", "10")]
        public void Number_normalization(string number, string expected) =>
            Assert.Equal(expected, TcgplayerCatalogMatcher.NormalizeNumber(number));

        [Theory]
        [InlineData("Charizard ex", "Charizard ex - 006/165")]
        [InlineData("Charizard ex", "Charizard EX")]
        [InlineData("Professor's Research", "Professor's Research (Professor Oak)")]
        [InlineData("Pikachu", "Pikachu - SWSH020")]
        [InlineData("Flabébé", "Flabebe")]
        [InlineData("Porygon-Z", "Porygon Z")]
        [InlineData("Mr. Mime", "Mr Mime")]
        [InlineData("Nidoran ♀", "Nidoran F")]
        [InlineData("Mewtwo & Mew GX", "Mewtwo and Mew GX - SM191")]
        [InlineData("Ho-Oh", "Ho-Oh")]
        public void Card_names_agree_loosely(string ours, string theirs) =>
            Assert.Equal(TcgplayerCatalogMatcher.NameKey(ours), TcgplayerCatalogMatcher.NameKey(theirs));

        [Theory]
        [InlineData("Charizard ex", "Charmander")]
        [InlineData("Pikachu", "Pikachu V")]
        [InlineData("Ho-Oh", "Ho")]
        public void Different_card_names_disagree(string ours, string theirs) =>
            Assert.NotEqual(TcgplayerCatalogMatcher.NameKey(ours), TcgplayerCatalogMatcher.NameKey(theirs));

        private static readonly CatalogSet Sv151 = Set("sv3pt5", "151", "2023/09/22");
        private static readonly TcgplayerGroup Sv151Group = Group(23237, "SV03.5: 151", "2023-09-22");

        [Fact]
        public void Cards_match_on_number_and_name()
        {
            var result = TcgplayerCatalogMatcher.Match(
                [Sv151],
                [new("sv3pt5-6", "sv3pt5", "6", "Charizard ex"), new("sv3pt5-1", "sv3pt5", "1", "Bulbasaur")],
                [Sv151Group],
                [
                    Product(500, 23237, "Charizard ex - 006/165", "006/165"),
                    Product(501, 23237, "Bulbasaur", "001/165"),
                    Product(502, 23237, "Pokemon Card 151 Booster Bundle", null), // sealed: no number
                    Product(600, 1, "Bulbasaur", "001/165"), // another group
                ]);

            Assert.Equal(500, result.CardToProduct["sv3pt5-6"]);
            Assert.Equal(501, result.CardToProduct["sv3pt5-1"]);
            Assert.Empty(result.Unmatched);
        }

        [Fact]
        public void Trainer_gallery_cards_match_in_the_parent_group()
        {
            var result = TcgplayerCatalogMatcher.Match(
                [Set("swsh9", "Brilliant Stars", "2022/02/25"), Set("swsh9tg", "Brilliant Stars Trainer Gallery", "2022/02/25")],
                [new("swsh9-5", "swsh9", "5", "Ivysaur"), new("swsh9tg-TG05", "swsh9tg", "TG05", "Charizard")],
                [Group(2948, "SWSH09: Brilliant Stars", "2022-02-25")],
                [
                    Product(700, 2948, "Ivysaur", "005/172"),
                    Product(701, 2948, "Charizard - TG05/TG30", "TG05/TG30"),
                    Product(702, 2948, "Charizard V", "017/172"),
                ]);

            Assert.Equal(700, result.CardToProduct["swsh9-5"]);
            Assert.Equal(701, result.CardToProduct["swsh9tg-TG05"]);
            Assert.Empty(result.Unmatched);
        }

        [Fact]
        public void Promo_numbers_match()
        {
            var result = TcgplayerCatalogMatcher.Match(
                [Set("swshp", "SWSH Black Star Promos", "2019/11/15", "Sword & Shield")],
                [new("swshp-SWSH101", "swshp", "SWSH101", "Pikachu V"), new("swshp-SWSH020", "swshp", "SWSH020", "Pikachu")],
                [Group(2545, "SWSH: Sword & Shield Promo Cards", "2019-11-15")],
                [
                    Product(800, 2545, "Pikachu V - SWSH101", "SWSH101"),
                    Product(801, 2545, "Pikachu - SWSH020", "SWSH020"),
                ]);

            Assert.Equal(2545, result.SetToGroup["swshp"]);
            Assert.Equal(800, result.CardToProduct["swshp-SWSH101"]);
            Assert.Equal(801, result.CardToProduct["swshp-SWSH020"]);
        }

        [Fact]
        public void Name_mismatch_is_rejected()
        {
            var result = TcgplayerCatalogMatcher.Match(
                [Sv151],
                [new("sv3pt5-4", "sv3pt5", "4", "Charmander")],
                [Sv151Group],
                [Product(500, 23237, "Charizard ex - 006/165", "004/165")]);

            Assert.Empty(result.CardToProduct);
            var item = Assert.Single(result.Unmatched);
            Assert.Equal(CatalogItemKind.Card, item.Kind);
            Assert.Equal("sv3pt5-4", item.Id);
            Assert.Contains("name mismatch", item.Reason);
        }

        [Fact]
        public void Missing_number_is_reported()
        {
            var result = TcgplayerCatalogMatcher.Match(
                [Sv151], [new("sv3pt5-200", "sv3pt5", "200", "Charizard ex")], [Sv151Group],
                [Product(500, 23237, "Charizard ex - 006/165", "006/165")]);

            Assert.Contains("no product numbered 200", Assert.Single(result.Unmatched).Reason);
        }

        [Fact]
        public void Duplicate_numbers_on_tcgplayer_prefer_the_exact_full_name()
        {
            var result = TcgplayerCatalogMatcher.Match(
                [Sv151], [new("sv3pt5-25", "sv3pt5", "25", "Pikachu")], [Sv151Group],
                [Product(900, 23237, "Pikachu - 025/165", "025/165"), Product(901, 23237, "Pikachu (Cosmos Holo)", "025/165")]);

            Assert.Equal(900, result.CardToProduct["sv3pt5-25"]);
        }

        [Fact]
        public void Duplicate_numbers_on_tcgplayer_that_cannot_be_told_apart_are_ambiguous()
        {
            var result = TcgplayerCatalogMatcher.Match(
                [Sv151], [new("sv3pt5-25", "sv3pt5", "25", "Pikachu")], [Sv151Group],
                [Product(900, 23237, "Pikachu (Cosmos Holo)", "025/165"), Product(901, 23237, "Pikachu (Stamped)", "025/165")]);

            Assert.Empty(result.CardToProduct);
            Assert.Contains("ambiguous", Assert.Single(result.Unmatched).Reason);
        }

        [Fact]
        public void Two_of_our_cards_on_one_product_are_both_reported()
        {
            var result = TcgplayerCatalogMatcher.Match(
                [Sv151],
                [new("sv3pt5-6", "sv3pt5", "6", "Charizard ex"), new("sv3pt5-006", "sv3pt5", "006", "Charizard ex")],
                [Sv151Group],
                [Product(500, 23237, "Charizard ex - 006/165", "006/165")]);

            Assert.Empty(result.CardToProduct);
            Assert.Equal(2, result.Unmatched.Count);
            Assert.All(result.Unmatched, u => Assert.Contains("matches several of our cards", u.Reason));
        }

        [Fact]
        public void Cards_in_unmatched_sets_are_reported()
        {
            var result = TcgplayerCatalogMatcher.Match(
                [Set("zzz", "Unknown Set", "2001/01/01")], [new("zzz-1", "zzz", "1", "Bulbasaur")], [Sv151Group], []);

            Assert.Equal(2, result.Unmatched.Count);
            Assert.Contains(result.Unmatched, u => u.Kind == CatalogItemKind.Card && u.Reason.Contains("not matched"));
        }
    }
}
