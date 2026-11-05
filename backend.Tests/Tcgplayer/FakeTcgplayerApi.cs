using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using PokemonTCG.API.Pricing.Tcgplayer;

namespace PokemonTcgMarketplace.Backend.Tests.Tcgplayer
{
    /// <summary>
    /// Stands in for api.tcgplayer.com: issues OAuth tokens, rejects requests
    /// without a live bearer token, and serves the catalog and pricing
    /// endpoints in TCGplayer's { success, errors, totalItems, results }
    /// envelope with offset/limit paging. Records every request.
    /// </summary>
    public class FakeTcgplayerApi : HttpMessageHandler
    {
        public const string PublicKey = "public-key";
        public const string PrivateKey = "private-key";
        public const string Version = "v1.39.0";

        public ConcurrentQueue<HttpRequestMessage> Requests { get; } = new();
        public List<Dictionary<string, string>> TokenRequests { get; } = [];
        public List<string> IssuedTokens { get; } = [];
        public HashSet<string> RevokedTokens { get; } = [];

        /// <summary>expires_in for issued tokens, in seconds.</summary>
        public int ExpiresIn { get; set; } = 1209599;

        /// <summary>Largest limit honoured by paged endpoints (the real API caps at 100).</summary>
        public int MaxLimit { get; set; } = 100;

        /// <summary>When set, answers every API (non-token) request instead of the built-in routes.</summary>
        public Func<HttpRequestMessage, HttpResponseMessage>? Override { get; set; }

        public List<JsonObject> Groups { get; } = [];
        public List<JsonObject> Products { get; } = [];
        public List<JsonObject> ProductPrices { get; } = [];
        public List<JsonObject> Skus { get; } = [];
        public List<JsonObject> SkuPrices { get; } = [];

        public IEnumerable<HttpRequestMessage> ApiRequests => Requests.Where(r => !r.RequestUri!.AbsolutePath.EndsWith("/token"));

        public JsonObject AddGroup(int groupId, string name, DateTime publishedOn, string? abbreviation = null)
        {
            var group = new JsonObject
            {
                ["groupId"] = groupId, ["name"] = name, ["abbreviation"] = abbreviation, ["isSupplemental"] = false,
                ["publishedOn"] = publishedOn.ToString("yyyy-MM-ddTHH:mm:ss"), ["modifiedOn"] = "2025-01-01T12:00:00.123", ["categoryId"] = 3,
            };
            Groups.Add(group);
            return group;
        }

        public JsonObject AddProduct(int productId, int groupId, string name, string? number, string rarity = "Common")
        {
            var extended = new JsonArray();
            if (number is not null) extended.Add(new JsonObject { ["name"] = "Number", ["displayName"] = "Card Number", ["value"] = number });
            extended.Add(new JsonObject { ["name"] = "Rarity", ["displayName"] = "Rarity", ["value"] = rarity });
            var product = new JsonObject
            {
                ["productId"] = productId, ["name"] = name, ["cleanName"] = name, ["imageUrl"] = $"https://tcgplayer-cdn.test/{productId}.jpg",
                ["categoryId"] = 3, ["groupId"] = groupId, ["url"] = $"https://www.tcgplayer.com/product/{productId}",
                ["modifiedOn"] = "2025-01-01T12:00:00", ["extendedData"] = extended,
            };
            Products.Add(product);
            return product;
        }

        public void AddProductPrice(int productId, string subTypeName, decimal market) =>
            ProductPrices.Add(new JsonObject
            {
                ["productId"] = productId, ["lowPrice"] = market * 0.8m, ["midPrice"] = market, ["highPrice"] = market * 2,
                ["marketPrice"] = market, ["directLowPrice"] = null, ["subTypeName"] = subTypeName,
            });

        public TcgplayerTokenProvider CreateTokenProvider(TimeProvider time, TcgplayerOptions? options = null) =>
            new(new Factory(this), Options.Create(options ?? CreateOptions()), time);

        public TcgplayerClient CreateClient(TcgplayerTokenProvider tokens, TcgplayerOptions? options = null)
        {
            options ??= CreateOptions();
            var http = new HttpClient(new TcgplayerAuthHandler(tokens) { InnerHandler = this }, disposeHandler: false)
            {
                BaseAddress = options.VersionedBaseUri,
            };
            return new TcgplayerClient(http, Options.Create(options));
        }

        public TcgplayerClient CreateClient() => CreateClient(CreateTokenProvider(TimeProvider.System));

        public static TcgplayerOptions CreateOptions() => new()
        {
            PublicKey = PublicKey, PrivateKey = PrivateKey, BaseUrl = "https://tcgplayer.test", ApiVersion = Version,
        };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Enqueue(request);
            var uri = request.RequestUri!;
            var path = uri.AbsolutePath.TrimEnd('/');

            if (path == "/token")
            {
                if (request.Method != HttpMethod.Post) return new HttpResponseMessage(HttpStatusCode.MethodNotAllowed);
                var form = System.Web.HttpUtility.ParseQueryString(await request.Content!.ReadAsStringAsync(cancellationToken));
                lock (TokenRequests)
                {
                    TokenRequests.Add(form.AllKeys.ToDictionary(k => k!, k => form[k]!));
                    if (form["grant_type"] != "client_credentials" || form["client_id"] != PublicKey || form["client_secret"] != PrivateKey)
                    {
                        return Json(new JsonObject { ["error"] = "invalid_client" }, HttpStatusCode.BadRequest);
                    }
                    var token = $"token-{IssuedTokens.Count + 1}";
                    IssuedTokens.Add(token);
                    return Json(new JsonObject { ["access_token"] = token, ["token_type"] = "bearer", ["expires_in"] = ExpiresIn, ["userName"] = PublicKey });
                }
            }

