using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PokemonTCG.API.Services;
using System.Security.Claims;

namespace PokemonTCG.API.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class WishlistController : ControllerBase
    {
        private readonly IUserService _userService;
        private readonly PokemonTcgService _pokemonTcgService;
        private readonly ILogger<WishlistController> _logger;

        public WishlistController(
            IUserService userService,
            PokemonTcgService pokemonTcgService,
            ILogger<WishlistController> logger)
        {
            _userService = userService;
            _pokemonTcgService = pokemonTcgService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> GetWishlist()
        {
            try
            {
                var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
                var wishlist = await _userService.GetWishlistAsync(userId);

                // Fetch card details for each item in the wishlist
                var cardDetailTasks = wishlist.Select(async item =>
                {
                    try
                    {
                        var cardJson = await _pokemonTcgService.GetCardByIdAsync(item.CardId);
                        return new
                        {
                            wishlistItemId = item.Id,
                            cardId = item.CardId,
                            addedAt = item.AddedAt,
                            card = (object?)cardJson.RootElement.GetProperty("data")
                        };
                    }
                    catch
                    {
                        // If we can't fetch card details, return just the wishlist item
                        return new
                        {
                            wishlistItemId = item.Id,
                            cardId = item.CardId,
                            addedAt = item.AddedAt,
                            card = (object?)null
                        };
                    }
                }).ToList();

                var cardDetails = await Task.WhenAll(cardDetailTasks);
                return Ok(cardDetails);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting wishlist");
                return StatusCode(500, new { message = "An error occurred while fetching wishlist" });
            }
        }

        [HttpPost("{cardId}")]
        public async Task<IActionResult> AddToWishlist(string cardId)
        {
            try
            {
                var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
                
                // Verify card exists
                try
                {
                    await _pokemonTcgService.GetCardByIdAsync(cardId);
                }
                catch
                {
                    return NotFound(new { message = "Card not found" });
                }
                
                var wishlistItem = await _userService.AddToWishlistAsync(userId, cardId);
                
                return Ok(new
                {
                    wishlistItemId = wishlistItem.Id,
                    cardId = wishlistItem.CardId,
                    addedAt = wishlistItem.AddedAt
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error adding card {cardId} to wishlist");
                return StatusCode(500, new { message = "An error occurred while adding to wishlist" });
            }
        }

        [HttpDelete("{cardId}")]
        public async Task<IActionResult> RemoveFromWishlist(string cardId)
        {
            try
            {
                var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
                var result = await _userService.RemoveFromWishlistAsync(userId, cardId);
                
                if (!result)
                {
                    return NotFound(new { message = "Card not found in wishlist" });
                }
                
                return Ok(new { message = "Card removed from wishlist" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error removing card {cardId} from wishlist");
                return StatusCode(500, new { message = "An error occurred while removing from wishlist" });
            }
        }
    }
}