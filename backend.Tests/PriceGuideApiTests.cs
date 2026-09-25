using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PokemonTCG.API.Data;
using PokemonTCG.API.Pricing;

namespace PokemonTcgMarketplace.Backend.Tests
{
    [Collection(ApiCollection.Name)]
    public class PriceGuideApiTests(ApiFixture fixture)
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        {
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
        };
        private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

        private async Task<T> Get<T>(string path)
        {
            var response = await fixture.CreateClient().GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<T>(Json))!;
        }

        private async Task<SnapshotResult> RunSnapshot()
        {
            var response = await fixture.CreateClient().PostAsync("/internal/snapshots", null);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<SnapshotResult>(Json))!;
        }

        [Fact]
        public async Task Set_detail_follows_pagination_and_sums_market_value()
        {
            var set = fixture.Upstream.AddSet("big1", "Big Set", "Test Series", new DateOnly(2024, 3, 22), 260);
            for (var i = 260; i >= 1; i--)
            {
                fixture.Upstream.AddCard(set, i.ToString(), $"Card {i}", "Common", Today, ("normal", 1.00m));
            }
            fixture.Upstream.AddCard(set, "TG01", "Trainer Gallery", "Rare", Today, ("holofoil", 50.00m), ("reverseHolofoil", 80.00m));

            var detail = await Get<SetDetail>("/api/sets/big1");

            Assert.Equal(261, detail.Cards.Count);
            Assert.Equal("1", detail.Cards[0].Number);
            Assert.Equal("260", detail.Cards[259].Number);
            Assert.Equal("TG01", detail.Cards[^1].Number);
            Assert.Equal(new DateOnly(2024, 3, 22), detail.Set.ReleaseDate);
            // Headline price is the holofoil printing, not the pricier reverse holo.
            Assert.Equal(50.00m, detail.Stats.MostValuable!.MarketPrice);
            Assert.Equal("holofoil", detail.Stats.MostValuable.PriceVariant);
            Assert.Equal(260m + 50m, detail.Stats.TotalMarketValue);
            Assert.Equal("https://prices.pokemontcg.io/tcgplayer/big1-TG01", detail.Stats.MostValuable.TcgplayerUrl);
        }

        [Fact]
        public async Task Unknown_set_and_card_are_404()
        {
            var client = fixture.CreateClient();
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/sets/nope")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/cards/nope-1")).StatusCode);
        }

        [Fact]
        public async Task Search_needs_two_characters_and_escapes_the_query()
        {
            var set = fixture.Upstream.AddSet("srch", "Search Set", "Test Series", new DateOnly(2023, 1, 1), 2);
            fixture.Upstream.AddCard(set, "1", "Zygarde", "Rare", Today, ("holofoil", 3m));
            fixture.Upstream.AddCard(set, "2", "Zyzzyva", "Common", Today, ("normal", 1m));
            var client = fixture.CreateClient();

            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/cards?q=z")).StatusCode);

            var results = await Get<SearchResults>("/api/cards?q=zyg");
            Assert.Equal("Zygarde", Assert.Single(results.Cards).Name);

            // A quote can't close the term and add clauses of its own.
            await client.GetAsync("/api/cards?q=" + Uri.EscapeDataString("zy\" OR set.id:x"));
            var sent = fixture.Upstream.Requests.Last(u => u.Query.Contains("OR"));
            Assert.Contains(Uri.EscapeDataString("name:\"zy OR set.id:x*\""), sent.Query);
        }

        [Fact]
        public async Task Snapshots_build_history_movers_and_top_cards()
        {
            var set = fixture.Upstream.AddSet("mkt1", "Market Set", "Test Series", new DateOnly(2022, 5, 1), 4);
            var weekAgo = Today.AddDays(-7);
            fixture.Upstream.AddCard(set, "1", "Riser", "Rare Holo", weekAgo, ("holofoil", 10m), ("reverseHolofoil", 11m));
            fixture.Upstream.AddCard(set, "2", "Faller", "Rare Holo", weekAgo, ("holofoil", 40m));
            fixture.Upstream.AddCard(set, "3", "Bulk", "Common", weekAgo, ("normal", 0.10m));
            fixture.Upstream.AddCard(set, "4", "Steady", "Rare", weekAgo, ("holofoil", 5m));

            var first = await RunSnapshot();
            Assert.Equal(SnapshotStatus.Succeeded, first.Status);

            fixture.Upstream.SetPrices("mkt1-1", Today, ("holofoil", 25m), ("reverseHolofoil", 12m));
            fixture.Upstream.SetPrices("mkt1-2", Today, ("holofoil", 30m));
            fixture.Upstream.SetPrices("mkt1-3", Today, ("normal", 0.40m));
            fixture.Upstream.SetPrices("mkt1-4", Today, ("holofoil", 5m));
            await RunSnapshot();
            var rerun = await RunSnapshot(); // same day again: overwrites, doesn't duplicate
            Assert.Equal(SnapshotStatus.Succeeded, rerun.Status);

            using (var scope = fixture.Factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var rows = await db.PriceSnapshots.Where(s => s.CardId.StartsWith("mkt1-")).ToListAsync();
                Assert.Equal(8, rows.Count); // 4 printings over the minimum × 2 days; bulk commons skipped
                Assert.DoesNotContain(rows, r => r.CardId == "mkt1-3");
                Assert.Equal("Riser", (await db.Cards.SingleAsync(c => c.Id == "mkt1-1")).Name);
            }

            var card = await Get<CardDetail>("/api/cards/mkt1-1");
            var holo = card.History.Where(h => h.Variant == "holofoil").ToList();
            Assert.Equal([weekAgo, Today], holo.Select(h => h.Date));
            Assert.Equal([10m, 25m], holo.Select(h => h.Market));
            Assert.Equal(["Holofoil", "Reverse Holofoil"], card.Prices.Select(p => p.Label));
            Assert.Equal("https://prices.pokemontcg.io/tcgplayer/mkt1-1", card.TcgplayerUrl);

            var movers = await Get<MarketMovers>("/api/market/movers?days=7&minPrice=2");
            Assert.Equal(weekAgo, movers.From);
            // Both printings rose; only the bigger move (holofoil, +150%) is listed.
            var riser = Assert.Single(movers.Gainers, m => m.CardId == "mkt1-1");
            Assert.Equal(150.0m, riser.ChangePercent);
            Assert.Equal("holofoil", riser.Variant);
            var faller = Assert.Single(movers.Losers, m => m.CardId == "mkt1-2");
            Assert.Equal(-25.0m, faller.ChangePercent);
            Assert.DoesNotContain(movers.Gainers.Concat(movers.Losers), m => m.CardId == "mkt1-4");

            var top = await Get<List<ValuableCard>>("/api/market/top?limit=50");
            Assert.Contains(top, t => t.CardId == "mkt1-2" && t.Market == 30m);
            Assert.Equal(top.Count, top.Select(t => t.CardId).Distinct().Count());
        }

        [Fact]
        public async Task Upstream_outage_is_a_502_with_an_explanation()
        {
            fixture.Upstream.Fail = true;
            try
            {
                var response = await fixture.CreateClient().GetAsync("/api/cards?q=outage");
                Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
                Assert.Contains("isn't responding", await response.Content.ReadAsStringAsync());
            }
            finally
            {
                fixture.Upstream.Fail = false;
            }
        }
    }

    public class PricesTests
    {
        [Theory]
        [InlineData("holofoil", "Holofoil")]
        [InlineData("reverseHolofoil", "Reverse Holofoil")]
        [InlineData("1stEditionNormal", "1st Edition Normal")]
        public void Variant_labels_read_naturally(string variant, string label) =>
            Assert.Equal(label, Prices.VariantLabel(variant));

        [Fact]
        public void Headline_price_prefers_the_main_printing()
        {
            var prices = new Dictionary<string, TcgPrice>
            {
                ["reverseHolofoil"] = new(null, null, null, 9m, null),
                ["normal"] = new(null, null, null, 2m, null),
            };
            Assert.Equal("normal", Prices.HeadlineVariant(prices));
            Assert.Equal(9m, Prices.HeadlinePrice(new Dictionary<string, TcgPrice> { ["reverseHolofoil"] = new(null, null, null, 9m, null) }));
            Assert.Null(Prices.HeadlinePrice(null));
        }

        [Fact]
        public void Quote_keeps_the_term_closed() =>
            Assert.Equal("\"pika chu*\"", PokemonTcgClient.Quote("pika\" chu\\*"));
    }
}
