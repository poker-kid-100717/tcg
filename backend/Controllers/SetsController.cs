using Microsoft.AspNetCore.Mvc;
using PokemonTCG.API.Services;

namespace PokemonTCG.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SetsController : ControllerBase
    {
        private readonly PokemonTcgService _pokemonTcgService;
        private readonly ILogger<SetsController> _logger;

        public SetsController(PokemonTcgService pokemonTcgService, ILogger<SetsController> logger)
        {
            _pokemonTcgService = pokemonTcgService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> GetSets()
        {
            try
            {
                var result = await _pokemonTcgService.GetSetsAsync();
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting sets");
                return StatusCode(500, "An error occurred while fetching sets");
            }
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetSetById(string id)
        {
            try
            {
                var result = await _pokemonTcgService.GetSetByIdAsync(id);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting set with ID {id}");
                return StatusCode(500, $"An error occurred while fetching set with ID {id}");
            }
        }
    }
}