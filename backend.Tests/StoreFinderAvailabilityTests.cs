using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using PokemonTCG.API.Product;

namespace PokemonTcgMarketplace.Backend.Tests;

/// <summary>Store Finder is never offered or sold while Inventory.Service has no retailer source.</summary>
[Collection(ApiCollection.Name)]
public class StoreFinderAvailabilityTests(ApiFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Without_a_retailer_source_the_preview_does_not_unlock_Store_Finder()
    {
        using var client = fixture.CreateClient();

        var account = await SessionAsync(client);

        Assert.True(account.IsPro);
        Assert.False(account.HasStoreFinder);
        Assert.False(account.StoreFinderAvailable);
    }

    [Fact]
    public async Task With_a_retailer_source_the_preview_unlocks_Store_Finder()
    {
        await using var app = fixture.Factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Features:StoreFinder", "true"));
        using var client = app.CreateClient();

        var account = await SessionAsync(client);

        Assert.True(account.HasStoreFinder);
        Assert.True(account.StoreFinderAvailable);
    }

    [Theory]
    [InlineData("storefinder")]
    [InlineData("complete")]
    public async Task Store_Finder_plans_cannot_be_bought_without_a_retailer_source(string plan)
    {
        await using var live = fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Billing:Enabled", "true");
            builder.UseSetting("Billing:StripeSecretKey", "sk_test_never_called");
            builder.UseSetting("Billing:StripeWebhookSecret", "whsec_never_called");
            builder.UseSetting("Billing:ProMonthlyPriceId", "price_monthly");
            builder.UseSetting("Billing:ProAnnualPriceId", "price_annual");
            builder.UseSetting("Billing:StoreFinderMonthlyPriceId", "price_store_finder");
            builder.UseSetting("Billing:CompleteMonthlyPriceId", "price_complete");
        });
        using var client = live.CreateClient();

        var account = await SessionAsync(client);
        Assert.False(account.StoreFinderBillingConfigured);

        var response = await client.PostAsJsonAsync("/api/billing/checkout", new { plan });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    private static async Task<AccountView> SessionAsync(HttpClient client)
    {
        var response = await client.PostAsync("/api/session", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AccountView>(Json))!;
    }
}
