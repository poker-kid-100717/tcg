using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using PokemonTCG.API.Collections;
using PokemonTCG.API.Data;

namespace PokemonTcgMarketplace.Backend.Tests
{
    [Collection(ApiCollection.Name)]
    public class CollectionApiTests(ApiFixture fixture)
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        {
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
        };
        private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

        /// <summary>A browser: keeps cookies and marks its API calls the way the site's fetch wrapper does.</summary>
        private HttpClient Browser()
        {
            var client = fixture.Factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
            client.DefaultRequestHeaders.Add("X-Requested-With", "fetch");
            return client;
        }

        private async Task<T> Read<T>(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
        {
            Assert.True(response.StatusCode == expected, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
            return (await response.Content.ReadFromJsonAsync<T>(Json))!;
        }

        /// <summary>A small set, snapshotted so it's in the local catalog with prices.</summary>
        private async Task<string> SeedSet(string id)
        {
            var set = fixture.Upstream.AddSet(id, $"Set {id}", "Test Series", new DateOnly(2025, 1, 1), 3);
            set["printedTotal"] = 2; // Card 3 is a secret rare, numbered past the printed total.
            fixture.Upstream.AddCard(set, "1", "Pikachu", "Common", Today, ("normal", 2.00m), ("reverseHolofoil", 4.00m));
            fixture.Upstream.AddCard(set, "2", "Raichu", "Rare Holo", Today, ("holofoil", 20.00m));
            fixture.Upstream.AddCard(set, "3", "Golden Pikachu", "Secret Rare", Today, ("holofoil", 100.00m));
            Assert.Equal(HttpStatusCode.OK, (await fixture.CreateClient().PostAsync("/internal/snapshots", null)).StatusCode);
            return id;
        }

        [Fact]
        public async Task A_collection_needs_a_session()
        {
            var client = Browser();
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/collection")).StatusCode);
            Assert.False((await client.GetFromJsonAsync<AccountView>("/api/account", Json))!.SignedIn);
        }

        [Fact]
        public async Task A_guest_adds_edits_values_and_removes_cards()
        {
            var set = await SeedSet("col1");
            var client = Browser();
            var guest = await Read<AccountView>(await client.PostAsync("/api/account/guest", null));
            Assert.True(guest is { SignedIn: true, IsGuest: true });

            var added = await Read<CardOwnership>(await client.PostAsJsonAsync("/api/collection/items",
                new AddItemRequest($"{set}-2", "holofoil", CardCondition.LightlyPlayed, 2, 15.00m), Json));
            var item = Assert.Single(added.Items);
            Assert.Equal(2, item.Quantity);
            Assert.Equal(20.00m, item.MarketEach);
            Assert.Equal(17.00m, item.ValueEach); // Lightly played: 85% of market.

            // Adding the same printing in the same condition stacks, averaging the cost.
            added = await Read<CardOwnership>(await client.PostAsJsonAsync("/api/collection/items",
                new AddItemRequest($"{set}-2", "holofoil", CardCondition.LightlyPlayed, 1, 18.00m), Json));
            item = Assert.Single(added.Items);
            Assert.Equal(3, item.Quantity);
            Assert.Equal(16.00m, item.CostEach);

            // A printing the card doesn't come in is refused.
            var wrong = await client.PostAsJsonAsync("/api/collection/items", new AddItemRequest($"{set}-2", "normal"), Json);
            Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);

            var view = await Read<CollectionView>(await client.GetAsync("/api/collection"));
            Assert.Equal(3, view.Summary.Cards);
            Assert.Equal(60.00m, view.Summary.MarketValue);
            Assert.Equal(51.00m, view.Summary.ConditionValue);
            Assert.Equal(48.00m, view.Summary.CostBasis);
            Assert.True(view.Summary.NetIfSold < view.Summary.ConditionValue);
            var progress = Assert.Single(view.Sets, s => s.SetId == set);
            Assert.Equal(1, progress.Owned);

            var updated = await Read<CardOwnership>(await client.PatchAsJsonAsync($"/api/collection/items/{item.Id}",
                new UpdateItemRequest(Quantity: 1, Condition: CardCondition.NearMint), Json));
            Assert.Equal((1, CardCondition.NearMint, 20.00m), (updated.Items[0].Quantity, updated.Items[0].Condition, updated.Items[0].ValueEach));

            Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/collection/items/{item.Id}")).StatusCode);
            Assert.Empty((await Read<CollectionView>(await client.GetAsync("/api/collection"))).Items);
        }

        [Fact]
        public async Task Another_collectors_cards_are_out_of_reach()
        {
            var set = await SeedSet("col2");
            var alice = Browser();
            await alice.PostAsync("/api/account/guest", null);
            var item = (await Read<CardOwnership>(await alice.PostAsJsonAsync("/api/collection/items", new AddItemRequest($"{set}-1", "normal"), Json))).Items[0];

            var mallory = Browser();
            await mallory.PostAsync("/api/account/guest", null);
            Assert.Equal(HttpStatusCode.NotFound, (await mallory.DeleteAsync($"/api/collection/items/{item.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await mallory.PatchAsJsonAsync($"/api/collection/items/{item.Id}", new UpdateItemRequest(Quantity: 9), Json)).StatusCode);
            Assert.Single((await Read<CollectionView>(await alice.GetAsync("/api/collection"))).Items);
        }

        [Fact]
        public async Task Cross_site_and_form_requests_are_refused()
        {
            var client = fixture.Factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
            // No JSON and no X-Requested-With: what a plain HTML form or image tag would send.
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/api/account/guest", null)).StatusCode);
            var form = new StringContent("cardId=x", Encoding.UTF8, "application/x-www-form-urlencoded");
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/api/collection/items", form)).StatusCode);

            var request = new HttpRequestMessage(HttpMethod.Post, "/api/account/guest") { Content = JsonContent.Create(new { }) };
            request.Headers.Add("Origin", "https://evil.example");
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(request)).StatusCode);

            var sameSite = new HttpRequestMessage(HttpMethod.Post, "/api/account/guest") { Content = JsonContent.Create(new { }) };
            sameSite.Headers.Add("Origin", "http://localhost");
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(sameSite)).StatusCode);
        }

