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
        public async Task Search_needs_two_characters_and_reads_the_local_catalog()
        {
            var set = fixture.Upstream.AddSet("srch", "Search Set", "Test Series", new DateOnly(2023, 1, 1), 3);
            fixture.Upstream.AddCard(set, "1", "Zygarde", "Rare", Today, ("holofoil", 3m));
            fixture.Upstream.AddCard(set, "2", "Zyzzyva", "Common", Today, ("normal", 1m));
            fixture.Upstream.AddCard(set, "3", "Mega Zygarde", "Rare", Today, ("holofoil", 9m));
            await RunSnapshot();
            var client = fixture.CreateClient();

            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/cards?q=z")).StatusCode);

            // Name prefix or word prefix, from Postgres rather than the upstream API.
            var before = fixture.Upstream.Requests.Count;
            var results = await Get<SearchResults>("/api/cards?q=zyg");
            Assert.Equal(["Mega Zygarde", "Zygarde"], results.Cards.Where(c => c.Id.StartsWith("srch-")).Select(c => c.Name).Order());
            Assert.Equal(before, fixture.Upstream.Requests.Count);

            // LIKE wildcards in the query are literal text, not patterns.
            Assert.DoesNotContain((await Get<SearchResults>("/api/cards?q=" + Uri.EscapeDataString("z%a"))).Cards, c => c.Id.StartsWith("srch-"));
        }

        [Fact]
        public async Task Upstream_search_escapes_the_query()
        {
            using var scope = fixture.Factory.Services.CreateScope();
            var client = scope.ServiceProvider.GetRequiredService<PokemonTcgClient>();

            // A quote can't close the term and add clauses of its own.
            await client.SearchCardsAsync("zy\" OR set.id:x", 1, 10, CancellationToken.None);
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
                var stored = await db.Cards.SingleAsync(c => c.Id == "mkt1-1");
                Assert.Equal("Riser", stored.Name);
                // The attributes the price model learns from are stored with the card.
                Assert.Equal(6, stored.NationalDex);
                Assert.Equal("Pokémon", stored.Supertype);
                Assert.Equal(["Basic"], stored.Subtypes);
                Assert.Equal("Test Artist", stored.Artist);
                Assert.Equal("Test Series", stored.SetSeries);
                Assert.Equal(new DateOnly(2022, 5, 1), stored.SetReleased);
                Assert.Equal(4, stored.SetPrintedTotal);
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

        private async Task Seed(string id, string name, Func<int, (decimal Market, decimal Low)?> priceDaysAgo, int days = 30)
        {
            using var scope = fixture.Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Cards.Add(new Card { Id = id, Name = name, Number = "1", SetId = "seed", SetName = "Seeded Set" });
            for (var ago = days; ago >= 0; ago--)
            {
                if (priceDaysAgo(ago) is not { } p) continue;
                db.PriceSnapshots.Add(new PriceSnapshot
                {
                    CardId = id, Variant = "holofoil", Date = Today.AddDays(-ago), Market = p.Market, Low = p.Low, Mid = p.Market,
                });
            }
            await db.SaveChangesAsync();
        }

        [Fact]
        public async Task Down_trend_needs_a_steady_fall_not_one_bad_day()
        {
            // $40 → $28 in a straight line over 30 days.
            await Seed("trd-steady", "Steady Decline", ago => (28m + ago * 0.4m, 28m));
            // Same net drop, but swinging ±$8 day to day: the line doesn't fit.
            await Seed("trd-noisy", "Noisy", ago => (28m + ago * 0.4m + (ago % 2 == 0 ? 8m : -8m) * (ago is 0 or 30 ? 0 : 1), 20m));
            // Falling, but only three data points.
            await Seed("trd-sparse", "Sparse", ago => ago is 0 or 10 or 20 ? (40m - (20 - ago), 1m) : null);
            // Falling in a straight line, but under $2.
            await Seed("trd-cheap", "Cheap", ago => (1m + ago * 0.05m, 1m));

            var trend = await Get<DownTrend>("/api/market/downtrend?days=30&limit=50");

            Assert.Equal(Today, trend.To);
            var steady = Assert.Single(trend.Cards, c => c.CardId == "trd-steady");
            Assert.Equal(40m, steady.From);
            Assert.Equal(28m, steady.To);
            Assert.Equal(-30.0m, steady.ChangePercent);
            Assert.Equal(1.0, steady.Fit);
            Assert.Equal(31, steady.Points.Count);
            Assert.Equal(Today, steady.Points[^1].Date);
            Assert.DoesNotContain(trend.Cards, c => c.CardId is "trd-noisy" or "trd-sparse" or "trd-cheap");
        }

        [Fact]
        public async Task Sleepers_are_quiet_cards_with_no_listings_near_the_market_price()
        {
            // Sells around $20, cheapest listing $26; barely moved in 30 days.
            await Seed("slp-quiet", "Quiet Gap", ago => ago is 0 ? (20m, 26m) : (19m, 19m));
            // Same gap, but already up 50% this month: not sleeping.
            await Seed("slp-moved", "Already Moved", ago => ago is 0 ? (30m, 39m) : (20m, 20m));
            // Listings right at the market price.
            await Seed("slp-supplied", "Well Supplied", ago => ago is 0 ? (20m, 21m) : (20m, 20m));
            // A $100 listing on a $10 card is an outlier, not a signal.
            await Seed("slp-outlier", "Outlier", ago => ago is 0 ? (10m, 100m) : (10m, 10m));
            // New card with no 30-day history: still counts, with no 30-day change.
            await Seed("slp-new", "New Gap", ago => (10m, 12m), days: 3);

            var sleepers = await Get<Sleepers>("/api/market/sleepers?limit=50");

            Assert.Equal(Today, sleepers.AsOf);
            var quiet = Assert.Single(sleepers.Cards, c => c.CardId == "slp-quiet");
            Assert.Equal(30.0m, quiet.ListingGapPercent);
            Assert.Equal(5.3m, quiet.Change30Percent);
            var fresh = Assert.Single(sleepers.Cards, c => c.CardId == "slp-new");
            Assert.Equal(20.0m, fresh.ListingGapPercent);
            Assert.Null(fresh.Change30Percent);
            Assert.True(sleepers.Cards.ToList().IndexOf(quiet) < sleepers.Cards.ToList().IndexOf(fresh));
            Assert.DoesNotContain(sleepers.Cards, c => c.CardId is "slp-moved" or "slp-supplied" or "slp-outlier");
        }

        [Fact]
        public async Task A_snapshot_that_fails_part_way_records_nothing()
        {
            var set = fixture.Upstream.AddSet("half1", "Half Set", "Test Series", new DateOnly(2021, 1, 1), 300);
            for (var i = 1; i <= 300; i++) fixture.Upstream.AddCard(set, i.ToString(), $"Half {i}", "Rare", Today, ("holofoil", 5m));
            var total = fixture.Upstream.Requests.Count;
            // The first page of cards succeeds, then the card database goes down for good.
            fixture.Upstream.FailAfterRequests = total + 1;
            try
            {
                var response = await fixture.CreateClient().PostAsync("/internal/snapshots", null);
                var result = (await response.Content.ReadFromJsonAsync<SnapshotResult>(Json))!;
                Assert.Equal(SnapshotStatus.Failed, result.Status);
                // The first page's rows were rolled back with the rest, so the run doesn't claim them.
                Assert.Equal((0, 0), (result.CardsSeen, result.PricesWritten));
            }
            finally
            {
                fixture.Upstream.FailAfterRequests = null;
            }

            using var scope = fixture.Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.False(await db.PriceSnapshots.AnyAsync(s => s.CardId.StartsWith("half1-")));
            Assert.False(await db.Cards.AnyAsync(c => c.Id.StartsWith("half1-")));
        }

        [Fact]
        public async Task Upstream_outage_is_a_502_with_an_explanation()
        {
            fixture.Upstream.Fail = true;
            try
            {
                // A card the local catalog doesn't have yet goes upstream.
                var response = await fixture.CreateClient().GetAsync("/api/cards/outage-1");
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
