using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PokemonTCG.API.Data;
using PokemonTCG.API.Market;
using PokemonTCG.API.Market.Providers;
using PokemonTCG.API.Pricing;

namespace PokemonTcgMarketplace.Backend.Tests
{
    public class ObservationKeyTests
    {
        private static readonly DateTimeOffset SoldAt = new(2026, 9, 20, 18, 30, 0, TimeSpan.Zero);
        private static string Key(CompRecord comp) => ObservationKeys.Dedupe(MarketProvider.LicensedCompProvider, ObservationKind.Sale, comp);

        [Fact]
        public void Sales_that_differ_in_printing_language_grade_or_grader_never_share_a_key()
        {
            var raw = new CompRecord("sv3pt5-199", "holofoil", 238m, SoldAt);
            string[] keys =
            [
                Key(raw),
                Key(raw with { Variant = "reverseHolofoil" }),
                Key(raw with { Variant = "1stEditionHolofoil" }),
                Key(raw with { Language = "ja" }),
                Key(raw with { Grade = 10m, GradingCompany = "PSA" }),
                Key(raw with { Grade = 10m, GradingCompany = "CGC" }),
                Key(raw with { Grade = 9.5m, GradingCompany = "BGS" }),
                Key(raw with { Condition = "LightlyPlayed" }),
            ];
            Assert.Equal(keys.Length, keys.Distinct().Count());
        }

        [Fact]
        public void The_same_sale_reported_twice_has_one_key()
        {
            var sale = new CompRecord("sv3pt5-199", "holofoil", 238m, SoldAt, ProviderReference: "order-123");
            Assert.Equal(Key(sale), Key(sale with { Price = 240m, SourceUrl = "https://example.test/x" }));
            var noReference = new CompRecord("sv3pt5-199", "holofoil", 238m, SoldAt);
            Assert.Equal(Key(noReference), Key(noReference with { SoldAt = SoldAt.ToOffset(TimeSpan.FromHours(-5)) }));
        }

        [Fact]
        public async Task The_ebay_provider_stays_disabled_and_never_fetches_without_approved_access()
        {
            var provider = new EbayCompProvider(Options.Create(new EbayOptions { ClientId = "id", ClientSecret = "secret" }));
            Assert.False(provider.IsEnabled);
            Assert.Contains("approval", provider.DisabledReason);
            await Assert.ThrowsAsync<NotSupportedException>(() => provider.FetchSalesAsync([], DateTimeOffset.UtcNow, CancellationToken.None));
        }
    }

    [Collection(ApiCollection.Name)]
    public class CompIngestionTests(ApiFixture fixture)
    {
        private sealed class FakeCompProvider(IReadOnlyList<CompRecord> comps, bool fail = false) : ICompProvider
        {
            public int Calls { get; private set; }
            public MarketProvider Id => MarketProvider.LicensedCompProvider;
            public string DisplayName => "Test comps";
            public bool IsEnabled => true;
            public string? DisabledReason => null;
            public Task<IReadOnlyList<CompRecord>> FetchSalesAsync(IReadOnlyCollection<CompRequest> printings, DateTimeOffset since, CancellationToken cancellationToken)
            {
                Calls++;
                return fail ? throw new HttpRequestException("down") : Task.FromResult(comps);
            }
        }

        private async Task SeedCardAsync(string id)
        {
            using var scope = fixture.Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (!await db.Cards.AnyAsync(c => c.Id == id))
            {
                db.Cards.Add(new Card { Id = id, Name = "Comp Card", Number = "1", SetId = "cmp", SetName = "Comp Set" });
                await db.SaveChangesAsync();
            }
        }

        [Fact]
        public async Task Sales_are_stored_once_with_provenance_and_grades_kept_apart()
        {
            await SeedCardAsync("cmp-1");
            var at = DateTimeOffset.UtcNow.AddDays(-2);
            var sales = new List<CompRecord>
            {
                new("cmp-1", "holofoil", 100m, at, ProviderReference: "s-1", SourceUrl: "https://comps.test/s-1"),
                new("cmp-1", "holofoil", 100m, at, ProviderReference: "s-1"), // the same sale twice in one batch
                new("cmp-1", "holofoil", 410m, at, Grade: 10m, GradingCompany: "psa", ProviderReference: "s-2"),
            };
            var provider = new FakeCompProvider(sales);
            var ebay = new EbayCompProvider(Options.Create(new EbayOptions()));

            using var scope = fixture.Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var service = new CompIngestionService([ebay, provider], db, TimeProvider.System, NullLogger<CompIngestionService>.Instance);

            var first = await service.IngestAsync([new CompRequest("cmp-1", "holofoil", null)], at.AddDays(-1), CancellationToken.None);
            Assert.Equal((ProviderRunStatus.Disabled, 0), (first[0].Status, first[0].Stored));
            Assert.Equal((ProviderRunStatus.Succeeded, 3, 2), (first[1].Status, first[1].Fetched, first[1].Stored));

            // Re-ingesting the same window stores nothing new.
            var again = await service.IngestAsync([new CompRequest("cmp-1", "holofoil", null)], at.AddDays(-1), CancellationToken.None);
            Assert.Equal(0, again[1].Stored);

            var rows = await db.MarketObservations.AsNoTracking().Where(o => o.CardId == "cmp-1").OrderBy(o => o.Price).ToListAsync();
            Assert.Equal(2, rows.Count);
            Assert.All(rows, r => Assert.Equal((MarketProvider.LicensedCompProvider, ObservationKind.Sale, "USD"), (r.Provider, r.Kind, r.Currency)));
            Assert.Equal(("https://comps.test/s-1", (decimal?)null, (string?)null), (rows[0].SourceUrl, rows[0].Grade, rows[0].GradingCompany));
            Assert.Equal((10m, "PSA"), (rows[1].Grade!.Value, rows[1].GradingCompany));
        }

