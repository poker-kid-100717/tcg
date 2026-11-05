using PokemonTCG.API.Data;
using PokemonTCG.API.Pricing.Tcgplayer;

namespace PokemonTcgMarketplace.Backend.Tests.Tcgplayer
{
    public class TcgplayerMappingTests
    {
        [Theory]
        [InlineData("Normal", "normal")]
        [InlineData("Holofoil", "holofoil")]
        [InlineData("Reverse Holofoil", "reverseHolofoil")]
        [InlineData("1st Edition Holofoil", "1stEditionHolofoil")]
        [InlineData("1st Edition Normal", "1stEditionNormal")]
        [InlineData("Unlimited Holofoil", "unlimitedHolofoil")]
        [InlineData("Unlimited", "unlimited")]
        [InlineData("1st Edition", "1stEdition")]
        [InlineData("  reverse holofoil ", "reverseHolofoil")]
        [InlineData("Cosmos Holofoil", "cosmosHolofoil")]
        [InlineData("Poké Ball Pattern", "pokéBallPattern")]
        [InlineData("Master-Ball Pattern", "masterBallPattern")]
        public void Variant_keys(string subTypeName, string expected) =>
            Assert.Equal(expected, TcgplayerVariants.ToVariantKey(subTypeName));

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void Variant_key_requires_a_name(string subTypeName) =>
            Assert.ThrowsAny<ArgumentException>(() => TcgplayerVariants.ToVariantKey(subTypeName));

        [Theory]
        [InlineData("Near Mint", CardCondition.NearMint)]
        [InlineData("Lightly Played", CardCondition.LightlyPlayed)]
        [InlineData("Moderately Played", CardCondition.ModeratelyPlayed)]
        [InlineData("Heavily Played", CardCondition.HeavilyPlayed)]
        [InlineData("Damaged", CardCondition.Damaged)]
        [InlineData("Near Mint Holofoil", CardCondition.NearMint)]
        [InlineData("Lightly Played Foil", CardCondition.LightlyPlayed)]
        [InlineData("Damaged Holofoil", CardCondition.Damaged)]
        [InlineData("near mint", CardCondition.NearMint)]
        [InlineData(" Heavily  Played ", CardCondition.HeavilyPlayed)]
        [InlineData("NM", CardCondition.NearMint)]
        [InlineData("DMG", CardCondition.Damaged)]
        public void Conditions(string name, CardCondition expected)
        {
            Assert.True(TcgplayerConditions.TryParse(name, out var condition));
            Assert.Equal(expected, condition);
            Assert.Equal(expected, TcgplayerConditions.ToCondition(name));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("Unopened")]
        [InlineData("Mint")]
        [InlineData("Holofoil")]
        public void Unknown_conditions(string? name)
        {
            Assert.False(TcgplayerConditions.TryParse(name, out _));
            Assert.Null(TcgplayerConditions.ToCondition(name));
        }
    }
}
