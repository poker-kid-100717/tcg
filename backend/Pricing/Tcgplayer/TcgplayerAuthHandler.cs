using System.Net;
using System.Net.Http.Headers;

namespace PokemonTCG.API.Pricing.Tcgplayer;

/// <summary>
/// Adds "Authorization: bearer &lt;token&gt;" to every TCGplayer API request.
/// On a 401 it gets a fresh token once and retries; a second 401 is returned
/// to the caller as-is.
/// </summary>
public class TcgplayerAuthHandler(TcgplayerTokenProvider tokens) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await tokens.GetTokenAsync(cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("bearer", token);

        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized) return response;

        response.Dispose();
        var fresh = await tokens.RefreshTokenAsync(token, cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("bearer", fresh);
        return await base.SendAsync(request, cancellationToken);
    }
}
