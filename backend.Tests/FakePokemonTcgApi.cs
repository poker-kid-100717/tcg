using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PokemonTcgMarketplace.Backend.Tests
{
    /// <summary>
    /// Stands in for api.pokemontcg.io and for TCGCSV (tcgcsv.test): serves sets and cards in the
    /// Pokémon TCG API's v2 format, and the same sets, cards and prices as TCGCSV groups, products and
    /// prices, plus any sealed product added with <see cref="AddSealed"/>. Records every request across
    /// both, so failures can be injected part-way through a run. Prices can be changed between snapshot
    /// runs to simulate the market moving.
    /// </summary>
    public class FakePokemonTcgApi : HttpMessageHandler
    {
        public ConcurrentQueue<Uri> Requests { get; } = new();
        public bool Fail { get; set; }
        /// <summary>When set, every request after this many (counted across the fake's lifetime) fails.</summary>
        public int? FailAfterRequests { get; set; }

        private readonly List<JsonObject> _sets = [];
        private readonly List<JsonObject> _cards = [];
        private readonly Dictionary<string, int> _productIds = [];
        private readonly List<Sealed> _sealed = [];
        private int _nextProductId = 500_000;

        private sealed class Sealed(JsonObject set, int productId, string name, decimal? market)
        {
            public JsonObject Set { get; } = set;
            public int ProductId { get; } = productId;
            public string Name { get; } = name;
            public decimal? Market { get; set; } = market;
        }

        /// <summary>A sealed product listed in the set's TCGCSV group. Returns its TCGplayer product id.</summary>
        public int AddSealed(JsonObject set, string name, decimal? market)
        {
            var productId = _nextProductId++;
            _sealed.Add(new Sealed(set, productId, name, market));
            return productId;
        }

        public void SetSealedPrice(int productId, decimal? market) => _sealed.Single(p => p.ProductId == productId).Market = market;

        public JsonObject AddSet(string id, string name, string series, DateOnly released, int total, string? code = null)
        {
            var set = new JsonObject
            {
                ["id"] = id, ["name"] = name, ["series"] = series, ["printedTotal"] = total, ["total"] = total,
                ["releaseDate"] = released.ToString("yyyy/MM/dd"), ["ptcgoCode"] = code ?? id.ToUpperInvariant(),
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
            _productIds[id] = _nextProductId++;
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
            if (Fail || Requests.Count > FailAfterRequests) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

            var path = uri.AbsolutePath.TrimEnd('/');
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            if (uri.Host == "tcgcsv.test") return Tcgcsv(path);

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

        // TCGCSV: /tcgplayer/3/groups, /tcgplayer/3/{groupId}/products, /tcgplayer/3/{groupId}/prices.
        // Each set is one group, named the way TCGplayer names them ("TS: Market Set").
        private Task<HttpResponseMessage> Tcgcsv(string path)
        {
            var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts is ["tcgplayer", "3", "groups"])
                return TcgcsvResults(_sets.Select(g => new JsonObject
                {
                    ["groupId"] = GroupId(g), ["name"] = $"TS: {g["name"]}", ["abbreviation"] = (string?)g["ptcgoCode"],
                    ["isSupplemental"] = false, ["publishedOn"] = $"{DateOnly.ParseExact((string)g["releaseDate"]!, "yyyy/MM/dd"):yyyy-MM-dd}T00:00:00",
                    ["modifiedOn"] = "2026-01-01T00:00:00", ["categoryId"] = 3,
                }));
            if (parts is not ["tcgplayer", "3", var groupText, var resource] || !int.TryParse(groupText, out var groupId))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            var set = _sets.FirstOrDefault(candidate => GroupId(candidate) == groupId);
            if (set is null) return TcgcsvResults([]);
            var cards = _cards.Where(c => (string)c["set"]!["id"]! == (string)set["id"]!).ToList();
            var sealedProducts = _sealed.Where(p => p.Set == set).ToList();

            if (resource == "products")
                return TcgcsvResults(cards.Select(c => new JsonObject
                {
                    ["productId"] = _productIds[(string)c["id"]!], ["name"] = $"{c["name"]} - {Printed(c)}", ["groupId"] = groupId,
                    ["imageUrl"] = $"https://tcgplayer-cdn.test/{c["id"]}.jpg", ["url"] = $"https://www.tcgplayer.test/product/{_productIds[(string)c["id"]!]}",
                    ["extendedData"] = new JsonArray(
                        new JsonObject { ["name"] = "Number", ["displayName"] = "Card Number", ["value"] = Printed(c) },
                        new JsonObject { ["name"] = "Rarity", ["displayName"] = "Rarity", ["value"] = (string?)c["rarity"] }),
                }).Concat(sealedProducts.Select(p => new JsonObject
                {
                    ["productId"] = p.ProductId, ["name"] = p.Name, ["groupId"] = groupId,
                    ["imageUrl"] = $"https://tcgplayer-cdn.test/{p.ProductId}.jpg", ["url"] = $"https://www.tcgplayer.test/product/{p.ProductId}",
                    ["presaleInfo"] = new JsonObject { ["isPresale"] = false, ["releasedOn"] = null, ["note"] = null },
                    ["extendedData"] = new JsonArray(),
                })));

            if (resource == "prices")
                return TcgcsvResults(cards.SelectMany(c =>
                    ((JsonObject?)c["tcgplayer"]?["prices"] ?? new JsonObject()).Select(price => new JsonObject
                    {
                        ["productId"] = _productIds[(string)c["id"]!],
                        ["lowPrice"] = price.Value!["low"]?.DeepClone(), ["midPrice"] = price.Value!["mid"]?.DeepClone(),
                        ["highPrice"] = price.Value!["high"]?.DeepClone(), ["marketPrice"] = price.Value!["market"]?.DeepClone(),
                        ["directLowPrice"] = null, ["subTypeName"] = SubTypeName(price.Key),
                    }))
                    .Concat(sealedProducts.Select(p => new JsonObject
                    {
                        ["productId"] = p.ProductId, ["lowPrice"] = p.Market * 0.9m, ["midPrice"] = p.Market,
                        ["highPrice"] = p.Market * 2m, ["marketPrice"] = p.Market, ["directLowPrice"] = null, ["subTypeName"] = "Normal",
                    })));

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private int GroupId(JsonObject set) => 1000 + _sets.IndexOf(set);

        /// <summary>TCGplayer pads numbers and adds the set size: "1" in a 4-card set is "001/004".</summary>
        private static string Printed(JsonObject card)
        {
            var number = (string)card["number"]!;
            var total = (int)card["set"]!["printedTotal"]!;
            return int.TryParse(number, out var n) ? $"{n:000}/{total:000}" : $"{number}/{total:000}";
        }

        /// <summary>The site's variant keys back to TCGplayer's printing names: "reverseHolofoil" → "Reverse Holofoil".</summary>
        private static string SubTypeName(string variant)
        {
            var builder = new System.Text.StringBuilder();
            foreach (var c in variant)
            {
                if (char.IsUpper(c) && builder.Length > 0) builder.Append(' ');
                builder.Append(builder.Length == 0 ? char.ToUpperInvariant(c) : c);
            }
            return builder.ToString();
        }

        private static Task<HttpResponseMessage> TcgcsvResults(IEnumerable<JsonObject> items) =>
            Json(new JsonObject { ["success"] = true, ["errors"] = new JsonArray(), ["results"] = new JsonArray(items.Select(i => (JsonNode)i).ToArray()) });

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
