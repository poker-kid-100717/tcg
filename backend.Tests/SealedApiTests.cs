using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PokemonTCG.API.Data;
using PokemonTCG.API.Pricing;

namespace PokemonTcgMarketplace.Backend.Tests;

[Collection(ApiCollection.Name)]
public class SealedApiTests(ApiFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    [Fact]
    public async Task Snapshots_record_sealed_prices_and_compare_them_with_msrp()
    {
        var today = DateOnly.FromDateTime(fixture.Clock.GetUtcNow().UtcDateTime);
        var set = fixture.Upstream.AddSet("seal1", "Sealed Set", "Mega Evolution", new DateOnly(2026, 2, 1), 1);
        fixture.Upstream.AddCard(set, "1", "Sealed Set Pikachu", "Rare", today, ("holofoil", 3m));
        var etb = fixture.Upstream.AddSealed(set, "Sealed Set Elite Trainer Box", 99.98m);
        var bundle = fixture.Upstream.AddSealed(set, "Sealed Set Booster Bundle", 26.94m);
        var unpriced = fixture.Upstream.AddSealed(set, "Sealed Set Premium Collection", null);
        fixture.Upstream.AddSealed(set, "Code Card - Sealed Set Booster Pack", 0.25m);
        var oldSet = fixture.Upstream.AddSet("seal0", "Older Sealed Set", "Sword & Shield", new DateOnly(2021, 2, 1), 1);
        var oldEtb = fixture.Upstream.AddSealed(oldSet, "Older Sealed Set Elite Trainer Box", 120m);

        var run = await (await fixture.CreateClient().PostAsync("/internal/snapshots", null))
            .Content.ReadFromJsonAsync<SnapshotResult>(Json);
        Assert.Equal(SnapshotStatus.Succeeded, run!.Status);

        var list = await Get("/api/sealed?q=Sealed%20Set");
        var etbView = Assert.Single(list.Products, p => p.ProductId == etb);
        Assert.Equal("Elite Trainer Box", etbView.ProductType);
        Assert.Equal("Sealed Set", etbView.SetName);
        Assert.Equal("seal1", etbView.SetId);
        Assert.Equal(99.98m, etbView.Market);
        Assert.Equal(49.99m, etbView.Msrp);
        Assert.Equal("typical", etbView.MsrpSource);
        Assert.Equal(100.0m, etbView.PercentOverMsrp);
        Assert.Equal(today, etbView.AsOf);

        Assert.Equal(0.0m, Assert.Single(list.Products, p => p.ProductId == bundle).PercentOverMsrp);
        var collection = Assert.Single(list.Products, p => p.ProductId == unpriced);
        Assert.Null(collection.Market);
        Assert.Null(collection.Msrp); // no typical MSRP for collections: unknown, not guessed
        Assert.DoesNotContain(list.Products, p => p.Name.Contains("Code Card"));
        Assert.Contains("Elite Trainer Box", list.Types);

        // Older products are left out by default and never get a typical MSRP.
        Assert.DoesNotContain(list.Products, p => p.ProductId == oldEtb);
        var all = await Get("/api/sealed?q=Older%20Sealed%20Set&all=true");
        var old = Assert.Single(all.Products, p => p.ProductId == oldEtb);
        Assert.Null(old.Msrp);
        Assert.Null(old.PercentOverMsrp);

        // Filtering by type.
        var bundles = await Get("/api/sealed?q=Sealed%20Set&type=Booster%20Bundle");
        Assert.All(bundles.Products, p => Assert.Equal("Booster Bundle", p.ProductType));
    }

    private async Task<SealedListView> Get(string path)
    {
        var response = await fixture.CreateClient().GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SealedListView>(Json))!;
    }
}
