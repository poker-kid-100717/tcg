using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PokemonTCG.API.Data;
using PokemonTCG.API.Pricing;

namespace PokemonTcgMarketplace.Backend.Tests
{
    [Collection(TcgplayerCollection.Name)]
    public class TcgplayerSyncTests(TcgplayerFixture fixture)
    {
        private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

        [Fact]
        public async Task With_keys_the_snapshot_takes_prices_and_links_from_tcgplayer()
        {
            var set = fixture.Upstream.AddSet("tp1", "Surging Sparks", "Scarlet & Violet", new DateOnly(2024, 11, 8), 2);
            fixture.Upstream.AddCard(set, "1", "Pikachu ex", "Double Rare", Today.AddDays(-3), ("holofoil", 10m));
            fixture.Upstream.AddCard(set, "2", "Raichu", "Rare", Today.AddDays(-3), ("normal", 1m), ("reverseHolofoil", 2m));
            fixture.TcgplayerApi.AddGroup(501, "SV08: Surging Sparks", new DateTime(2024, 11, 8));
            fixture.TcgplayerApi.AddProduct(9001, 501, "Pikachu ex - 001/191", "001/191");
            fixture.TcgplayerApi.AddProduct(9002, 501, "Raichu", "002/191");
            fixture.TcgplayerApi.AddProductPrice(9001, "Holofoil", 12.50m);
            fixture.TcgplayerApi.AddProductPrice(9002, "Reverse Holofoil", 3.25m);

            var response = await fixture.CreateClient().PostAsync("/internal/snapshots", null);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            using var scope = fixture.Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(501, (await db.Sets.SingleAsync(s => s.Id == "tp1")).TcgplayerGroupId);
            Assert.Equal(9001, (await db.Cards.SingleAsync(c => c.Id == "tp1-1")).TcgplayerProductId);

            // TCGplayer's price wins for today, in both the current price and the dated history.
            var latest = await db.LatestPrices.SingleAsync(p => p.CardId == "tp1-1" && p.Variant == "holofoil");
            Assert.Equal((12.50m, Today), (latest.Market, latest.UpdatedOn));
            Assert.Equal(12.50m, (await db.PriceSnapshots.SingleAsync(s => s.CardId == "tp1-1" && s.Date == Today)).Market);
            Assert.Equal(3.25m, (await db.LatestPrices.SingleAsync(p => p.CardId == "tp1-2" && p.Variant == "reverseHolofoil")).Market);
            // A printing TCGplayer didn't price keeps the Pokémon TCG API's.
            Assert.Equal(1m, (await db.LatestPrices.SingleAsync(p => p.CardId == "tp1-2" && p.Variant == "normal")).Market);

            // Card pages link straight to the TCGplayer product, and the site says where prices come from.
            var card = await fixture.CreateClient().GetFromJsonAsync<JsonElement>("/api/cards/tp1-1");
            Assert.Equal("https://www.tcgplayer.com/product/9001", card.GetProperty("tcgplayerUrl").GetString());
            var status = await fixture.CreateClient().GetFromJsonAsync<JsonElement>("/api/market/status");
            Assert.Equal("TCGplayer API", status.GetProperty("priceSource").GetString());
            Assert.True(status.GetProperty("tcgplayerApi").GetBoolean());
        }
    }
}
