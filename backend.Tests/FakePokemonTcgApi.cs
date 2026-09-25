using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PokemonTcgMarketplace.Backend.Tests
{
    /// <summary>
    /// Stands in for api.pokemontcg.io: serves sets and cards in the API's v2
    /// response format, supports the query forms the app sends, and records
    /// every request. Prices and their date can be changed between snapshot
    /// runs to simulate the market moving.
    /// </summary>
    public class FakePokemonTcgApi : HttpMessageHandler
    {
        public ConcurrentQueue<Uri> Requests { get; } = new();
        public bool Fail { get; set; }

        private readonly List<JsonObject> _sets = [];
        private readonly List<JsonObject> _cards = [];

        public JsonObject AddSet(string id, string name, string series, DateOnly released, int total)
        {
            var set = new JsonObject
            {
                ["id"] = id, ["name"] = name, ["series"] = series, ["printedTotal"] = total, ["total"] = total,
                ["releaseDate"] = released.ToString("yyyy/MM/dd"),
                ["images"] = new JsonObject { ["symbol"] = $"https://images.test/{id}/symbol.png", ["logo"] = $"https://images.test/{id}/logo.png" },
            };
            _sets.Add(set);
            return set;
        }

        public JsonObject AddCard(JsonObject set, string number, string name, string rarity, DateOnly pricesUpdated, params (string Variant, decimal Market)[] prices)
        {
            var id = $"{set["id"]}-{number}";
            var card = new JsonObject
            {
                ["id"] = id, ["name"] = name, ["supertype"] = "Pokémon", ["subtypes"] = new JsonArray("Basic"), ["hp"] = "70",
                ["types"] = new JsonArray("Fire"), ["nationalPokedexNumbers"] = new JsonArray(6), ["set"] = set.DeepClone(), ["number"] = number, ["artist"] = "Test Artist",
                ["rarity"] = rarity,
                ["images"] = new JsonObject { ["small"] = $"https://images.test/{id}.png", ["large"] = $"https://images.test/{id}_hires.png" },
            };
            _cards.Add(card);
            SetPrices(id, pricesUpdated, prices);
            return card;
        }

        public void SetPrices(string cardId, DateOnly updated, params (string Variant, decimal Market)[] prices)
        {
            var card = _cards.Single(c => (string)c["id"]! == cardId);
            var map = new JsonObject();
            foreach (var (variant, market) in prices)
            {
                map[variant] = new JsonObject { ["low"] = market * 0.8m, ["mid"] = market, ["high"] = market * 1.5m, ["market"] = market, ["directLow"] = null };
            }
            card["tcgplayer"] = new JsonObject
            {
                ["url"] = $"https://prices.pokemontcg.io/tcgplayer/{cardId}",
                ["updatedAt"] = updated.ToString("yyyy/MM/dd"),
                ["prices"] = map,
            };
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            Requests.Enqueue(uri);
            if (Fail) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

            var path = uri.AbsolutePath.TrimEnd('/');
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);

            if (path.EndsWith("/v2/sets")) return Json(Page(_sets, 1, 250));
            if (path.Contains("/v2/sets/")) return One(_sets, path[(path.LastIndexOf('/') + 1)..]);
            if (path.Contains("/v2/cards/")) return One(_cards, Uri.UnescapeDataString(path[(path.LastIndexOf('/') + 1)..]));
            if (path.EndsWith("/v2/cards"))
            {
                var page = int.Parse(query["page"] ?? "1");
                var size = int.Parse(query["pageSize"] ?? "250");
                return Json(Page(Filter(query["q"]), page, size));
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private IEnumerable<JsonObject> Filter(string? q)
        {
            if (q is null) return _cards;
            var term = q[(q.IndexOf(':') + 1)..].Trim('"');
            if (q.StartsWith("set.id:")) return _cards.Where(c => (string)c["set"]!["id"]! == term);
            if (q.StartsWith("name:"))
            {
                var prefix = term.TrimEnd('*');
                return _cards.Where(c => ((string)c["name"]!).StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            }
            throw new InvalidOperationException($"Unexpected query: {q}");
        }

        private static JsonObject Page(IEnumerable<JsonObject> items, int page, int size)
        {
            var all = items.ToList();
            var slice = all.Skip((page - 1) * size).Take(size).Select(i => (JsonNode)i.DeepClone()).ToArray();
            return new JsonObject { ["data"] = new JsonArray(slice), ["page"] = page, ["pageSize"] = size, ["count"] = slice.Length, ["totalCount"] = all.Count };
        }

        private static Task<HttpResponseMessage> One(List<JsonObject> items, string id) =>
            items.FirstOrDefault(i => (string)i["id"]! == id) is { } item
                ? Json(new JsonObject { ["data"] = item.DeepClone() })
                : Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

        private static Task<HttpResponseMessage> Json(JsonNode body) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body.ToJsonString(new JsonSerializerOptions()), System.Text.Encoding.UTF8, "application/json"),
            });
    }
}
