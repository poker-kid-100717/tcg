using System.Net;
using PokemonTCG.API.Pricing.Tcgplayer;

namespace PokemonTcgMarketplace.Backend.Tests.Tcgplayer
{
    public class TcgplayerClientTests
    {
        private static IEnumerable<string> ApiPaths(FakeTcgplayerApi api) =>
            api.ApiRequests.Select(r => r.RequestUri!.PathAndQuery);

        [Fact]
        public async Task Groups_are_paged_until_total_items()
        {
            var api = new FakeTcgplayerApi();
            for (var i = 1; i <= 234; i++) api.AddGroup(i, $"Group {i}", new DateTime(2020, 1, 1).AddDays(i), $"G{i}");
            var client = api.CreateClient();

            var groups = await client.GetGroupsAsync(3);

            Assert.Equal(234, groups.Count);
            Assert.Equal(Enumerable.Range(1, 234), groups.Select(g => g.GroupId));
            Assert.Equal(
                [
                    "/v1.39.0/catalog/categories/3/groups?offset=0&limit=100",
                    "/v1.39.0/catalog/categories/3/groups?offset=100&limit=100",
                    "/v1.39.0/catalog/categories/3/groups?offset=200&limit=100",
                ],
                ApiPaths(api));
            var first = groups[0];
            Assert.Equal("Group 1", first.Name);
            Assert.Equal("G1", first.Abbreviation);
            Assert.Equal(new DateTime(2020, 1, 2), first.PublishedOn);
            Assert.Equal(3, first.CategoryId);
        }

        [Fact]
        public async Task Paging_stops_on_an_empty_page_even_if_total_is_higher()
        {
            var api = new FakeTcgplayerApi();
            var calls = 0;
            api.Override = _ =>
            {
                calls++;
                return calls == 1
                    ? FakeTcgplayerApi.Envelope(true, [], [new System.Text.Json.Nodes.JsonObject { ["groupId"] = 1, ["name"] = "A", ["categoryId"] = 3 }], totalItems: 500)
                    : FakeTcgplayerApi.Envelope(true, [], [], totalItems: 500);
            };

            var groups = await api.CreateClient().GetGroupsAsync(3);

            Assert.Single(groups);
            Assert.Equal(2, calls);
        }

        [Fact]
        public async Task Group_products_request_cards_with_extended_fields()
        {
            var api = new FakeTcgplayerApi();
            api.AddProduct(500, 23237, "Charizard ex - 006/165", "006/165", "Double Rare");
            api.AddProduct(501, 23237, "Bulbasaur", "001/165");
            api.AddProduct(900, 99, "Other set card", "001/100");
            var client = api.CreateClient();

            var products = await client.GetGroupProductsAsync(23237);

            Assert.Equal([500, 501], products.Select(p => p.ProductId));
            Assert.Equal("/v1.39.0/catalog/products?groupId=23237&categoryId=3&productTypes=Cards&getExtendedFields=true&offset=0&limit=100",
                Assert.Single(ApiPaths(api)));
            var charizard = products[0];
            Assert.Equal("006/165", charizard.Number);
            Assert.Equal("Double Rare", charizard.GetExtendedValue("rarity"));
            Assert.Equal("https://www.tcgplayer.com/product/500", charizard.Url);
            Assert.Null(charizard.GetExtendedValue("HP"));
        }

        [Fact]
        public async Task Group_prices_map_fields()
        {
            var api = new FakeTcgplayerApi();
            api.AddProduct(500, 7, "Charizard ex", "006/165");
            api.AddProductPrice(500, "Holofoil", 100m);
            api.AddProductPrice(500, "Reverse Holofoil", 20m);

            var prices = await api.CreateClient().GetGroupPricesAsync(7);

            Assert.Equal("/v1.39.0/pricing/group/7", Assert.Single(ApiPaths(api)));
            Assert.Equal(2, prices.Count);
            var holo = prices.Single(p => p.SubTypeName == "Holofoil");
            Assert.Equal(500, holo.ProductId);
            Assert.Equal(80m, holo.LowPrice);
            Assert.Equal(100m, holo.MidPrice);
            Assert.Equal(200m, holo.HighPrice);
            Assert.Equal(100m, holo.MarketPrice);
            Assert.Null(holo.DirectLowPrice);
        }

        [Fact]
        public async Task Product_prices_are_batched_at_250_ids()
        {
            var api = new FakeTcgplayerApi();
            var ids = Enumerable.Range(1, 600).ToList();
            foreach (var id in ids) api.AddProductPrice(id, "Normal", id);

            var prices = await api.CreateClient().GetProductPricesAsync(ids.Concat([5, 6])); // duplicates are sent once

            Assert.Equal(600, prices.Count);
            var paths = ApiPaths(api).ToList();
            Assert.Equal(3, paths.Count);
            Assert.All(paths, p => Assert.StartsWith("/v1.39.0/pricing/product/", p));
            Assert.Equal([250, 250, 100], paths.Select(p => p["/v1.39.0/pricing/product/".Length..].Split(',').Length));
            Assert.Equal("/v1.39.0/pricing/product/251," + string.Join(',', Enumerable.Range(252, 249)), paths[1]);
        }

        [Fact]
        public async Task Empty_id_list_makes_no_requests()
        {
            var api = new FakeTcgplayerApi();
            Assert.Empty(await api.CreateClient().GetSkuPricesAsync([]));
            Assert.Empty(api.Requests);
        }

