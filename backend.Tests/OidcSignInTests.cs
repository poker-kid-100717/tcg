using System.Net;
using Microsoft.AspNetCore.WebUtilities;

namespace PokemonTcgMarketplace.Backend.Tests;

[Collection(OidcCollection.Name)]
public class OidcSignInTests(OidcFixture fixture)
{
    [Fact]
    public async Task Sign_in_redirects_to_the_provider_with_the_authorization_code_flow_and_pkce()
    {
        using var client = fixture.Factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await client.GetAsync("/api/auth/login?returnUrl=https://evil.example/");

        Assert.True(response.StatusCode == HttpStatusCode.Redirect, await response.Content.ReadAsStringAsync());
        var location = response.Headers.Location!;
        Assert.StartsWith(OidcFixture.AuthorizeEndpoint, location.ToString());
        var query = QueryHelpers.ParseQuery(location.Query);
        Assert.Equal("code", query["response_type"].ToString());
        Assert.Equal("S256", query["code_challenge_method"].ToString());
        Assert.False(string.IsNullOrEmpty(query["code_challenge"]));
        Assert.Equal("tcg-signal-test", query["client_id"].ToString());
        Assert.EndsWith("/api/auth/callback", query["redirect_uri"].ToString());
        Assert.Contains("openid", query["scope"].ToString());
        // No secret or token ever travels in the browser's URL.
        Assert.DoesNotContain("fixture-client-secret", location.ToString());
    }

    [Fact]
    public async Task Sign_in_is_advertised_once_a_provider_is_configured()
    {
        using var client = fixture.CreateClient();
        var options = await client.GetStringAsync("/api/auth/options");
        Assert.Contains("\"signInAvailable\":true", options);
    }
}
