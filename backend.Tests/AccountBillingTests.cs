using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using PokemonTCG.API.Accounts;

namespace PokemonTcgMarketplace.Backend.Tests;

/// <summary>Sign-in, server-side Pro gating, CSRF, ownership checks, and Stripe billing (against <see cref="FakeStripeApi"/>).</summary>
[Collection(ApiCollection.Name)]
public class AccountBillingTests(ApiFixture fixture)
{
    private static async Task<JsonElement> Me(HttpClient client) => await client.GetFromJsonAsync<JsonElement>("/api/me");
    private static async Task<bool> IsPro(HttpClient client) => (await Me(client)).GetProperty("account").GetProperty("isPro").GetBoolean();
    private static async Task<string?> ProblemType(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).TryGetProperty("type", out var t) ? t.GetString() : null;

    private static object Watch(string cardId, decimal? below = null, bool sleeper = false) => new
    {
        cardId, variant = "holofoil", cardName = "x", setName = "x", imageUrl = (string?)null,
        targetBelow = below, targetAbove = (decimal?)null, movePercent = (decimal?)null, alertSleeper = sleeper,
    };

    [Fact]
    public async Task Anonymous_requests_get_401_and_public_endpoints_stay_open()
    {
        using var client = fixture.CreateClient();

        var watchlist = await client.GetAsync("/api/watchlist");
        Assert.Equal(HttpStatusCode.Unauthorized, watchlist.StatusCode);
        Assert.Equal("sign_in_required", await ProblemType(watchlist));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/cards/any/intelligence")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/billing/subscription")).StatusCode);

        Assert.False((await Me(client)).GetProperty("signedIn").GetBoolean());
        var plans = await client.GetFromJsonAsync<JsonElement>("/api/billing/plans");
        Assert.Contains(plans.EnumerateArray(), p => p.GetProperty("id").GetString() == "monthly" && p.GetProperty("price").GetDecimal() == 9.99m);
        Assert.Contains(plans.EnumerateArray(), p => p.GetProperty("id").GetString() == "annual" && p.GetProperty("price").GetDecimal() == 79m);
    }

    [Fact]
    public async Task Local_sign_in_sets_an_http_only_cookie_and_logout_ends_the_session()
    {
        using var client = fixture.Factory.CreateClient(new() { HandleCookies = false });
        var login = await client.PostAsJsonAsync("/api/auth/local-login", new { email = $"{Guid.NewGuid():N}@example.test" });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        var cookie = Assert.Single(login.Headers.GetValues("Set-Cookie"), c => c.StartsWith("tcg_auth="));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);

        var (signedIn, _) = await fixture.SignInAsync();
        using var _c = signedIn;
        Assert.True((await Me(signedIn)).GetProperty("signedIn").GetBoolean());
        Assert.Equal(HttpStatusCode.NoContent, (await signedIn.PostAsJsonAsync("/api/auth/logout", new { })).StatusCode);
        Assert.False((await Me(signedIn)).GetProperty("signedIn").GetBoolean());
    }

    [Fact]
    public async Task Sign_in_with_the_same_identity_returns_the_same_account()
    {
        var email = $"{Guid.NewGuid():N}@example.test";
        var (first, a) = await fixture.SignInAsync(email);
        var (second, b) = await fixture.SignInAsync(email);
        first.Dispose();
        second.Dispose();
        Assert.Equal(a, b);
    }

    [Fact]
    public async Task Provider_sign_in_is_unavailable_until_configured()
    {
        using var client = fixture.CreateClient();
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/api/auth/login")).StatusCode);
        var options = await client.GetFromJsonAsync<JsonElement>("/api/auth/options");
        Assert.True(options.GetProperty("localLogin").GetBoolean());
    }

    [Theory]
    [InlineData(null, "/")]
    [InlineData("/dashboard", "/dashboard")]
    [InlineData("/cards/sv3pt5-199?variant=holofoil", "/cards/sv3pt5-199?variant=holofoil")]
    [InlineData("https://evil.example/", "/")]
    [InlineData("//evil.example/", "/")]
    [InlineData("/\\evil.example", "/")]
    [InlineData("javascript:alert(1)", "/")]
    public void Return_urls_are_local_paths_only(string? input, string expected) =>
        Assert.Equal(expected, AuthEndpoints.SafeReturnUrl(input));

    [Fact]
    public async Task Free_accounts_are_refused_pro_features_on_the_server()
    {
        var (client, _) = await fixture.SignInAsync();
        using var _c = client;

        var intelligence = await client.GetAsync("/api/cards/any/intelligence");
        Assert.Equal(HttpStatusCode.Forbidden, intelligence.StatusCode);
        Assert.Equal("pro_required", await ProblemType(intelligence));

        // Alerts are Pro; watching without alerts is free up to the limit.
        var cards = new List<string>();
        for (var i = 0; i <= PokemonTCG.API.Product.ProductEndpoints.FreeWatchlistLimit; i++) cards.Add(await fixture.SeedCardAsync());
        var withAlert = await client.PostAsJsonAsync("/api/watchlist", Watch(cards[0], below: 5m));
        Assert.Equal(HttpStatusCode.Forbidden, withAlert.StatusCode);
        Assert.Equal("pro_required", await ProblemType(withAlert));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/watchlist", Watch(cards[0], sleeper: true))).StatusCode);

        for (var i = 0; i < PokemonTCG.API.Product.ProductEndpoints.FreeWatchlistLimit; i++)
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/watchlist", Watch(cards[i]))).StatusCode);
        var overLimit = await client.PostAsJsonAsync("/api/watchlist", Watch(cards[^1]));
        Assert.Equal(HttpStatusCode.Forbidden, overLimit.StatusCode);
        Assert.Equal("pro_required", await ProblemType(overLimit));
    }

    [Fact]
    public async Task Cross_site_and_form_posts_are_rejected()
    {
        var (client, _) = await fixture.SignInAsync();
        using var _c = client;

        var form = await client.PostAsync("/api/alerts/read-all", new FormUrlEncodedContent([]));
        Assert.Equal(HttpStatusCode.Forbidden, form.StatusCode);
        Assert.Equal("csrf", await ProblemType(form));

        using var crossSite = new HttpRequestMessage(HttpMethod.Post, "/api/alerts/read-all") { Content = JsonContent.Create(new { }) };
        crossSite.Headers.Add("Origin", "https://evil.example");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(crossSite)).StatusCode);

        using var sameSite = new HttpRequestMessage(HttpMethod.Post, "/api/alerts/read-all");
        sameSite.Headers.Add("X-Requested-With", "fetch");
        Assert.True((await client.SendAsync(sameSite)).IsSuccessStatusCode);
    }

    [Fact]
    public async Task One_account_cannot_read_or_change_another_accounts_watchlist()
    {
        var (owner, _) = await fixture.SignInAsync();
        var (other, _) = await fixture.SignInAsync();
        using var _o = owner;
        using var _x = other;

        var created = await (await owner.PostAsJsonAsync("/api/watchlist", Watch(await fixture.SeedCardAsync()))).Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetInt64();

        Assert.Equal(0, (await other.GetFromJsonAsync<JsonElement>("/api/watchlist")).GetArrayLength());
        var update = await other.PutAsJsonAsync($"/api/watchlist/{id}", new { enabled = false });
        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.SendAsync(Delete($"/api/watchlist/{id}"))).StatusCode);
        Assert.Equal(1, (await owner.GetFromJsonAsync<JsonElement>("/api/watchlist")).GetArrayLength());
    }

    [Fact]
    public async Task Checkout_returns_stripes_url_but_only_the_verified_webhook_grants_pro()
    {
        var (client, userId) = await fixture.SignInAsync();
        using var _c = client;

        var checkout = await client.PostAsJsonAsync("/api/billing/checkout", new { plan = "annual" });
        Assert.Equal(HttpStatusCode.OK, checkout.StatusCode);
        Assert.StartsWith("https://checkout.stripe.test/", (await checkout.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("url").GetString());
        var sent = fixture.Stripe.Calls.Last(c => c.Path == "/v1/checkout/sessions").Body;
        Assert.Contains(Uri.EscapeDataString(FakeStripeApi.AnnualPrice), sent);
        Assert.Contains($"client_reference_id={userId}", sent);

        // Coming back from Checkout changes nothing by itself.
        await client.GetAsync("/dashboard?checkout=success");
        Assert.False(await IsPro(client));

        await fixture.GrantProAsync(userId);
        Assert.True(await IsPro(client));
        var subscription = await client.GetFromJsonAsync<JsonElement>("/api/billing/subscription");
        Assert.Equal("active", subscription.GetProperty("status").GetString());
        Assert.True(subscription.GetProperty("canManageBilling").GetBoolean());

        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/billing/checkout", new { plan = "monthly" })).StatusCode);
        var portal = await client.PostAsJsonAsync("/api/billing/portal", new { });
        Assert.Equal("https://billing.stripe.test/p/session", (await portal.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("url").GetString());
    }

    [Fact]
    public async Task Webhooks_with_bad_or_stale_signatures_are_rejected()
    {
        var (client, userId) = await fixture.SignInAsync();
        using var _c = client;
        var subscriptionId = $"sub_{Guid.NewGuid():N}";
        fixture.Stripe.SetSubscription(subscriptionId, "cus_bad", "active", FakeStripeApi.MonthlyPrice, userId);
        var checkout = new { id = "cs", client_reference_id = userId.ToString(), customer = "cus_bad", subscription = subscriptionId };

        using var anon = fixture.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await anon.SendAsync(FakeStripeApi.Webhook("checkout.session.completed", checkout, secret: "whsec_wrong"))).StatusCode);
        var stale = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds();
        Assert.Equal(HttpStatusCode.BadRequest, (await anon.SendAsync(FakeStripeApi.Webhook("checkout.session.completed", checkout, timestamp: stale))).StatusCode);

        using var unsigned = new HttpRequestMessage(HttpMethod.Post, "/api/billing/webhook") { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        Assert.Equal(HttpStatusCode.BadRequest, (await anon.SendAsync(unsigned)).StatusCode);

        Assert.False(await IsPro(client));
    }

    [Fact]
    public async Task A_duplicate_webhook_event_is_applied_once()
    {
        var (client, userId) = await fixture.SignInAsync();
        using var _c = client;
        var subscriptionId = $"sub_{Guid.NewGuid():N}";
        fixture.Stripe.SetSubscription(subscriptionId, "cus_dup", "active", FakeStripeApi.MonthlyPrice, userId);
        var eventId = $"evt_{Guid.NewGuid():N}";
        var body = new { id = subscriptionId, customer = "cus_dup", metadata = new { user_id = userId.ToString() } };

        using var anon = fixture.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await anon.SendAsync(FakeStripeApi.Webhook("customer.subscription.created", body, eventId))).StatusCode);
        var reads = fixture.Stripe.Calls.Count(c => c.Path.EndsWith(subscriptionId));
        Assert.Equal(HttpStatusCode.OK, (await anon.SendAsync(FakeStripeApi.Webhook("customer.subscription.created", body, eventId))).StatusCode);

        Assert.Equal(reads, fixture.Stripe.Calls.Count(c => c.Path.EndsWith(subscriptionId)));
        Assert.True(await IsPro(client));
    }

    [Fact]
    public async Task Late_arriving_events_cannot_restore_a_cancelled_subscription()
    {
        var (client, userId) = await fixture.SignInAsync();
        using var _c = client;
        var subscriptionId = await fixture.GrantProAsync(userId);
        using var anon = fixture.CreateClient();

        fixture.Stripe.SetSubscription(subscriptionId, "cus_late", "canceled", FakeStripeApi.MonthlyPrice, userId);
        await anon.SendAsync(FakeStripeApi.Webhook("customer.subscription.deleted", new { id = subscriptionId, status = "canceled" }));
        Assert.False(await IsPro(client));

        // An older "active" event delivered late: the handler re-reads Stripe, which says cancelled.
        await anon.SendAsync(FakeStripeApi.Webhook("customer.subscription.updated", new { id = subscriptionId, status = "active" }));
        Assert.False(await IsPro(client));
        Assert.Equal("free", (await Me(client)).GetProperty("account").GetProperty("plan").GetString());
    }

    [Fact]
    public async Task A_failed_payment_flags_the_account_and_a_paid_invoice_clears_it()
    {
        var (client, userId) = await fixture.SignInAsync();
        using var _c = client;
        var subscriptionId = await fixture.GrantProAsync(userId);
        using var anon = fixture.CreateClient();

        fixture.Stripe.SetSubscription(subscriptionId, "cus_pay", "past_due", FakeStripeApi.MonthlyPrice, userId);
        await anon.SendAsync(FakeStripeApi.Webhook("invoice.payment_failed", new { id = "in_1", customer = "cus_pay", subscription = subscriptionId }));
        var pastDue = await client.GetFromJsonAsync<JsonElement>("/api/billing/subscription");
        Assert.True(pastDue.GetProperty("paymentIssue").GetBoolean());
        Assert.True(pastDue.GetProperty("isPro").GetBoolean()); // Stripe is still retrying: keep access meanwhile.

        fixture.Stripe.SetSubscription(subscriptionId, "cus_pay", "unpaid", FakeStripeApi.MonthlyPrice, userId);
        await anon.SendAsync(FakeStripeApi.Webhook("invoice.payment_failed", new { id = "in_2", customer = "cus_pay", subscription = subscriptionId }));
        Assert.False(await IsPro(client));

        fixture.Stripe.SetSubscription(subscriptionId, "cus_pay", "active", FakeStripeApi.MonthlyPrice, userId);
        // Newer API versions put the subscription under parent.subscription_details.
        await anon.SendAsync(FakeStripeApi.Webhook("invoice.paid", new
        {
            id = "in_3", customer = "cus_pay", parent = new { subscription_details = new { subscription = subscriptionId } },
        }));
        var restored = await client.GetFromJsonAsync<JsonElement>("/api/billing/subscription");
        Assert.False(restored.GetProperty("paymentIssue").GetBoolean());
        Assert.True(restored.GetProperty("isPro").GetBoolean());
    }

    [Fact]
    public async Task Deleting_an_account_cancels_billing_and_removes_the_account()
    {
        var email = $"{Guid.NewGuid():N}@example.test";
        var (client, userId) = await fixture.SignInAsync(email);
        using var _c = client;
        var subscriptionId = await fixture.GrantProAsync(userId);
        await client.PostAsJsonAsync("/api/watchlist", Watch(await fixture.SeedCardAsync()));

        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(Delete("/api/account"))).StatusCode);
        Assert.Contains(fixture.Stripe.Calls, c => c.Method == HttpMethod.Delete && c.Path.EndsWith(subscriptionId));
        Assert.False((await Me(client)).GetProperty("signedIn").GetBoolean());

        // Signing in again with the same identity starts a fresh, free account.
        var (again, newId) = await fixture.SignInAsync(email);
        using var _a = again;
        Assert.NotEqual(userId, newId);
        Assert.False(await IsPro(again));
        Assert.Equal(0, (await again.GetFromJsonAsync<JsonElement>("/api/watchlist")).GetArrayLength());
    }

    private static HttpRequestMessage Delete(string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, path);
        request.Headers.Add("X-Requested-With", "fetch");
        return request;
    }
}
