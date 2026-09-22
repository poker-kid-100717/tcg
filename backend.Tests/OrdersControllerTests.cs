using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using PokemonTCG.API.Controllers;
using PokemonTCG.API.Services;

namespace PokemonTcgMarketplace.Backend.Tests
{
    public class OrdersControllerTests
    {
        private static OrdersController CreateController(UserService userService, int userId)
        {
            var controller = new OrdersController(userService, NullLogger<OrdersController>.Instance);

            var claimsPrincipal = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId.ToString())
            }, "TestAuth"));

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = claimsPrincipal }
            };

            return controller;
        }

        [Fact]
        public async Task CreateOrder_ValidRequest_ReturnsOkWithPersistedOrder()
        {
            var context = TestHelpers.CreateInMemoryContext();
            var userService = new UserService(context);
            var user = await userService.RegisterAsync("ash", "ash@pokemon.com", "pikachu123");
            var controller = CreateController(userService, user.Id);

            var request = new CreateOrderRequest
            {
                Total = 89.99m,
                Items = new List<OrderItemRequest>
                {
                    new OrderItemRequest { CardId = "sv4-172", CardName = "Charizard ex", CardImage = "img.png", Price = 89.99m, Quantity = 1 }
                }
            };

            var result = await controller.CreateOrder(request);

            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(ok.Value);
        }

        [Fact]
        public async Task CreateOrder_ThenGetOrders_ReturnsTheOrderThatWasJustCreated()
        {
            var context = TestHelpers.CreateInMemoryContext();
            var userService = new UserService(context);
            var user = await userService.RegisterAsync("ash", "ash@pokemon.com", "pikachu123");
            var controller = CreateController(userService, user.Id);

            await controller.CreateOrder(new CreateOrderRequest
            {
                Total = 15.5m,
                Items = new List<OrderItemRequest>
                {
                    new OrderItemRequest { CardId = "sv3-151", CardName = "Pikachu", CardImage = "img.png", Price = 15.5m, Quantity = 1 }
                }
            });

            var result = await controller.GetOrders();

            var ok = Assert.IsType<OkObjectResult>(result);
            var orders = Assert.IsAssignableFrom<System.Collections.IEnumerable>(ok.Value).Cast<object>().ToList();
            Assert.Single(orders);
        }

        [Fact]
        public async Task GetOrders_NoOrdersYet_ReturnsEmptyList()
        {
            var context = TestHelpers.CreateInMemoryContext();
            var userService = new UserService(context);
            var user = await userService.RegisterAsync("ash", "ash@pokemon.com", "pikachu123");
            var controller = CreateController(userService, user.Id);

            var result = await controller.GetOrders();

            var ok = Assert.IsType<OkObjectResult>(result);
            var orders = Assert.IsAssignableFrom<System.Collections.IEnumerable>(ok.Value).Cast<object>().ToList();
            Assert.Empty(orders);
        }
    }
}
