using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace PokemonTCG.API.Pricing.Tcgplayer;

/// <summary>
/// Gets and caches TCGplayer bearer tokens (OAuth client credentials against
/// POST {BaseUrl}/token). A token is reused until five minutes before it
/// expires. Thread-safe: concurrent callers share one in-flight request.
/// Register as a singleton so the cache is shared.
/// </summary>
public class TcgplayerTokenProvider
{
    /// <summary>Name of the plain (unauthenticated) HttpClient used for the token request.</summary>
    public const string HttpClientName = "Tcgplayer.Token";

    /// <summary>How long before expiry a cached token is considered stale.</summary>
    public static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(5);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TcgplayerOptions _options;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private CachedToken? _cached;

    public TcgplayerTokenProvider(IHttpClientFactory httpClientFactory, IOptions<TcgplayerOptions> options, TimeProvider time)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _time = time;
    }

    /// <summary>A valid access token, from the cache when it is still fresh.</summary>
    public async Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        if (Fresh(_cached) is { } token) return token;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (Fresh(_cached) is { } again) return again;
            return await RequestTokenAsync(cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Replaces <paramref name="rejectedToken"/> (one the API answered 401 to).
    /// If another caller already replaced it, returns that newer token instead
    /// of requesting yet another.
    /// </summary>
    public async Task<string> RefreshTokenAsync(string rejectedToken, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (Fresh(_cached) is { } current && current != rejectedToken) return current;
            _cached = null;
            return await RequestTokenAsync(cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    private string? Fresh(CachedToken? cached) =>
        cached is not null && _time.GetUtcNow() < cached.RefreshAt ? cached.AccessToken : null;

    private async Task<string> RequestTokenAsync(CancellationToken cancellationToken)
    {
        if (!_options.IsConfigured)
        {
            throw new InvalidOperationException(
                $"TCGplayer API keys are not configured. Set {TcgplayerOptions.SectionName}:PublicKey and {TcgplayerOptions.SectionName}:PrivateKey.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.TokenUri)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = _options.PublicKey!,
                ["client_secret"] = _options.PrivateKey!,
            }),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var http = _httpClientFactory.CreateClient(HttpClientName);
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            // The body can echo request details; keep only the status.
            throw new TcgplayerApiException(
                $"TCGplayer token request failed with HTTP {(int)response.StatusCode}.", [], (int)response.StatusCode);
        }

        var token = await response.Content.ReadFromJsonAsync<TcgplayerToken>(Json, cancellationToken);
        if (token is null || string.IsNullOrEmpty(token.AccessToken))
        {
            throw new TcgplayerApiException("TCGplayer token response had no access_token.", []);
        }

        var now = _time.GetUtcNow();
        var lifetime = TimeSpan.FromSeconds(Math.Max(0, token.ExpiresIn));
        _cached = new CachedToken(token.AccessToken, now + lifetime - RefreshMargin);
        return token.AccessToken;
    }

    private sealed record CachedToken(string AccessToken, DateTimeOffset RefreshAt);
}
