using Microsoft.AspNetCore.Mvc;
using PokemonTCG.API.Services;
using System.Text.Json;

namespace PokemonTCG.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CardsController : ControllerBase
    {
        private readonly PokemonTcgService _pokemonTcgService;
        private readonly ILogger<CardsController> _logger;

        public CardsController(PokemonTcgService pokemonTcgService, ILogger<CardsController> logger)
        {
            _pokemonTcgService = pokemonTcgService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> GetCards([FromQuery] Dictionary<string, string> queryParams)
        {
            try
            {
                var result = await _pokemonTcgService.GetCardsAsync(queryParams);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting cards");
                return StatusCode(500, "An error occurred while fetching cards");
            }
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetCardById(string id)
        {
            try
            {
                var result = await _pokemonTcgService.GetCardByIdAsync(id);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting card with ID {id}");
                return StatusCode(500, $"An error occurred while fetching card with ID {id}");
            }
        }

        [HttpGet("market-prices/{id}")]
        public async Task<IActionResult> GetCardMarketPrices(string id)
        {
            try
            {
                var cardJson = await _pokemonTcgService.GetCardByIdAsync(id);
                var card = cardJson.RootElement.GetProperty("data");

                if (card.TryGetProperty("tcgplayer", out var tcgplayer) && 
                    tcgplayer.TryGetProperty("prices", out var prices))
                {
                    return Ok(prices);
                }

                // If TCGPlayer prices aren't available, try cardmarket prices
                if (card.TryGetProperty("cardmarket", out var cardmarket) && 
                    cardmarket.TryGetProperty("prices", out var cardmarketPrices))
                {
                    return Ok(cardmarketPrices);
                }

                // If no price data is available
                return NotFound("No pricing data available for this card");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting market prices for card with ID {id}");
                return StatusCode(500, $"An error occurred while fetching market prices for card with ID {id}");
            }
        }

        [HttpGet("price-history/{id}")]
        public async Task<IActionResult> GetCardPriceHistory(string id)
        {
            try
            {
                // This is a mock endpoint that generates simulated price history
                // In a real app, this would fetch from a database or third-party API
                
                // Get basic card info to make the mock data more realistic
                var cardJson = await _pokemonTcgService.GetCardByIdAsync(id);
                var card = cardJson.RootElement.GetProperty("data");

                // Generate mock price history
                var today = DateTime.UtcNow;
                var priceHistory = new List<object>();
                
                // Use card data to seed the random generator for consistent results
                var cardName = card.GetProperty("name").GetString() ?? "";
                var seed = cardName.GetHashCode();
                var random = new Random(seed);
                
                // Determine a base price based on rarity or other factors
                double basePrice;
                if (card.TryGetProperty("tcgplayer", out var tcgplayer) && 
                    tcgplayer.TryGetProperty("prices", out var prices))
                {
                    // Try to get a real price if available
                    basePrice = GetBasePrice(prices);
                }
                else if (card.TryGetProperty("rarity", out var rarity))
                {
                    // Or base it on rarity
                    var rarityString = rarity.GetString() ?? "Common";
                    basePrice = GetBasePriceFromRarity(rarityString, random);
                }
                else
                {
                    // Default random price
                    basePrice = random.NextDouble() * 20 + 1; // $1 to $21
                }
                
                // Generate 12 months of price history
                for (int i = 11; i >= 0; i--)
                {
                    var date = today.AddMonths(-i);
                    
                    // Add some randomness to the price
                    var fluctuation = 0.85 + (random.NextDouble() * 0.3); // 0.85 to 1.15
                    
                    // Ensure some growth trend for card value over time (0.5-2% monthly growth)
                    var growth = 1 + ((random.NextDouble() * 1.5 + 0.5) / 100);
                    
                    basePrice = basePrice * fluctuation * growth;
                    
                    priceHistory.Add(new 
                    {
                        date = date.ToString("yyyy-MM-dd"),
                        price = Math.Round(basePrice, 2)
                    });
                }
                
                // Generate predicted prices for the next 6 months
                var lastPrice = basePrice;
                var predictedPrices = new List<object>();
                
                for (int i = 1; i <= 6; i++)
                {
                    var date = today.AddMonths(i);
                    
                    // Add some randomness and growth to the prediction
                    var randomFactor = 0.95 + (random.NextDouble() * 0.1); // 0.95 to 1.05
                    var growthFactor = 1 + (random.NextDouble() * 0.02); // 1% to 3% growth
                    
                    lastPrice = lastPrice * randomFactor * growthFactor;
                    
                    predictedPrices.Add(new 
                    {
                        date = date.ToString("yyyy-MM-dd"),
                        price = Math.Round(lastPrice, 2),
                        isProjection = true
                    });
                }
                
                return Ok(new 
                {
                    id,
                    priceHistory,
                    predictedPrices
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error generating price history for card with ID {id}");
                return StatusCode(500, $"An error occurred while generating price history for card with ID {id}");
            }
        }

        [HttpGet("investment-potential/{id}")]
        public async Task<IActionResult> GetInvestmentPotential(string id)
        {
            try
            {
                // This is a mock endpoint that generates simulated investment potential
                // In a real app, this would use ML models and more sophisticated analysis
                
                // Get card data to make the analysis more realistic
                var cardJson = await _pokemonTcgService.GetCardByIdAsync(id);
                var card = cardJson.RootElement.GetProperty("data");
                
                // Use card data to seed the random generator for consistent results
                var cardName = card.GetProperty("name").GetString() ?? "";
                var seed = cardName.GetHashCode();
                var random = new Random(seed);
                
                // Get card attributes to analyze
                string rarity = "";
                if (card.TryGetProperty("rarity", out var rarityElement))
                {
                    rarity = rarityElement.GetString() ?? "";
                }
                
                string releaseDate = "";
                if (card.TryGetProperty("set", out var setElement) && 
                    setElement.TryGetProperty("releaseDate", out var releaseDateElement))
                {
                    releaseDate = releaseDateElement.GetString() ?? "";
                }
                
                // Generate factors scores
                var rarityScore = GetRarityScore(rarity);
                var priceGrowthScore = 1.0 + (random.NextDouble() * 4.0); // 1.0 to 5.0
                var setRotationScore = GetSetRotationScore(releaseDate);
                var popularityScore = GetPopularityScore(cardName, random);
                
                // Calculate overall score (weighted average)
                var overallScore = (
                    rarityScore * 0.35 + 
                    priceGrowthScore * 0.30 + 
                    setRotationScore * 0.15 + 
                    popularityScore * 0.20
                );
                
                // Determine confidence level based on score variance
                var scores = new[] { rarityScore, priceGrowthScore, setRotationScore, popularityScore };
                var mean = scores.Average();
                var variance = scores.Select(x => Math.Pow(x - mean, 2)).Average();
                
                string confidenceLevel;
                if (variance < 0.5) confidenceLevel = "High";
                else if (variance < 1.0) confidenceLevel = "Medium";
                else confidenceLevel = "Low";
                
                return Ok(new 
                {
                    rating = Math.Round(overallScore, 1),
                    factors = new 
                    {
                        rarity = Math.Round(rarityScore, 1),
                        priceGrowth = Math.Round(priceGrowthScore, 1),
                        setRotation = Math.Round(setRotationScore, 1),
                        popularity = Math.Round(popularityScore, 1)
                    },
                    confidenceLevel
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error generating investment potential for card with ID {id}");
                return StatusCode(500, $"An error occurred while generating investment potential for card with ID {id}");
            }
        }

        private double GetBasePrice(JsonElement prices)
        {
            try
            {
                // Try various price points that might be available
                if (prices.TryGetProperty("holofoil", out var holofoil) && 
                    holofoil.TryGetProperty("market", out var holoMarket))
                {
                    return holoMarket.GetDouble();
                }
                else if (prices.TryGetProperty("normal", out var normal) && 
                         normal.TryGetProperty("market", out var normalMarket))
                {
                    return normalMarket.GetDouble();
                }
                else if (prices.TryGetProperty("reverseHolofoil", out var reverseHolo) && 
                         reverseHolo.TryGetProperty("market", out var reverseMarket))
                {
                    return reverseMarket.GetDouble();
                }
                else if (prices.TryGetProperty("1stEditionHolofoil", out var firstEd) && 
                         firstEd.TryGetProperty("market", out var firstEdMarket))
                {
                    return firstEdMarket.GetDouble();
                }
                
                // Default if we can't find a market price
                return new Random().NextDouble() * 20 + 1; // $1 to $21
            }
            catch
            {
                return new Random().NextDouble() * 20 + 1; // $1 to $21
            }
        }

        private double GetBasePriceFromRarity(string rarity, Random random)
        {
            var basePriceByRarity = new Dictionary<string, (double min, double max)>
            {
                { "Common", (0.5, 3.0) },
                { "Uncommon", (1.0, 5.0) },
                { "Rare", (2.0, 15.0) },
                { "Rare Holo", (5.0, 25.0) },
                { "Rare Ultra", (10.0, 50.0) },
                { "Rare Holo EX", (15.0, 80.0) },
                { "Rare Holo GX", (15.0, 80.0) },
                { "Rare Holo V", (10.0, 60.0) },
                { "Rare Holo VMAX", (20.0, 100.0) },
                { "Rare Holo VSTAR", (20.0, 100.0) },
                { "Rare Secret", (50.0, 200.0) },
                { "Rare Rainbow", (50.0, 200.0) },
                { "Rare Shiny", (30.0, 150.0) },
                { "Rare Shiny GX", (50.0, 200.0) },
                { "Promo", (5.0, 50.0) }
            };
            
            if (basePriceByRarity.TryGetValue(rarity, out var priceRange))
            {
                return priceRange.min + (random.NextDouble() * (priceRange.max - priceRange.min));
            }
            
            // Default if rarity is not recognized
            return random.NextDouble() * 20 + 1; // $1 to $21
        }

        private double GetRarityScore(string rarity)
        {
            var rarityScores = new Dictionary<string, double>
            {
                { "Common", 1.0 },
                { "Uncommon", 1.5 },
                { "Rare", 2.5 },
                { "Rare Holo", 3.0 },
                { "Rare Ultra", 3.5 },
                { "Rare Holo EX", 4.0 },
                { "Rare Holo GX", 4.0 },
                { "Rare Holo V", 4.0 },
                { "Rare Holo VMAX", 4.5 },
                { "Rare Holo VSTAR", 4.5 },
                { "Rare Secret", 5.0 },
                { "Rare Rainbow", 5.0 },
                { "Rare Shiny", 5.0 },
                { "Rare Shiny GX", 5.0 }
            };
            
            if (rarityScores.TryGetValue(rarity, out var score))
            {
                return score;
            }
            
            return 2.5; // Default if rarity is not recognized
        }

        private double GetSetRotationScore(string releaseDate)
        {
            if (string.IsNullOrEmpty(releaseDate))
                return 3.0; // Default score
            
            if (DateTime.TryParse(releaseDate, out var date))
            {
                var currentYear = DateTime.UtcNow.Year;
                var releaseYear = date.Year;
                var yearDiff = currentYear - releaseYear;
                
                // Newer sets score higher (more likely to be in standard format)
                if (yearDiff <= 1) return 4.5;
                if (yearDiff <= 2) return 4.0;
                if (yearDiff <= 3) return 3.5;
                if (yearDiff <= 5) return 3.0;
                if (yearDiff <= 10) return 2.5; // Vintage starting to have collector value
                if (yearDiff <= 15) return 3.0; // Vintage has more collector value
                if (yearDiff <= 20) return 3.5; // Older vintage has higher collector value
                return 4.0; // Very old cards have highest collector value
            }
            
            return 3.0; // Default if date parsing fails
        }

        private double GetPopularityScore(string cardName, Random random)
        {
            // Popular Pokémon tend to have higher popularity scores
            var popularPokemon = new[] 
            { 
                "charizard", "pikachu", "mew", "mewtwo", "blastoise", "venusaur", 
                "gengar", "lugia", "rayquaza", "jirachi", "umbreon", "espeon", "eevee",
                "tyranitar", "dragonite", "ho-oh", "alakazam", "zekrom", "reshiram"
            };
            
            // Check if card contains name of popular Pokémon
            var containsPopularPokemon = false;
            foreach (var pokemon in popularPokemon)
            {
                if (cardName.ToLower().Contains(pokemon))
                {
                    containsPopularPokemon = true;
                    break;
                }
            }
            
            // Base score is random between 1-4
            var baseScore = 1.0 + (random.NextDouble() * 3.0);
            
            // Boost for popular Pokémon
            if (containsPopularPokemon)
            {
                // Add 0.5-1.5 to the score
                baseScore += 0.5 + (random.NextDouble() * 1.0);
            }
            
            // Cap at 5.0
            return Math.Min(baseScore, 5.0);
        }
    }
}