        [Fact]
        public async Task Saving_a_guest_keeps_its_cards_and_signing_in_merges_another_guest()
        {
            var set = await SeedSet("col3");
            var email = $"collector-{Guid.NewGuid():N}@example.com";

            var first = Browser();
            await first.PostAsync("/api/account/guest", null);
            await first.PostAsJsonAsync("/api/collection/items", new AddItemRequest($"{set}-1", "normal", Quantity: 2, CostEach: 1m), Json);
            var weak = await first.PostAsJsonAsync("/api/account/register", new Credentials(email, "short"), Json);
            Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
            var saved = await Read<AccountView>(await first.PostAsJsonAsync("/api/account/register", new Credentials(email, "a long passphrase"), Json));
            Assert.Equal((true, false, email), (saved.SignedIn, saved.IsGuest, saved.Email));
            Assert.Single((await Read<CollectionView>(await first.GetAsync("/api/collection"))).Items);

            // Same email again is a conflict, not a second account.
            var again = Browser();
            await again.PostAsync("/api/account/guest", null);
            Assert.Equal(HttpStatusCode.Conflict, (await again.PostAsJsonAsync("/api/account/register", new Credentials(email, "another passphrase"), Json)).StatusCode);

            // A second device starts as a guest, collects, then signs in: its cards join the account.
            await again.PostAsJsonAsync("/api/collection/items", new AddItemRequest($"{set}-1", "normal", Quantity: 1, CostEach: 4m), Json);
            await again.PostAsJsonAsync("/api/collection/items", new AddItemRequest($"{set}-2", "holofoil"), Json);
            Assert.Equal(HttpStatusCode.Unauthorized, (await again.PostAsJsonAsync("/api/account/login", new Credentials(email, "wrong passphrase"), Json)).StatusCode);
            await Read<AccountView>(await again.PostAsJsonAsync("/api/account/login", new Credentials(email, "a long passphrase"), Json));

            var merged = await Read<CollectionView>(await again.GetAsync("/api/collection"));
            Assert.Equal(2, merged.Items.Count);
            var pikachu = Assert.Single(merged.Items, i => i.CardId == $"{set}-1");
            Assert.Equal((3, 2.00m), (pikachu.Quantity, pikachu.CostEach));

            await again.PostAsync("/api/account/logout", null);
            Assert.Equal(HttpStatusCode.Unauthorized, (await again.GetAsync("/api/collection")).StatusCode);
        }

