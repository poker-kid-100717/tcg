using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PokemonTCG.API.Data;
using PokemonTCG.API.Market.Intelligence;
using PokemonTCG.API.Product;

namespace PokemonTcgMarketplace.Backend.Tests;

/// <summary>The post-snapshot pipeline (signals, then alerts), the Pro signal center, and the Deal Analyzer.</summary>
[Collection(AlertsCollection.Name)]
public class AlertsSignalsDealsTests(AlertsFixture fixture)
{
    private DateOnly Today => DateOnly.FromDateTime(fixture.Clock.GetUtcNow().UtcDateTime);

    /// <summary>A card with <paramref name="days"/> of flat history ending today: market 10, lowest listing <paramref name="low"/>.</summary>
    private async Task<string> SeedHistoryAsync(int days, decimal market = 10m, decimal low = 10m)
    {
        var id = $"h-{Guid.NewGuid():N}"[..20];
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Cards.Add(new Card { Id = id, Name = "History card", Number = "7", SetId = "hist", SetName = "History set" });
        for (var d = days - 1; d >= 0; d--)
            db.PriceSnapshots.Add(new PriceSnapshot { CardId = id, Variant = "holofoil", Date = Today.AddDays(-d), Market = market, Low = low, Mid = market, High = market * 1.2m });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task SetPriceTodayAsync(string cardId, decimal market)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = db.PriceSnapshots.Single(s => s.CardId == cardId && s.Date == Today);
        row.Market = market;
        await db.SaveChangesAsync();
    }

