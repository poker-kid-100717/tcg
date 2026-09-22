using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace PokemonTCG.API.Services
{
    public class PokemonTcgService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly IMemoryCache _cache;
        private readonly ILogger<PokemonTcgService> _logger;

        public PokemonTcgService(
            HttpClient httpClient,
            IConfiguration configuration,
            IMemoryCache cache,
            ILogger<PokemonTcgService> logger)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _cache = cache;
            _logger = logger;

            // Add API key if available
            var apiKey = _configuration["PokemonTcgApi:ApiKey"];
            if (!string.IsNullOrEmpty(apiKey))
            {
                _httpClient.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
            }
        }

        public async Task<JsonDocument> GetCardsAsync(Dictionary<string, string>? queryParams = null)
        {
            // Build the cache key from the query parameters
            string cacheKey = "cards";
            if (queryParams != null && queryParams.Count > 0)
            {
                cacheKey = $"cards_{string.Join("_", queryParams.Select(kv => $"{kv.Key}={kv.Value}"))}";
            }

            // Try to get from cache first
            if (_cache.TryGetValue(cacheKey, out JsonDocument? cachedResult) && cachedResult != null)
            {
                return cachedResult;
            }

            // Build the query string
            var query = string.Empty;
            if (queryParams != null && queryParams.Count > 0)
            {
                query = $"?{string.Join("&", queryParams.Select(kv => $"{kv.Key}={kv.Value}"))}";
            }

            try
            {
                var response = await _httpClient.GetAsync($"cards{query}");
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStreamAsync();
                var result = await JsonDocument.ParseAsync(content);

                // Cache the result for 5 minutes
                _cache.Set(cacheKey, result, TimeSpan.FromMinutes(5));

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching cards from Pokemon TCG API");
                throw;
            }
        }

        public async Task<JsonDocument> GetCardByIdAsync(string id)
        {
            string cacheKey = $"card_{id}";

            // Try to get from cache first
            if (_cache.TryGetValue(cacheKey, out JsonDocument? cachedResult) && cachedResult != null)
            {
                return cachedResult;
            }

            try
            {
                var response = await _httpClient.GetAsync($"cards/{id}");
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStreamAsync();
                var result = await JsonDocument.ParseAsync(content);

                // Cache the result for 1 hour as individual cards don't change often
                _cache.Set(cacheKey, result, TimeSpan.FromHours(1));

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error fetching card with ID {id} from Pokemon TCG API");
                throw;
            }
        }

        public async Task<JsonDocument> GetSetsAsync()
        {
            string cacheKey = "sets";

            // Try to get from cache first
            if (_cache.TryGetValue(cacheKey, out JsonDocument? cachedResult) && cachedResult != null)
            {
                return cachedResult;
            }

            try
            {
                var response = await _httpClient.GetAsync("sets");
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStreamAsync();
                var result = await JsonDocument.ParseAsync(content);

                // Cache the result for 24 hours as sets don't change often
                _cache.Set(cacheKey, result, TimeSpan.FromHours(24));

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching sets from Pokemon TCG API");
                throw;
            }
        }

        public async Task<JsonDocument> GetSetByIdAsync(string id)
        {
            string cacheKey = $"set_{id}";

            // Try to get from cache first
            if (_cache.TryGetValue(cacheKey, out JsonDocument? cachedResult) && cachedResult != null)
            {
                return cachedResult;
            }

            try
            {
                var response = await _httpClient.GetAsync($"sets/{id}");
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStreamAsync();
                var result = await JsonDocument.ParseAsync(content);

                // Cache the result for 24 hours as sets don't change often
                _cache.Set(cacheKey, result, TimeSpan.FromHours(24));

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error fetching set with ID {id} from Pokemon TCG API");
                throw;
            }
        }

        public async Task<JsonDocument> GetTypesAsync()
        {
            string cacheKey = "types";

            // Try to get from cache first
            if (_cache.TryGetValue(cacheKey, out JsonDocument? cachedResult) && cachedResult != null)
            {
                return cachedResult;
            }

            try
            {
                var response = await _httpClient.GetAsync("types");
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStreamAsync();
                var result = await JsonDocument.ParseAsync(content);

                // Cache the result indefinitely as types don't change
                _cache.Set(cacheKey, result, TimeSpan.FromDays(30));

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching types from Pokemon TCG API");
                throw;
            }
        }

        public async Task<JsonDocument> GetRaritiesAsync()
        {
            string cacheKey = "rarities";

            // Try to get from cache first
            if (_cache.TryGetValue(cacheKey, out JsonDocument? cachedResult) && cachedResult != null)
            {
                return cachedResult;
            }

            try
            {
                var response = await _httpClient.GetAsync("rarities");
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStreamAsync();
                var result = await JsonDocument.ParseAsync(content);

                // Cache the result indefinitely as rarities don't change
                _cache.Set(cacheKey, result, TimeSpan.FromDays(30));

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching rarities from Pokemon TCG API");
                throw;
            }
        }
    }
}