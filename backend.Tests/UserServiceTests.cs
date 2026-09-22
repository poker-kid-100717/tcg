using PokemonTCG.API.Models;
using PokemonTCG.API.Services;

namespace PokemonTcgMarketplace.Backend.Tests
{
    public class UserServiceTests
    {
        [Fact]
        public async Task RegisterAsync_StoresBcryptHash_NeverThePlaintextPassword()
        {
            using var context = TestHelpers.CreateInMemoryContext();
            var service = new UserService(context);

            var user = await service.RegisterAsync("misty", "misty@pokemon.com", "starmie123");

            Assert.NotEqual("starmie123", user.PasswordHash);
            Assert.StartsWith("$2", user.PasswordHash); // bcrypt hash prefix
            Assert.True(BCrypt.Net.BCrypt.Verify("starmie123", user.PasswordHash));
        }

        [Fact]
        public async Task ValidatePasswordAsync_CorrectPassword_ReturnsTrue()
        {
            using var context = TestHelpers.CreateInMemoryContext();
            var service = new UserService(context);
            var user = await service.RegisterAsync("brock", "brock@pokemon.com", "onix456");

            var isValid = await service.ValidatePasswordAsync(user, "onix456");

            Assert.True(isValid);
        }

        [Fact]
        public async Task ValidatePasswordAsync_WrongPassword_ReturnsFalse()
        {
            using var context = TestHelpers.CreateInMemoryContext();
            var service = new UserService(context);
            var user = await service.RegisterAsync("brock", "brock@pokemon.com", "onix456");

            var isValid = await service.ValidatePasswordAsync(user, "wrong-password");

            Assert.False(isValid);
        }

        [Fact]
        public async Task GetByEmailAsync_UnknownEmail_ReturnsNull()
        {
            using var context = TestHelpers.CreateInMemoryContext();
            var service = new UserService(context);

            var user = await service.GetByEmailAsync("nobody@pokemon.com");

            Assert.Null(user);
        }

        [Fact]
        public async Task AddToWishlistAsync_SameCardTwice_DoesNotCreateDuplicate()
        {
            using var context = TestHelpers.CreateInMemoryContext();
            var service = new UserService(context);
            var user = await service.RegisterAsync("gary", "gary@pokemon.com", "blastoise456");

            await service.AddToWishlistAsync(user.Id, "swsh12-179");
            await service.AddToWishlistAsync(user.Id, "swsh12-179");
            var wishlist = await service.GetWishlistAsync(user.Id);

            Assert.Single(wishlist);
        }

        [Fact]
        public async Task RemoveFromWishlistAsync_ExistingItem_RemovesItAndReturnsTrue()
        {
            using var context = TestHelpers.CreateInMemoryContext();
            var service = new UserService(context);
            var user = await service.RegisterAsync("gary", "gary@pokemon.com", "blastoise456");
            await service.AddToWishlistAsync(user.Id, "swsh12-179");

            var removed = await service.RemoveFromWishlistAsync(user.Id, "swsh12-179");
            var wishlist = await service.GetWishlistAsync(user.Id);

            Assert.True(removed);
            Assert.Empty(wishlist);
        }

        [Fact]
        public async Task RemoveFromWishlistAsync_MissingItem_ReturnsFalse()
        {
            using var context = TestHelpers.CreateInMemoryContext();
            var service = new UserService(context);
            var user = await service.RegisterAsync("gary", "gary@pokemon.com", "blastoise456");

            var removed = await service.RemoveFromWishlistAsync(user.Id, "does-not-exist");

            Assert.False(removed);
        }

        [Fact]
        public async Task CreateOrderAsync_ThenGetOrdersAsync_ReturnsOrderWithItems()
        {
            using var context = TestHelpers.CreateInMemoryContext();
            var service = new UserService(context);
            var user = await service.RegisterAsync("ash", "ash@pokemon.com", "pikachu123");

            var items = new List<OrderItem>
            {
                new OrderItem { CardId = "sv4-172", CardName = "Charizard", CardImage = "img.png", Price = 89.99m, Quantity = 1 }
            };

            var order = await service.CreateOrderAsync(user.Id, 89.99m, items);
            var orders = await service.GetOrdersAsync(user.Id);

            Assert.Single(orders);
            Assert.Equal(order.Id, orders[0].Id);
            Assert.Equal("Pending", orders[0].Status);
            Assert.Single(orders[0].Items);
            Assert.Equal("sv4-172", orders[0].Items[0].CardId);
        }

        [Fact]
        public async Task GetOrdersAsync_OrdersFromOtherUsers_AreNotReturned()
        {
            using var context = TestHelpers.CreateInMemoryContext();
            var service = new UserService(context);
            var userA = await service.RegisterAsync("ash", "ash@pokemon.com", "pikachu123");
            var userB = await service.RegisterAsync("gary", "gary@pokemon.com", "blastoise456");

            await service.CreateOrderAsync(userA.Id, 10m, new List<OrderItem>());
            var ordersForB = await service.GetOrdersAsync(userB.Id);

            Assert.Empty(ordersForB);
        }
    }
}
