using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using PokemonTCG.API.Controllers;
using PokemonTCG.API.Services;

namespace PokemonTcgMarketplace.Backend.Tests
{
    public class AuthControllerTests
    {
        private static AuthController CreateController(out UserService userService)
        {
            var context = TestHelpers.CreateInMemoryContext();
            userService = new UserService(context);
            var tokenService = new TokenService(TestHelpers.CreateJwtConfiguration());
            return new AuthController(userService, tokenService, NullLogger<AuthController>.Instance);
        }

        [Fact]
        public async Task Register_NewUser_ReturnsOkWithJwtToken()
        {
            var controller = CreateController(out _);

            var result = await controller.Register(new RegisterRequest
            {
                Username = "misty",
                Email = "misty@pokemon.com",
                Password = "starmie123"
            });

            var ok = Assert.IsType<OkObjectResult>(result);
            var token = ok.Value?.GetType().GetProperty("token")?.GetValue(ok.Value) as string;
            Assert.False(string.IsNullOrEmpty(token));
        }

        [Fact]
        public async Task Register_DuplicateEmail_ReturnsBadRequest()
        {
            var controller = CreateController(out _);
            await controller.Register(new RegisterRequest { Username = "misty", Email = "misty@pokemon.com", Password = "starmie123" });

            var result = await controller.Register(new RegisterRequest { Username = "misty2", Email = "misty@pokemon.com", Password = "different1" });

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task Register_DuplicateUsername_ReturnsBadRequest()
        {
            var controller = CreateController(out _);
            await controller.Register(new RegisterRequest { Username = "misty", Email = "misty@pokemon.com", Password = "starmie123" });

            var result = await controller.Register(new RegisterRequest { Username = "misty", Email = "other@pokemon.com", Password = "different1" });

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task Login_ValidCredentials_ReturnsOkWithJwtToken()
        {
            var controller = CreateController(out _);
            await controller.Register(new RegisterRequest { Username = "misty", Email = "misty@pokemon.com", Password = "starmie123" });

            var result = await controller.Login(new LoginRequest { Email = "misty@pokemon.com", Password = "starmie123" });

            var ok = Assert.IsType<OkObjectResult>(result);
            var token = ok.Value?.GetType().GetProperty("token")?.GetValue(ok.Value) as string;
            Assert.False(string.IsNullOrEmpty(token));
        }

        [Fact]
        public async Task Login_WrongPassword_ReturnsBadRequest()
        {
            var controller = CreateController(out _);
            await controller.Register(new RegisterRequest { Username = "misty", Email = "misty@pokemon.com", Password = "starmie123" });

            var result = await controller.Login(new LoginRequest { Email = "misty@pokemon.com", Password = "wrong-password" });

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task Login_UnknownEmail_ReturnsBadRequest()
        {
            var controller = CreateController(out _);

            var result = await controller.Login(new LoginRequest { Email = "nobody@pokemon.com", Password = "whatever1" });

            Assert.IsType<BadRequestObjectResult>(result);
        }
    }
}
