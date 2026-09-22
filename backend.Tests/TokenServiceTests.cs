using PokemonTCG.API.Models;
using PokemonTCG.API.Services;

namespace PokemonTcgMarketplace.Backend.Tests
{
    public class TokenServiceTests
    {
        private static User CreateUser(int id = 1) => new User
        {
            Id = id,
            Username = "ash",
            Email = "ash@pokemon.com",
            PasswordHash = "irrelevant-for-this-test"
        };

        [Fact]
        public void GenerateJwtToken_ThenValidate_RoundTripsToSameUserId()
        {
            var tokenService = new TokenService(TestHelpers.CreateJwtConfiguration());
            var user = CreateUser(42);

            var token = tokenService.GenerateJwtToken(user);
            var userId = tokenService.ValidateJwtToken(token);

            Assert.NotNull(token);
            Assert.NotEmpty(token);
            Assert.Equal(42, userId);
        }

        [Fact]
        public void ValidateJwtToken_GarbageToken_ReturnsNull()
        {
            var tokenService = new TokenService(TestHelpers.CreateJwtConfiguration());

            var userId = tokenService.ValidateJwtToken("not-a-real-jwt");

            Assert.Null(userId);
        }

        [Fact]
        public void ValidateJwtToken_TokenSignedWithDifferentKey_ReturnsNull()
        {
            var issuerConfig = TestHelpers.CreateJwtConfiguration(key: "key-number-one-at-least-32-bytes-long!!");
            var otherConfig = TestHelpers.CreateJwtConfiguration(key: "a-totally-different-key-32-bytes-long!!");

            var issuingService = new TokenService(issuerConfig);
            var validatingService = new TokenService(otherConfig);

            var token = issuingService.GenerateJwtToken(CreateUser());
            var userId = validatingService.ValidateJwtToken(token);

            Assert.Null(userId);
        }
    }
}