    private async Task<int> RunPipelineAsync()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<SignalRefreshService>().RefreshAsync(CancellationToken.None);
        return await scope.ServiceProvider.GetRequiredService<AlertEvaluationService>().EvaluateAsync(CancellationToken.None);
    }

    private static async Task<List<JsonElement>> Alerts(HttpClient client) =>
        (await client.GetFromJsonAsync<List<JsonElement>>("/api/alerts"))!;

    private static object Watch(string cardId, decimal? below = null, bool sleeper = false) => new
    {
        cardId, variant = "holofoil", cardName = "x", setName = "x", imageUrl = (string?)null,
        targetBelow = below, targetAbove = (decimal?)null, movePercent = (decimal?)null, alertSleeper = sleeper,
    };

    [Fact]
    public async Task A_sleeper_signal_is_stored_shown_in_the_signal_center_and_alerts_once()
    {
        // Listings sit 50% above a flat market reference: the Sleeper rule.
        var cardId = await SeedHistoryAsync(35, market: 10m, low: 15m);
        var (client, userId) = await fixture.SignInAsync();
        using var _c = client;
        await fixture.GrantProAsync(userId);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/watchlist", Watch(cardId, sleeper: true))).StatusCode);

        Assert.True(await RunPipelineAsync() >= 1);
        var alert = Assert.Single(await Alerts(client), a => a.GetProperty("kind").GetString() == AlertEvaluationService.Sleeper);
        Assert.Contains("sleeper", alert.GetProperty("message").GetString());

        // The same state again (a re-run, or the next day with nothing changed) doesn't alert again.
        await RunPipelineAsync();
        Assert.Single(await Alerts(client), a => a.GetProperty("kind").GetString() == AlertEvaluationService.Sleeper);

        var center = await client.GetFromJsonAsync<JsonElement>($"/api/signals?kind={Signals.Sleeper}");
        var item = Assert.Single(center.GetProperty("signals").EnumerateArray(), s => s.GetProperty("cardId").GetString() == cardId);
        Assert.Equal("Sleeper", item.GetProperty("name").GetString());
        Assert.Equal(30, item.GetProperty("lookbackDays").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(item.GetProperty("reason").GetString()));
        Assert.True(center.GetProperty("rules").TryGetProperty(Signals.Sleeper, out _));

        var dashboard = await client.GetFromJsonAsync<JsonElement>("/api/dashboard");
        Assert.Contains(dashboard.GetProperty("watchedSignals").EnumerateArray(), s => s.GetProperty("kind").GetString() == Signals.Sleeper);
        Assert.False(dashboard.GetProperty("signalsLocked").GetBoolean());
        Assert.True(dashboard.GetProperty("market").GetProperty("printingsPriced").GetInt32() >= 1);
    }

    [Fact]
    public async Task A_price_target_fires_on_the_crossing_then_waits_out_its_cooldown()
    {
        var cardId = await SeedHistoryAsync(3);
        var (client, userId) = await fixture.SignInAsync();
        using var _c = client;
        await fixture.GrantProAsync(userId);
        await client.PostAsJsonAsync("/api/watchlist", Watch(cardId, below: 9m));
        int Below(List<JsonElement> alerts) => alerts.Count(a => a.GetProperty("kind").GetString() == AlertEvaluationService.Below);

        await RunPipelineAsync();
        Assert.Equal(0, Below(await Alerts(client))); // 10 is above the 9 target.

        await SetPriceTodayAsync(cardId, 8.5m);
        await RunPipelineAsync();
        Assert.Equal(1, Below(await Alerts(client)));

        // It recovers and dips again the same day: inside the 24h cooldown, no second alert.
        await SetPriceTodayAsync(cardId, 9.5m);
        await RunPipelineAsync();
        await SetPriceTodayAsync(cardId, 8m);
        await RunPipelineAsync();
        Assert.Equal(1, Below(await Alerts(client)));

        try
        {
            // The following day, recovered and then crossing again: a new alert.
            fixture.Clock.Offset = TimeSpan.FromDays(1);
            using (var scope = fixture.Factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.PriceSnapshots.Add(new PriceSnapshot { CardId = cardId, Variant = "holofoil", Date = Today, Market = 9.5m });
                await db.SaveChangesAsync();
            }
            await RunPipelineAsync();
            await SetPriceTodayAsync(cardId, 8m);
            await RunPipelineAsync();
            Assert.Equal(2, Below(await Alerts(client)));
        }
        finally
        {
            fixture.Clock.Offset = TimeSpan.Zero;
        }
    }

    [Fact]
    public async Task Alerts_stop_when_pro_lapses()
    {
        var cardId = await SeedHistoryAsync(3);
        var (client, userId) = await fixture.SignInAsync();
        using var _c = client;
        var subscriptionId = await fixture.GrantProAsync(userId);
        await client.PostAsJsonAsync("/api/watchlist", Watch(cardId, below: 20m));

        fixture.Stripe.SetSubscription(subscriptionId, "cus_lapse", "canceled", FakeStripeApi.MonthlyPrice, userId);
        using var anon = fixture.CreateClient();
        await anon.SendAsync(FakeStripeApi.Webhook("customer.subscription.deleted", new { id = subscriptionId }));

        await RunPipelineAsync();
        Assert.Empty(await Alerts(client));
    }

    [Fact]
    public async Task Signal_center_deal_analyzer_and_long_history_are_pro_only()
    {
        var cardId = await SeedHistoryAsync(3);
        var (client, _) = await fixture.SignInAsync();
        using var _c = client;

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/signals")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/cards/{cardId}/history")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/deals/analyze", new { cardId, variant = "holofoil", askingPrice = 5m })).StatusCode);
        // Presets are visible (the locked page shows them).
        var presets = await client.GetFromJsonAsync<JsonElement>("/api/deals/presets");
        Assert.Contains(presets.EnumerateArray(), p => p.GetProperty("id").GetString() == "local");

        var dashboard = await client.GetFromJsonAsync<JsonElement>("/api/dashboard");
        Assert.True(dashboard.GetProperty("signalsLocked").GetBoolean());
        Assert.Equal(0, dashboard.GetProperty("watchedSignals").GetArrayLength());
    }

    [Fact]
    public async Task The_deal_analyzer_does_the_math_against_the_market_reference()
    {
        var cardId = await SeedHistoryAsync(10, market: 100m, low: 95m);
        var (client, userId) = await fixture.SignInAsync();
        using var _c = client;
        await fixture.GrantProAsync(userId);

        var response = await client.PostAsJsonAsync("/api/deals/analyze", new
        {
            cardId, variant = "holofoil", askingPrice = 80m, inboundShipping = 5m, outboundShipping = 5m, presetId = "local",
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var deal = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(100m, deal.GetProperty("marketReference").GetDecimal());
        Assert.Equal(-20m, deal.GetProperty("askVsMarketPercent").GetDecimal());
        Assert.Equal("Below market reference", deal.GetProperty("position").GetString());
        var math = deal.GetProperty("math");
        Assert.Equal(85m, math.GetProperty("acquisition").GetDecimal());
        Assert.Equal(10m, math.GetProperty("net").GetDecimal());
        Assert.Equal(90m, math.GetProperty("breakEvenSalePrice").GetDecimal());
        Assert.Contains("not financial advice", deal.GetProperty("summary").GetString());
        Assert.Contains(deal.GetProperty("warnings").EnumerateArray(), w => w.GetString()!.Contains("not a sold price"));

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/deals/analyze", new { cardId, variant = "holofoil", askingPrice = -1m })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/deals/analyze", new { cardId = "no-such-card", variant = "holofoil", askingPrice = 1m })).StatusCode);

        var history = await client.GetFromJsonAsync<JsonElement>($"/api/cards/{cardId}/history?days=365");
        Assert.Equal(365, history.GetProperty("historyDays").GetInt32());
    }

    [Theory]
    // asking, inbound, tax, outbound, sale, fee %, fixed → net, break-even
    [InlineData(100, 5, 0, 5, 150, 12.75, 0.30, 20.57, 126.42)]
    [InlineData(50, 0, 0, 0, 50, 0, 0, 0, 50)]
    [InlineData(10, 0, 1, 1, 0, 13.25, 0.40, -12, 14.29)]
    public void Deal_math_includes_every_entered_cost(decimal asking, decimal inbound, decimal tax, decimal outbound, decimal sale,
        decimal percent, decimal fixedFee, decimal net, decimal breakEven)
    {
        var math = DealAnalyzerService.Compute(asking, inbound, tax, outbound, sale, percent, fixedFee);
        Assert.Equal(net, math.Net);
        Assert.Equal(breakEven, math.BreakEvenSalePrice);
    }
}
