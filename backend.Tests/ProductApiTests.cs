using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PokemonTCG.API.Data;
using PokemonTCG.API.Product;

namespace PokemonTcgMarketplace.Backend.Tests;

[Collection(ApiCollection.Name)]
public class ProductApiTests(ApiFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    [Fact]
    public async Task Preview_session_watchlist_alert_dashboard_and_intelligence_work_end_to_end()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var cardId = $"sig-{suffix}";
        var today = DateOnly.FromDateTime(fixture.Clock.GetUtcNow().UtcDateTime);
        var yesterday = today.AddDays(-1);

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Cards.Add(new Card
            {
                Id = cardId,
                Name = "Signal Pikachu",
                Number = "25",
                SetId = "signal",
                SetName = "Signal Test Set",
                ImageSmall = "https://example.test/pikachu.png",
            });
            db.PriceSnapshots.Add(new PriceSnapshot
            {
                CardId = cardId,
                Variant = "holofoil",
                Date = yesterday,
                Market = 10m,
                Low = 9m,
                Mid = 10m,
                High = 12m,
            });
            await db.SaveChangesAsync();
        }

        using var client = fixture.CreateClient();

        var sessionResponse = await client.PostAsync("/api/session", null);
        Assert.Equal(HttpStatusCode.OK, sessionResponse.StatusCode);
        var account = (await sessionResponse.Content.ReadFromJsonAsync<AccountView>(Json))!;
        Assert.True(account.IsPro);
        Assert.False(account.BillingConfigured);
        Assert.Equal("preview", account.Plan);

        var watchResponse = await client.PostAsJsonAsync("/api/watchlist", new CreateWatchlistRequest(
            cardId,
            "holofoil",
            "untrusted client name",
            "untrusted client set",
            null,
            TargetBelow: 9m,
            TargetAbove: null,
            MovePercent: 10m));
        Assert.Equal(HttpStatusCode.OK, watchResponse.StatusCode);

        var watchlist = (await (await client.GetAsync("/api/watchlist"))
            .Content.ReadFromJsonAsync<List<WatchlistItemView>>(Json))!;
        var watched = Assert.Single(watchlist);
        Assert.Equal("Signal Pikachu", watched.CardName);
        Assert.Equal("Signal Test Set", watched.SetName);
        Assert.Equal(10m, watched.BaselinePrice);

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.PriceSnapshots.Add(new PriceSnapshot
            {
                CardId = cardId,
                Variant = "holofoil",
                Date = today,
                Market = 8m,
                Low = 7.5m,
                Mid = 8m,
                High = 9m,
            });
            await db.SaveChangesAsync();

            var alerts = scope.ServiceProvider.GetRequiredService<AlertEvaluationService>();
            Assert.True(await alerts.EvaluateAsync(CancellationToken.None) >= 1);
        }

        var dashboardResponse = await client.GetAsync("/api/dashboard");
        Assert.Equal(HttpStatusCode.OK, dashboardResponse.StatusCode);
        var dashboard = (await dashboardResponse.Content.ReadFromJsonAsync<DashboardView>(Json))!;
        Assert.Single(dashboard.Watchlist);
        Assert.True(dashboard.UnreadAlerts >= 1);
        Assert.Contains(dashboard.Alerts, a => a.Kind == "below" && a.CurrentValue == 8m);

        var intelligenceResponse = await client.GetAsync($"/api/cards/{cardId}/intelligence?variant=holofoil");
        Assert.Equal(HttpStatusCode.OK, intelligenceResponse.StatusCode);
        var intelligence = (await intelligenceResponse.Content.ReadFromJsonAsync<MarketIntelligenceView>(Json))!;
        Assert.Equal("Unknown", intelligence.Liquidity);
        Assert.Null(intelligence.SoldComps30Days);
        Assert.Empty(intelligence.RecentSoldComps);
        Assert.Equal(8m, intelligence.Market);
        Assert.Contains("TCGplayer", intelligence.Source);

        // Evaluating the same unchanged state again must not duplicate the crossing event.
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var alerts = scope.ServiceProvider.GetRequiredService<AlertEvaluationService>();
            await alerts.EvaluateAsync(CancellationToken.None);
        }

        var alertsAfterRepeat = (await (await client.GetAsync("/api/alerts"))
            .Content.ReadFromJsonAsync<List<AlertView>>(Json))!;
        Assert.Single(alertsAfterRepeat, a => a.Kind == "below" && a.WatchlistItemId == watched.Id);
    }
}
