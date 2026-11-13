using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using PokemonTCG.API.Data;
using PokemonTCG.API.Product;

namespace PokemonTcgMarketplace.Backend.Tests;

[Collection(ApiCollection.Name)]
public class AccountApiTests(ApiFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Signing_in_turns_a_browser_profile_into_an_account_that_works_on_another_device()
    {
        var email = NewEmail();
        var firstCard = await SeedCardAsync(10m);
        var secondCard = await SeedCardAsync(20m);

        using var laptop = fixture.CreateClient();
        var anonymous = await SessionAsync(laptop);
        Assert.False(anonymous.SignedIn);
        Assert.Null(anonymous.Email);
        Assert.True(anonymous.SignInAvailable);
        await WatchAsync(laptop, firstCard);

        var signedIn = await SignInAsync(laptop, email);
        Assert.True(signedIn.SignedIn);
        Assert.Equal(email, signedIn.Email);
        Assert.Equal(anonymous.Id, signedIn.Id); // the first sign-in keeps the browser's profile
        Assert.Single(await WatchlistAsync(laptop));

        // A second device builds its own anonymous watchlist, which joins the account on sign-in.
        // The address is matched case-insensitively.
        using var phone = fixture.CreateClient();
        await WatchAsync(phone, secondCard);
        var onPhone = await SignInAsync(phone, email.ToUpperInvariant());
        Assert.Equal(signedIn.Id, onPhone.Id);
        Assert.Equal(email, onPhone.Email);

        var expected = new[] { firstCard, secondCard }.Order().ToArray();
        Assert.Equal(expected, (await WatchlistAsync(phone)).Select(w => w.CardId).Order().ToArray());
        Assert.Equal(expected, (await WatchlistAsync(laptop)).Select(w => w.CardId).Order().ToArray());
    }

    [Fact]
    public async Task Signing_out_leaves_the_browser_anonymous_until_it_signs_in_again()
    {
        var email = NewEmail();
        using var client = fixture.CreateClient();
        var account = await SignInAsync(client, email);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/sign-out", null)).StatusCode);
        var afterSignOut = await SessionAsync(client);
        Assert.False(afterSignOut.SignedIn);
        Assert.NotEqual(account.Id, afterSignOut.Id);

        Assert.Equal(account.Id, (await SignInAsync(client, email)).Id);
    }

    [Fact]
    public async Task Only_the_newest_code_works_and_only_once()
    {
        var email = NewEmail();
        using var client = fixture.CreateClient();

        Assert.Equal(HttpStatusCode.Accepted, (await RequestCodeAsync(client, email)).StatusCode);
        var first = fixture.Emails.LatestCodeFor(email);
        Assert.Equal(HttpStatusCode.Accepted, (await RequestCodeAsync(client, email)).StatusCode);
        var second = fixture.Emails.LatestCodeFor(email);

        if (first != second)
            Assert.Equal(HttpStatusCode.BadRequest, (await VerifyAsync(client, email, first)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await VerifyAsync(client, email, second)).StatusCode);

        using var other = fixture.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await VerifyAsync(other, email, second)).StatusCode);
    }

    [Fact]
    public async Task A_code_stops_working_after_five_wrong_guesses()
    {
        var email = NewEmail();
        using var client = fixture.CreateClient();
        Assert.Equal(HttpStatusCode.Accepted, (await RequestCodeAsync(client, email)).StatusCode);
        var code = fixture.Emails.LatestCodeFor(email);
        var wrong = code == "000000" ? "111111" : "000000";

        for (var attempt = 0; attempt < AccountStore.MaxCodeAttempts; attempt++)
            Assert.Equal(HttpStatusCode.BadRequest, (await VerifyAsync(client, email, wrong)).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await VerifyAsync(client, email, code)).StatusCode);
        Assert.False((await SessionAsync(client)).SignedIn);
    }

    [Fact]
    public async Task An_address_can_request_a_limited_number_of_codes_an_hour()
    {
        var email = NewEmail();
        using var client = fixture.CreateClient();
        for (var request = 0; request < SignInService.MaxCodesPerEmailPerHour; request++)
            Assert.Equal(HttpStatusCode.Accepted, (await RequestCodeAsync(client, email)).StatusCode);

        Assert.Equal(HttpStatusCode.TooManyRequests, (await RequestCodeAsync(client, email)).StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("collector@localhost")]
    [InlineData("Collector <collector@example.com>")]
    public async Task Malformed_addresses_are_rejected(string email)
    {
        using var client = fixture.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await RequestCodeAsync(client, email)).StatusCode);
    }

    [Fact]
    public async Task Checkout_requires_a_signed_in_account_when_billing_is_live()
    {
        await using var live = fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Billing:StripeSecretKey", "sk_test_never_called");
            builder.UseSetting("Billing:StripeWebhookSecret", "whsec_never_called");
            builder.UseSetting("Billing:ProMonthlyPriceId", "price_monthly");
            builder.UseSetting("Billing:ProAnnualPriceId", "price_annual");
        });
        using var client = live.CreateClient();

        var response = await client.PostAsJsonAsync("/api/billing/checkout", new { plan = "monthly" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Alert_emails_can_be_turned_off_in_settings_or_from_the_email()
    {
        using var anonymous = fixture.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PutAsJsonAsync("/api/account/preferences", new { emailAlerts = false })).StatusCode);

        var email = NewEmail();
        using var client = fixture.CreateClient();
        var account = await SignInAsync(client, email);
        Assert.True(account.EmailAlerts);

        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PutAsJsonAsync("/api/account/preferences", new { emailAlerts = false })).StatusCode);
        Assert.False((await SessionAsync(client)).EmailAlerts);
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PutAsJsonAsync("/api/account/preferences", new { emailAlerts = true })).StatusCode);

        // The one-click link works without a session, as a mail provider would call it.
        var token = await UnsubscribeTokenAsync(account.Id);
        using var mailProvider = fixture.CreateClient();
        var oneClick = new FormUrlEncodedContent(new Dictionary<string, string> { ["List-Unsubscribe"] = "One-Click" });
        Assert.Equal(HttpStatusCode.NoContent,
            (await mailProvider.PostAsync($"/api/email/unsubscribe?token={token}", oneClick)).StatusCode);
        Assert.False((await SessionAsync(client)).EmailAlerts);

        Assert.Equal(HttpStatusCode.NotFound,
            (await mailProvider.PostAsync($"/api/email/unsubscribe?token={Guid.NewGuid()}", null)).StatusCode);
    }

    [Fact]
    public async Task New_alerts_are_emailed_once_as_a_digest()
    {
        var email = NewEmail();
        var cardId = await SeedCardAsync(10m);
        using var client = fixture.CreateClient();
        await SignInAsync(client, email);
        await WatchAsync(client, cardId, targetBelow: 9m);
        await AddSnapshotAsync(cardId, Today(), 8m);

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<AlertEvaluationService>().EvaluateAsync(CancellationToken.None);
            var alertEmails = scope.ServiceProvider.GetRequiredService<AlertEmailService>();
            Assert.True(await alertEmails.SendPendingAsync(CancellationToken.None) >= 1);
            await alertEmails.SendPendingAsync(CancellationToken.None); // nothing new: no second email
        }

        var digests = fixture.Emails.SentTo(email).Where(m => m.Subject.Contains("watchlist alert")).ToList();
        var digest = Assert.Single(digests);
        Assert.Contains("crossed below your $9.00 target at $8.00", digest.Text);
        Assert.NotNull(digest.Headers);
        Assert.Equal("List-Unsubscribe=One-Click", digest.Headers["List-Unsubscribe-Post"]);
        Assert.Contains("/api/email/unsubscribe?token=", digest.Headers["List-Unsubscribe"]);
    }

    [Fact]
    public async Task Alerts_for_anonymous_profiles_are_never_emailed()
    {
        var cardId = await SeedCardAsync(10m);
        using var client = fixture.CreateClient();
        var anonymous = await SessionAsync(client);
        await WatchAsync(client, cardId, targetBelow: 9m);
        await AddSnapshotAsync(cardId, Today(), 8m);
        var sentBefore = fixture.Emails.Sent.Count;

        using var scope = fixture.Factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AlertEvaluationService>().EvaluateAsync(CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<AlertEmailService>().SendPendingAsync(CancellationToken.None);

        Assert.DoesNotContain(fixture.Emails.Sent.Skip(sentBefore), m => m.Text.Contains(cardId));
        Assert.False(anonymous.SignedIn);
    }

    // ---- helpers ----------------------------------------------------------------------------

    private static string NewEmail() => $"collector-{Guid.NewGuid():N}@example.com";

    private DateOnly Today() => DateOnly.FromDateTime(fixture.Clock.GetUtcNow().UtcDateTime);

    /// <summary>A card with yesterday's market price, ready to be watched.</summary>
    private async Task<string> SeedCardAsync(decimal yesterdayPrice)
    {
        var cardId = $"acct-{Guid.NewGuid():N}";
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Cards.Add(new Card
        {
            Id = cardId,
            Name = $"Card {cardId}",
            Number = "1",
            SetId = "accounts",
            SetName = "Accounts Test Set",
        });
        db.PriceSnapshots.Add(new PriceSnapshot
        {
            CardId = cardId,
            Variant = "holofoil",
            Date = Today().AddDays(-1),
            Market = yesterdayPrice,
        });
        await db.SaveChangesAsync();
        return cardId;
    }

    private async Task AddSnapshotAsync(string cardId, DateOnly date, decimal market)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.PriceSnapshots.Add(new PriceSnapshot { CardId = cardId, Variant = "holofoil", Date = date, Market = market });
        await db.SaveChangesAsync();
    }

    private async Task<Guid> UnsubscribeTokenAsync(Guid userId)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var token = await scope.ServiceProvider.GetRequiredService<AccountStore>()
            .GetUnsubscribeTokenAsync(userId, CancellationToken.None);
        Assert.NotNull(token);
        return token.Value;
    }

    private static async Task<AccountView> SessionAsync(HttpClient client)
    {
        var response = await client.PostAsync("/api/session", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AccountView>(Json))!;
    }

    private static Task<HttpResponseMessage> RequestCodeAsync(HttpClient client, string email) =>
        client.PostAsJsonAsync("/api/auth/code", new { email });

    private static Task<HttpResponseMessage> VerifyAsync(HttpClient client, string email, string code) =>
        client.PostAsJsonAsync("/api/auth/verify", new { email, code });

    private async Task<AccountView> SignInAsync(HttpClient client, string email)
    {
        Assert.Equal(HttpStatusCode.Accepted, (await RequestCodeAsync(client, email)).StatusCode);
        var response = await VerifyAsync(client, email, fixture.Emails.LatestCodeFor(email));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AccountView>(Json))!;
    }

    private static async Task WatchAsync(HttpClient client, string cardId, decimal? targetBelow = null)
    {
        var response = await client.PostAsJsonAsync("/api/watchlist",
            new CreateWatchlistRequest(cardId, "holofoil", "", "", null, targetBelow, null, null));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<List<WatchlistItemView>> WatchlistAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<WatchlistItemView>>("/api/watchlist", Json))!;
}