        [Fact]
        public async Task A_failing_comp_provider_is_reported_and_stores_nothing()
        {
            using var scope = fixture.Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var provider = new FakeCompProvider([], fail: true);
            var service = new CompIngestionService([provider], db, TimeProvider.System, NullLogger<CompIngestionService>.Instance);

            var result = Assert.Single(await service.IngestAsync([new CompRequest("cmp-1", "holofoil", null)], DateTimeOffset.UtcNow.AddDays(-1), CancellationToken.None));
            Assert.Equal((ProviderRunStatus.Failed, 0), (result.Status, result.Stored));
        }
    }

    [Collection(TcgplayerCollection.Name)]
    public class OverlayProviderFailureTests(TcgplayerFixture fixture)
    {
        private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

        [Fact]
        public async Task A_failing_tcgplayer_overlay_leaves_the_primary_prices_under_their_own_name()
        {
            var set = fixture.Upstream.AddSet("tpf", "Overlay Failure Set", "Test Series", new DateOnly(2024, 1, 1), 1);
            fixture.Upstream.AddCard(set, "1", "Zapdos", "Rare", Today, ("holofoil", 7m));
            fixture.TcgplayerApi.Override = _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            try
            {
                var response = await fixture.CreateClient().PostAsync("/internal/snapshots", null);
                var result = await response.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal("Succeeded", result.GetProperty("status").GetString());
            }
            finally
            {
                fixture.TcgplayerApi.Override = null;
            }

            using var scope = fixture.Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var price = await db.LatestPrices.AsNoTracking().SingleAsync(p => p.CardId == "tpf-1");
            Assert.Equal((7m, MarketProvider.PokemonTcg), (price.Market!.Value, price.Provider));
            var run = await db.SnapshotRuns.AsNoTracking().OrderByDescending(r => r.Id).FirstAsync();
            Assert.Contains("\"provider\":\"TCGPlayer\",\"status\":\"Failed\"", run.ProviderResults);
            Assert.Contains("\"provider\":\"PokemonTcg\",\"status\":\"Succeeded\"", run.ProviderResults);
        }
    }

    public class FreshnessTests
    {
        private static readonly DateOnly Today = new(2026, 9, 26);
        private static readonly FreshnessOptions Options = new();

        [Theory]
        [InlineData(0, FreshnessState.Fresh, "Updated today")]
        [InlineData(1, FreshnessState.Fresh, "Updated yesterday")]
        [InlineData(3, FreshnessState.Aging, "Updated 3 days ago")]
        [InlineData(9, FreshnessState.Stale, "Stale: last updated 9 days ago")]
        public void Prices_say_how_old_they_are(int ageDays, FreshnessState state, string label)
        {
            var freshness = Options.Of(Today.AddDays(-ageDays), Today);
            Assert.Equal((ageDays, state, label), (freshness.AgeDays!.Value, freshness.State, freshness.Label));
        }

        [Fact]
        public void No_price_is_no_data_not_zero() =>
            Assert.Equal((FreshnessState.NoData, (int?)null), (Options.Of(null, Today).State, Options.Of(null, Today).AgeDays));
    }

    [Collection(ApiCollection.Name)]
    public class ProvenanceApiTests(ApiFixture fixture)
    {
        private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

        [Fact]
        public async Task Status_lists_every_provider_and_says_sold_comps_are_not_available()
        {
            var status = await fixture.CreateClient().GetFromJsonAsync<JsonElement>("/api/market/status");
            Assert.False(status.GetProperty("soldCompsAvailable").GetBoolean());
            var providers = status.GetProperty("providers").EnumerateArray().ToList();
            Assert.Contains(providers, p => p.GetProperty("provider").GetString() == "PokemonTcg" && p.GetProperty("supplies").GetString() == "Reference");
            var ebay = Assert.Single(providers, p => p.GetProperty("provider").GetString() == "Ebay");
            Assert.Equal(("SoldComps", false), (ebay.GetProperty("supplies").GetString(), ebay.GetProperty("enabled").GetBoolean()));
            Assert.Contains("approval", ebay.GetProperty("detail").GetString());
        }

        [Fact]
        public async Task A_card_says_where_its_prices_came_from_and_how_old_they_are()
        {
            var set = fixture.Upstream.AddSet("prov", "Provenance Set", "Test Series", new DateOnly(2024, 1, 1), 1);
            fixture.Upstream.AddCard(set, "1", "Mew", "Rare", Today, ("holofoil", 12m));
            Assert.Equal(HttpStatusCode.OK, (await fixture.CreateClient().PostAsync("/internal/snapshots", null)).StatusCode);

            var card = await fixture.CreateClient().GetFromJsonAsync<JsonElement>("/api/cards/prov-1");
            Assert.Equal("PokemonTcg", card.GetProperty("priceProvider").GetString());
            Assert.Equal("Fresh", card.GetProperty("freshness").GetProperty("state").GetString());
            Assert.Equal(30, card.GetProperty("historyDays").GetInt32());
            var price = card.GetProperty("prices")[0];
            Assert.Equal(("PokemonTcg", Today.ToString("yyyy-MM-dd")), (price.GetProperty("provider").GetString(), price.GetProperty("asOf").GetString()));
        }
    }
}
