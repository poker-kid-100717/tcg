using PokemonTCG.API.Pricing;

namespace PokemonTcgMarketplace.Backend.Tests;

public class TcgcsvMappingTests
{
    private static TcgSet Set(string id, string name, string released, string? code) =>
        new(id, name, "Series", 100, 100, released, null, code);

    private static TcgcsvGroup Group(int id, string name, string? abbreviation, string published) =>
        new(id, name, abbreviation, false, $"{published}T00:00:00");

    [Theory]
    [InlineData("006/165", "6")]
    [InlineData("6", "6")]
    [InlineData("199/165", "199")]
    [InlineData("100", "100")]
    [InlineData("TG01/TG30", "TG1")]
    [InlineData("TG01", "TG1")]
    [InlineData("SV001/SV122", "SV1")]
    [InlineData("gg05", "GG5")]
    public void Printed_numbers_compare_without_set_size_or_padding(string number, string normalized) =>
        Assert.Equal(normalized, TcgcsvMapping.NormalizeNumber(number));

    [Theory]
    [InlineData("Normal", "normal")]
    [InlineData("Holofoil", "holofoil")]
    [InlineData("Reverse Holofoil", "reverseHolofoil")]
    [InlineData("1st Edition Holofoil", "1stEditionHolofoil")]
    [InlineData(null, "normal")]
    public void Printing_names_become_the_sites_variant_keys(string? subType, string key) =>
        Assert.Equal(key, TcgcsvMapping.VariantKey(subType));

    [Fact]
    public void Groups_pair_with_sets_by_code_date_and_name()
    {
        var sets = new[]
        {
            Set("sv3pt5", "151", "2023/09/22", "MEW"),
            Set("sv8pt5", "Prismatic Evolutions", "2025/01/17", "PRE"),
        };
        var groups = new[]
        {
            Group(23237, "SV: Scarlet & Violet 151", "MEW", "2023-09-22"),
            Group(23821, "SV: Prismatic Evolutions", "PRE", "2025-01-17"),
            Group(99999, "Miscellaneous Cards & Products", "MCAP", "2000-01-01"),
        };

        var matched = TcgcsvMapping.MatchSets(groups, sets);

        Assert.Equal("sv3pt5", matched[23237].Id);
        Assert.Equal("sv8pt5", matched[23821].Id);
        Assert.False(matched.ContainsKey(99999));
    }

    [Fact]
    public void A_sub_set_sharing_the_code_and_date_cannot_take_the_main_sets_cards()
    {
        var sets = new[] { Set("me30", "30th Celebration", "2026/09/16", "30C") };
        var groups = new[]
        {
            Group(24837, "ME: 30th Celebration Classic Collection", "30C", "2026-09-16"),
            Group(24722, "ME: 30th Celebration", "30C", "2026-09-16"),
        };

        var matched = TcgcsvMapping.MatchSets(groups, sets);

        Assert.Equal("me30", matched[24722].Id);
        Assert.False(matched.ContainsKey(24837));
    }

    [Fact]
    public void A_weak_match_is_left_unpaired()
    {
        // Same release date and a name that merely contains the set's: not enough to trust.
        var sets = new[] { Set("base1", "Base", "1999/01/09", null) };
        var groups = new[] { Group(604, "Base Set (Shadowless)", "BS", "1999-01-09") };

        Assert.Empty(TcgcsvMapping.MatchSets(groups, sets));
    }

    [Theory]
    [InlineData("Ascended Heroes Elite Trainer Box", "Elite Trainer Box")]
    [InlineData("Ascended Heroes Pokemon Center Elite Trainer Box", "Pokémon Center ETB")]
    [InlineData("Ascended Heroes Elite Trainer Box Case", "ETB Case")]
    [InlineData("30th Celebration Booster Bundle", "Booster Bundle")]
    [InlineData("Surging Sparks Booster Box", "Booster Box")]
    [InlineData("Surging Sparks Booster Pack", "Booster Pack")]
    [InlineData("Prismatic Evolutions Booster Pack Art Bundle [Set of 4]", "Booster Pack Art Bundle")]
    [InlineData("30th Celebration Ultra-Premium Collection", "Ultra-Premium Collection")]
    [InlineData("30th Celebration Mini Tin [Day]", "Mini Tin")]
    [InlineData("Chaos Rising Build & Battle Box", "Build & Battle Box")]
    [InlineData("Paldean Fates 3 Pack Blister", "Blister")]
    [InlineData("Charizard ex Premium Collection", "Collection")]
    [InlineData("Costco Pokemon 30th Celebration 2-Pack Trainer Box and Booster Bundle", "Retail Bundle")]
    [InlineData("Ascended Heroes Elite Trainer Box and Pokeball (Sam's Club)", "Retail Bundle")]
    [InlineData("Code Card - Surging Sparks Booster Pack", null)]
    [InlineData("Ultra Pro Card Sleeves", null)]
    public void Sealed_products_are_classified_by_name(string name, string? type) =>
        Assert.Equal(type, SealedProducts.Classify(name));

    [Fact]
    public void Typical_msrp_applies_only_to_current_era_products_of_a_known_type()
    {
        Assert.Equal(49.99m, SealedProducts.TypicalMsrp("Elite Trainer Box", new DateOnly(2026, 1, 30)));
        Assert.Equal(26.94m, SealedProducts.TypicalMsrp("Booster Bundle", new DateOnly(2026, 9, 16)));
        Assert.Null(SealedProducts.TypicalMsrp("Elite Trainer Box", new DateOnly(2021, 11, 12)));
        Assert.Null(SealedProducts.TypicalMsrp("Collection", new DateOnly(2026, 1, 30)));
        Assert.Null(SealedProducts.TypicalMsrp("Elite Trainer Box", null));
    }
}
