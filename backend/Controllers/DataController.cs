using Microsoft.AspNetCore.Mvc;
using PokemonTCG.API.Services;

namespace PokemonTCG.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DataController : ControllerBase
    {
        private readonly PokemonTcgService _pokemonTcgService;
        private readonly ILogger<DataController> _logger;

        public DataController(PokemonTcgService pokemonTcgService, ILogger<DataController> logger)
        {
            _pokemonTcgService = pokemonTcgService;
            _logger = logger;
        }

        [HttpGet("types")]
        public async Task<IActionResult> GetTypes()
        {
            try
            {
                var result = await _pokemonTcgService.GetTypesAsync();
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting types");
                return StatusCode(500, "An error occurred while fetching types");
            }
        }

        [HttpGet("rarities")]
        public async Task<IActionResult> GetRarities()
        {
            try
            {
                var result = await _pokemonTcgService.GetRaritiesAsync();
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting rarities");
                return StatusCode(500, "An error occurred while fetching rarities");
            }
        }

        [HttpGet("health")]
        public IActionResult HealthCheck()
        {
            return Ok(new { status = "healthy", timestamp = DateTime.UtcNow });
        }
    }
}