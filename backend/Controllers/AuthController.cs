using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PokemonTCG.API.Models;
using PokemonTCG.API.Services;
using System.Security.Claims;

namespace PokemonTCG.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IUserService _userService;
        private readonly ITokenService _tokenService;
        private readonly ILogger<AuthController> _logger;

        public AuthController(
            IUserService userService,
            ITokenService tokenService,
            ILogger<AuthController> logger)
        {
            _userService = userService;
            _tokenService = tokenService;
            _logger = logger;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            try
            {
                var user = await _userService.GetByEmailAsync(request.Email);

                if (user == null)
                {
                    return BadRequest(new { message = "Invalid email or password" });
                }

                var isValidPassword = await _userService.ValidatePasswordAsync(user, request.Password);
                if (!isValidPassword)
                {
                    return BadRequest(new { message = "Invalid email or password" });
                }

                var token = _tokenService.GenerateJwtToken(user);

                return Ok(new
                {
                    id = user.Id,
                    username = user.Username,
                    email = user.Email,
                    avatar = user.Avatar,
                    token
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during login");
                return StatusCode(500, new { message = "An error occurred during login" });
            }
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            try
            {
                var existingUserByEmail = await _userService.GetByEmailAsync(request.Email);
                if (existingUserByEmail != null)
                {
                    return BadRequest(new { message = "Email already in use" });
                }

                var existingUserByUsername = await _userService.GetByUsernameAsync(request.Username);
                if (existingUserByUsername != null)
                {
                    return BadRequest(new { message = "Username already in use" });
                }

                var user = await _userService.RegisterAsync(request.Username, request.Email, request.Password);
                var token = _tokenService.GenerateJwtToken(user);

                return Ok(new
                {
                    id = user.Id,
                    username = user.Username,
                    email = user.Email,
                    avatar = user.Avatar,
                    token
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during registration");
                return StatusCode(500, new { message = "An error occurred during registration" });
            }
        }

        [Authorize]
        [HttpGet("me")]
        public async Task<IActionResult> GetCurrentUser()
        {
            try
            {
                var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
                var user = await _userService.GetByIdAsync(userId);

                if (user == null)
                {
                    return NotFound(new { message = "User not found" });
                }

                return Ok(new
                {
                    id = user.Id,
                    username = user.Username,
                    email = user.Email,
                    avatar = user.Avatar
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting current user");
                return StatusCode(500, new { message = "An error occurred while fetching user data" });
            }
        }
    }

    public class LoginRequest
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    public class RegisterRequest
    {
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }
}