            var auth = request.Headers.Authorization;
            lock (TokenRequests)
            {
                if (auth is null || !auth.Scheme.Equals("bearer", StringComparison.Ordinal) || !IssuedTokens.Contains(auth.Parameter!) || RevokedTokens.Contains(auth.Parameter!))
                {
                    return new HttpResponseMessage(HttpStatusCode.Unauthorized);
                }
            }

            if (Override is not null) return Override(request);

            var prefix = $"/{Version}/";
            if (!path.StartsWith(prefix)) return new HttpResponseMessage(HttpStatusCode.NotFound);
            var route = path[prefix.Length..];
            var segments = route.Split('/');
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);

            if (route == "catalog/categories/3/groups") return Paged(Groups, query);
            if (route == "catalog/products")
            {
                if (query["categoryId"] != "3" || query["productTypes"] != "Cards" || query["getExtendedFields"] != "true")
                {
                    return Envelope(false, ["Unexpected query " + uri.Query], []);
                }
                var groupId = int.Parse(query["groupId"]!);
                return Paged(Products.Where(p => (int)p["groupId"]! == groupId), query);
            }
            if (route.StartsWith("pricing/group/"))
            {
                var groupId = int.Parse(segments[2]);
                var ids = Products.Where(p => (int)p["groupId"]! == groupId).Select(p => (int)p["productId"]!).ToHashSet();
                return Results(ProductPrices.Where(p => ids.Contains((int)p["productId"]!)), "No products were found.");
            }
            if (route.StartsWith("pricing/product/")) return ById(ProductPrices, "productId", segments[2], "No products were found.");
            if (route.StartsWith("catalog/products/") && segments.Length == 4 && segments[3] == "skus") return ById(Skus, "productId", segments[2], "No SKUs were found.");
            if (route.StartsWith("pricing/sku/")) return ById(SkuPrices, "skuId", segments[2], "No products were found.");
            if (route == "catalog/categories/3/conditions")
            {
                string[] names = ["Near Mint", "Lightly Played", "Moderately Played", "Heavily Played", "Damaged"];
                string[] abbrs = ["NM", "LP", "MP", "HP", "DMG"];
                return Envelope(true, [], names.Select((n, i) => (JsonNode)new JsonObject { ["conditionId"] = i + 1, ["name"] = n, ["abbreviation"] = abbrs[i], ["displayOrder"] = i + 1 }));
            }
            if (route == "catalog/categories/3/printings")
            {
                string[] names = ["Normal", "Holofoil", "Reverse Holofoil", "1st Edition"];
                return Envelope(true, [], names.Select((n, i) => (JsonNode)new JsonObject { ["printingId"] = i + 1, ["name"] = n, ["displayOrder"] = i + 1, ["modifiedOn"] = "2024-01-01T00:00:00" }));
            }
            if (route == "catalog/categories/3/languages")
            {
                return Envelope(true, [], [new JsonObject { ["languageId"] = 1, ["name"] = "English", ["abbr"] = "EN" }]);
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private HttpResponseMessage Paged(IEnumerable<JsonObject> items, System.Collections.Specialized.NameValueCollection query)
        {
            var all = items.ToList();
            var offset = int.Parse(query["offset"] ?? "0");
            var limit = Math.Min(int.Parse(query["limit"] ?? "10"), MaxLimit);
            var page = all.Skip(offset).Take(limit).Select(i => i.DeepClone()).ToList();
            if (all.Count == 0) return Envelope(false, ["No products were found."], [], totalItems: 0);
            return Envelope(true, [], page, totalItems: all.Count);
        }

        private static HttpResponseMessage ById(List<JsonObject> items, string idField, string idList, string notFound)
        {
            var ids = idList.Split(',').Select(int.Parse).ToHashSet();
            return Results(items.Where(i => ids.Contains((int)i[idField]!)), notFound);
        }

        private static HttpResponseMessage Results(IEnumerable<JsonObject> items, string notFound)
        {
            var list = items.Select(i => i.DeepClone()).ToList();
            return list.Count == 0 ? Envelope(false, [notFound], []) : Envelope(true, [], list);
        }

        public static HttpResponseMessage Envelope(bool success, string[] errors, IEnumerable<JsonNode> results, int? totalItems = null, HttpStatusCode status = HttpStatusCode.OK)
        {
            var body = new JsonObject
            {
                ["success"] = success,
                ["errors"] = new JsonArray(errors.Select(e => (JsonNode)JsonValue.Create(e)!).ToArray()),
                ["results"] = new JsonArray(results.ToArray()),
            };
            if (totalItems is not null) body["totalItems"] = totalItems;
            return Json(body, status);
        }

        private static HttpResponseMessage Json(JsonNode body, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status)
            {
                Content = new StringContent(body.ToJsonString(new JsonSerializerOptions()), System.Text.Encoding.UTF8, "application/json"),
            };

        private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
        {
            public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
        }
    }

    /// <summary>A clock tests move by hand.</summary>
    public class ManualTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }
}