        [Fact]
        public async Task Set_checklist_prices_whats_missing_and_the_wishlist_tracks_targets()
        {
            var set = await SeedSet("col4");
            var client = Browser();
            await client.PostAsync("/api/account/guest", null);
            await client.PostAsJsonAsync("/api/collection/items", new AddItemRequest($"{set}-1", "normal"), Json);

            var checklist = await Read<SetChecklist>(await client.GetAsync($"/api/collection/sets/{set}"));
            Assert.Equal($"{set}-1", Assert.Single(checklist.Owned).CardId);
            Assert.Equal([$"{set}-2", $"{set}-3"], checklist.Missing.Select(m => m.CardId));
            Assert.True(Assert.Single(checklist.Missing, m => m.CardId == $"{set}-3").Secret);
            Assert.Equal(20.00m, checklist.CostToCompleteBase);
            Assert.Equal(120.00m, checklist.CostToCompleteAll);

            var wish = await Read<WishlistEntry>(await client.PostAsJsonAsync("/api/wishlist", new WishRequest($"{set}-3", "holofoil", 120m), Json));
            Assert.True(wish.AtOrBelowTarget);
            wish = await Read<WishlistEntry>(await client.PostAsJsonAsync("/api/wishlist", new WishRequest($"{set}-3", "holofoil", 50m), Json));
            Assert.False(wish.AtOrBelowTarget);
            Assert.Single(await Read<List<WishlistEntry>>(await client.GetAsync("/api/wishlist")));
            Assert.NotNull((await Read<CardOwnership>(await client.GetAsync($"/api/collection/cards/{set}-3"))).Wish);

            Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/wishlist/{wish.Id}")).StatusCode);
            Assert.Empty(await Read<List<WishlistEntry>>(await client.GetAsync("/api/wishlist")));
        }

        [Fact]
        public async Task Export_is_a_spreadsheet_safe_csv()
        {
            var set = await SeedSet("col5");
            var client = Browser();
            await client.PostAsync("/api/account/guest", null);
            await client.PostAsJsonAsync("/api/collection/items", new AddItemRequest($"{set}-2", "holofoil", Notes: "=HYPERLINK(\"x\")"), Json);

            var response = await client.GetAsync("/api/collection/export.csv");
            Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
            var csv = await response.Content.ReadAsStringAsync();
            Assert.StartsWith("Card ID,", csv);
            Assert.Contains("Raichu", csv);
            Assert.DoesNotContain(",\"=HYPERLINK", csv);
            Assert.Contains("'=HYPERLINK", csv);
        }

