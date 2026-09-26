using System.Net;
using PokemonTCG.API.Pricing.Tcgplayer;

namespace PokemonTcgMarketplace.Backend.Tests.Tcgplayer
{
    public class TcgplayerAuthTests
    {
        private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        [Fact]
        public async Task Token_request_posts_client_credentials_form()
        {
            var api = new FakeTcgplayerApi();
            var tokens = api.CreateTokenProvider(new ManualTimeProvider(Start));

            var token = await tokens.GetTokenAsync();

            Assert.Equal("token-1", token);
            var request = Assert.Single(api.Requests);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://tcgplayer.test/token", request.RequestUri!.ToString());
            var form = Assert.Single(api.TokenRequests);
            Assert.Equal("client_credentials", form["grant_type"]);
            Assert.Equal(FakeTcgplayerApi.PublicKey, form["client_id"]);
            Assert.Equal(FakeTcgplayerApi.PrivateKey, form["client_secret"]);
        }

        [Fact]
        public async Task Token_is_cached_until_five_minutes_before_expiry()
        {
            var api = new FakeTcgplayerApi { ExpiresIn = 3600 };
            var clock = new ManualTimeProvider(Start);
            var tokens = api.CreateTokenProvider(clock);

            Assert.Equal("token-1", await tokens.GetTokenAsync());
            clock.Advance(TimeSpan.FromMinutes(54));
            Assert.Equal("token-1", await tokens.GetTokenAsync());
            Assert.Single(api.TokenRequests);

            clock.Advance(TimeSpan.FromMinutes(1)); // 55 min: inside the 5-minute margin
            Assert.Equal("token-2", await tokens.GetTokenAsync());
            Assert.Equal(2, api.TokenRequests.Count);
        }

        [Fact]
        public async Task Concurrent_callers_share_one_token_request()
        {
            var api = new FakeTcgplayerApi();
            var tokens = api.CreateTokenProvider(new ManualTimeProvider(Start));

            var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => tokens.GetTokenAsync())));

            Assert.All(results, t => Assert.Equal("token-1", t));
            Assert.Single(api.TokenRequests);
        }

        [Fact]
        public async Task Unconfigured_keys_throw_without_calling_the_api()
        {
            var api = new FakeTcgplayerApi();
            var options = new TcgplayerOptions();
            Assert.False(options.IsConfigured);
            var tokens = api.CreateTokenProvider(new ManualTimeProvider(Start), options);

            await Assert.ThrowsAsync<InvalidOperationException>(() => tokens.GetTokenAsync());
            Assert.Empty(api.Requests);
        }

        [Fact]
        public async Task Rejected_credentials_throw_with_status()
        {
            var api = new FakeTcgplayerApi();
            var options = FakeTcgplayerApi.CreateOptions();
            options.PrivateKey = "wrong";
            var tokens = api.CreateTokenProvider(new ManualTimeProvider(Start), options);

            var ex = await Assert.ThrowsAsync<TcgplayerApiException>(() => tokens.GetTokenAsync());
            Assert.Equal(400, ex.StatusCode);
            Assert.DoesNotContain("wrong", ex.Message);
        }

        [Fact]
        public async Task Requests_carry_bearer_token()
        {
            var api = new FakeTcgplayerApi();
            var client = api.CreateClient();

            await client.GetLanguagesAsync(3);

            var request = Assert.Single(api.ApiRequests);
            Assert.Equal("bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal("token-1", request.Headers.Authorization.Parameter);
        }

        [Fact]
        public async Task A_401_refreshes_the_token_once_and_retries()
        {
            var api = new FakeTcgplayerApi();
            var tokens = api.CreateTokenProvider(new ManualTimeProvider(Start));
            var client = api.CreateClient(tokens);
            await client.GetLanguagesAsync(3);

            api.RevokedTokens.Add("token-1"); // e.g. revoked server-side before its expiry
            var languages = await client.GetLanguagesAsync(3);

            Assert.Equal("English", Assert.Single(languages).Name);
            Assert.Equal(["token-1", "token-2"], api.IssuedTokens);
            Assert.Equal(3, api.ApiRequests.Count()); // first call, rejected attempt, retry
            Assert.Equal("token-2", await tokens.GetTokenAsync());
        }

        [Fact]
        public async Task A_second_401_is_not_retried_again()
        {
            var api = new FakeTcgplayerApi
            {
                Override = _ => new HttpResponseMessage(HttpStatusCode.Unauthorized),
            };
            var client = api.CreateClient();

            var ex = await Assert.ThrowsAsync<TcgplayerApiException>(() => client.GetLanguagesAsync(3));

            Assert.Equal(401, ex.StatusCode);
            Assert.Equal(2, api.ApiRequests.Count());
            Assert.Equal(2, api.IssuedTokens.Count);
        }

        [Fact]
        public async Task Refresh_after_another_caller_already_refreshed_reuses_the_new_token()
        {
            var api = new FakeTcgplayerApi();
            var tokens = api.CreateTokenProvider(new ManualTimeProvider(Start));
            var first = await tokens.GetTokenAsync();

            var second = await tokens.RefreshTokenAsync(first);
            var third = await tokens.RefreshTokenAsync(first); // stale 401 from a request that raced the refresh

            Assert.Equal("token-2", second);
            Assert.Equal("token-2", third);
            Assert.Equal(2, api.TokenRequests.Count);
        }

        [Fact]
        public void Options_defaults_and_uris()
        {
            var options = new TcgplayerOptions();
            Assert.Equal("Tcgplayer", TcgplayerOptions.SectionName);
            Assert.Equal("https://api.tcgplayer.com", options.BaseUrl);
            Assert.Equal("v1.39.0", options.ApiVersion);
            Assert.Equal(3, options.CategoryId);
            Assert.Equal("https://api.tcgplayer.com/token", options.TokenUri.ToString());
            Assert.Equal("https://api.tcgplayer.com/v1.39.0/", options.VersionedBaseUri.ToString());

            options.PublicKey = "a";
            Assert.False(options.IsConfigured);
            options.PrivateKey = " ";
            Assert.False(options.IsConfigured);
            options.PrivateKey = "b";
            Assert.True(options.IsConfigured);
        }
    }
}