        [Fact]
        public async Task Skus_and_sku_prices()
        {
            var api = new FakeTcgplayerApi();
            api.Skus.Add(new() { ["skuId"] = 10, ["productId"] = 500, ["languageId"] = 1, ["printingId"] = 2, ["conditionId"] = 1 });
            api.Skus.Add(new() { ["skuId"] = 11, ["productId"] = 500, ["languageId"] = 1, ["printingId"] = 2, ["conditionId"] = 2 });
            api.SkuPrices.Add(new() { ["skuId"] = 10, ["lowPrice"] = 1.5m, ["lowestShipping"] = 0.99m, ["lowestListingPrice"] = 2.49m, ["marketPrice"] = 2m, ["directLowPrice"] = null });
            var client = api.CreateClient();

            var skus = await client.GetSkusAsync([500]);
            var prices = await client.GetSkuPricesAsync(skus.Select(s => s.SkuId));

            Assert.Equal([10, 11], skus.Select(s => s.SkuId));
            Assert.Equal(new TcgplayerSku(11, 500, 1, 2, 2), skus[1]);
            Assert.Equal(new TcgplayerSkuPrice(10, 1.5m, 0.99m, 2.49m, 2m, null), Assert.Single(prices));
            Assert.Equal(["/v1.39.0/catalog/products/500/skus", "/v1.39.0/pricing/sku/10,11"], ApiPaths(api));
        }

        [Fact]
        public async Task Category_reference_data()
        {
            var client = new FakeTcgplayerApi().CreateClient();

            var conditions = await client.GetConditionsAsync(3);
            var printings = await client.GetPrintingsAsync(3);
            var languages = await client.GetLanguagesAsync(3);

            Assert.Equal(5, conditions.Count);
            Assert.Equal(new TcgplayerCondition(1, "Near Mint", "NM", 1), conditions[0]);
            Assert.All(conditions, c => Assert.NotNull(TcgplayerConditions.ToCondition(c.Name)));
            Assert.Contains(printings, p => p.Name == "Reverse Holofoil");
            Assert.Equal(new TcgplayerLanguage(1, "English", "EN"), Assert.Single(languages));
        }

        [Theory]
        [InlineData("No products were found.")]
        [InlineData("No results found.")]
        public async Task Not_found_envelope_returns_empty(string error)
        {
            var api = new FakeTcgplayerApi { Override = _ => FakeTcgplayerApi.Envelope(false, [error], []) };
            var client = api.CreateClient();

            Assert.Empty(await client.GetGroupPricesAsync(1));
            Assert.Empty(await client.GetGroupProductsAsync(1));
            Assert.Empty(await client.GetProductPricesAsync([1, 2]));
        }

        [Fact]
        public async Task Not_found_envelope_with_404_status_returns_empty()
        {
            var api = new FakeTcgplayerApi
            {
                Override = _ => FakeTcgplayerApi.Envelope(false, ["No products were found."], [], status: HttpStatusCode.NotFound),
            };
            Assert.Empty(await api.CreateClient().GetSkusAsync([1]));
        }

        [Fact]
        public async Task Missing_ids_come_back_empty_from_the_fake()
        {
            var api = new FakeTcgplayerApi();
            Assert.Empty(await api.CreateClient().GetProductPricesAsync([123]));
        }

        [Fact]
        public async Task Other_errors_throw_with_the_error_list()
        {
            var api = new FakeTcgplayerApi
            {
                Override = _ => FakeTcgplayerApi.Envelope(false, ["Invalid groupId.", "Something else"], []),
            };

            var ex = await Assert.ThrowsAsync<TcgplayerApiException>(() => api.CreateClient().GetGroupPricesAsync(1));

            Assert.Equal(["Invalid groupId.", "Something else"], ex.Errors);
            Assert.Contains("Invalid groupId.", ex.Message);
            Assert.Null(ex.StatusCode);
        }

        [Fact]
        public async Task Mixed_not_found_and_real_errors_throw()
        {
            var api = new FakeTcgplayerApi
            {
                Override = _ => FakeTcgplayerApi.Envelope(false, ["No products were found.", "Invalid productId: abc"], []),
            };
            await Assert.ThrowsAsync<TcgplayerApiException>(() => api.CreateClient().GetProductPricesAsync([1]));
        }

        [Fact]
        public async Task Non_json_server_error_throws_with_status()
        {
            var api = new FakeTcgplayerApi
            {
                Override = _ => new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent("<html>bad gateway</html>") },
            };

            var ex = await Assert.ThrowsAsync<TcgplayerApiException>(() => api.CreateClient().GetGroupsAsync(3));

            Assert.Equal(502, ex.StatusCode);
            Assert.Empty(ex.Errors);
        }

        [Fact]
        public async Task Server_error_with_envelope_throws_even_if_errors_say_not_found()
        {
            var api = new FakeTcgplayerApi
            {
                Override = _ => FakeTcgplayerApi.Envelope(false, ["No results"], [], status: HttpStatusCode.InternalServerError),
            };

            var ex = await Assert.ThrowsAsync<TcgplayerApiException>(() => api.CreateClient().GetGroupPricesAsync(1));
            Assert.Equal(500, ex.StatusCode);
        }
    }
}