        [Fact]
        public async Task The_sample_fills_an_empty_collection_once()
        {
            await SeedSet("col6");
            var client = Browser();
            await client.PostAsync("/api/account/guest", null);

            var sample = await Read<JsonElement>(await client.PostAsync("/api/collection/sample", null));
            Assert.True(sample.GetProperty("added").GetInt32() > 0);
            Assert.NotEmpty((await Read<CollectionView>(await client.GetAsync("/api/collection"))).Items);
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync("/api/collection/sample", null)).StatusCode);
        }

        [Fact]
        public async Task Deleting_the_account_removes_the_collection()
        {
            var set = await SeedSet("col7");
            var client = Browser();
            await client.PostAsync("/api/account/guest", null);
            await client.PostAsJsonAsync("/api/collection/items", new AddItemRequest($"{set}-1", "normal"), Json);

            Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/account")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/collection")).StatusCode);
        }
        [Fact]
        public async Task A_master_set_counts_every_printing_of_every_card()
        {
            var set = await SeedSet("mst1");
            var client = Browser();
            await client.PostAsync("/api/account/guest", null);
            await client.PostAsJsonAsync("/api/collection/items", new AddItemRequest($"{set}-1", "normal"), Json);

            var checklist = await Read<SetChecklist>(await client.GetAsync($"/api/collection/sets/{set}"));
            // Pikachu comes in normal and reverse holo, Raichu and the secret Golden Pikachu in holofoil: 4 printings.
            Assert.Equal((1, 4), (checklist.Master.Owned, checklist.Master.Total));
            Assert.Equal(
                [($"{set}-1", "reverseHolofoil", 4.00m), ($"{set}-2", "holofoil", 20.00m), ($"{set}-3", "holofoil", 100.00m)],
                checklist.Master.Missing.Select(m => (m.CardId, m.Variant, m.Price)));
            Assert.Equal("Reverse Holofoil", checklist.Master.Missing[0].VariantLabel);
            Assert.Equal(124.00m, checklist.Master.CostToComplete);

            // Owning the reverse holo in any condition fills its slot.
            await client.PostAsJsonAsync("/api/collection/items", new AddItemRequest($"{set}-1", "reverseHolofoil", CardCondition.HeavilyPlayed), Json);
            checklist = await Read<SetChecklist>(await client.GetAsync($"/api/collection/sets/{set}"));
            Assert.Equal((2, 4, 120.00m), (checklist.Master.Owned, checklist.Master.Total, checklist.Master.CostToComplete));
            // The checklist also reports every kind of goal, so the set page can show whichever is picked.
            Assert.Equal(
                [(SetGoalKind.MainSet, 1, 2), (SetGoalKind.FullSet, 1, 3), (SetGoalKind.MasterSet, 2, 4)],
                checklist.Progress.Select(p => (p.Kind, p.Owned, p.Total)));
        }

        [Fact]
        public async Task Collectors_set_goals_and_track_each_one()
        {
            var main = await SeedSet("gol1");
            var master = await SeedSet("gol2");
            var client = Browser();
            await client.PostAsync("/api/account/guest", null);
            await client.PostAsJsonAsync("/api/collection/items", new AddItemRequest($"{main}-1", "normal"), Json);

            Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/collection/goals/{main}", new SetGoalRequest(SetGoalKind.MainSet), Json)).StatusCode);
            // A goal can start before owning a single card of the set.
            Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/collection/goals/{master}", new SetGoalRequest(SetGoalKind.MasterSet), Json)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync("/api/collection/goals/nope", new SetGoalRequest(SetGoalKind.MasterSet), Json)).StatusCode);

            var goals = (await Read<CollectionView>(await client.GetAsync("/api/collection"))).Goals;
            var mainGoal = Assert.Single(goals, g => g.SetId == main);
            Assert.Equal((SetGoalKind.MainSet, 1, 2, 20.00m), (mainGoal.Kind, mainGoal.Owned, mainGoal.Total, mainGoal.CostToComplete));
            var masterGoal = Assert.Single(goals, g => g.SetId == master);
            Assert.Equal((SetGoalKind.MasterSet, 0, 4, 126.00m), (masterGoal.Kind, masterGoal.Owned, masterGoal.Total, masterGoal.CostToComplete));

            // Changing the goal re-targets it; the set page reports the current goal.
            await client.PutAsJsonAsync($"/api/collection/goals/{main}", new SetGoalRequest(SetGoalKind.FullSet), Json);
            var full = Assert.Single((await Read<CollectionView>(await client.GetAsync("/api/collection"))).Goals, g => g.SetId == main);
            Assert.Equal((SetGoalKind.FullSet, 1, 3, 120.00m), (full.Kind, full.Owned, full.Total, full.CostToComplete));
            Assert.Equal(SetGoalKind.FullSet, (await Read<SetChecklist>(await client.GetAsync($"/api/collection/sets/{main}"))).Goal);

            Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/collection/goals/{main}")).StatusCode);
            Assert.DoesNotContain((await Read<CollectionView>(await client.GetAsync("/api/collection"))).Goals, g => g.SetId == main);
        }
    }
}
