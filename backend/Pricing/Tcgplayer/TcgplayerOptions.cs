namespace PokemonTCG.API.Pricing.Tcgplayer;

/// <summary>Settings for TCGplayer's Developer API (bound from the "Tcgplayer" section).</summary>
public class TcgplayerOptions
{
    public const string SectionName = "Tcgplayer";

    /// <summary>The API's client id. Sent as <c>client_id</c> when requesting a token.</summary>
    public string? PublicKey { get; set; }

    /// <summary>The API's client secret. Sent as <c>client_secret</c>; stays on the server.</summary>
    public string? PrivateKey { get; set; }

    public string BaseUrl { get; set; } = "https://api.tcgplayer.com";

    public string ApiVersion { get; set; } = "v1.39.0";

    /// <summary>TCGplayer's category for the Pokémon TCG.</summary>
    public int CategoryId { get; set; } = 3;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(PublicKey) && !string.IsNullOrWhiteSpace(PrivateKey);

    /// <summary>{BaseUrl}/token.</summary>
    public Uri TokenUri => new($"{BaseUrl.TrimEnd('/')}/token");

    /// <summary>{BaseUrl}/{ApiVersion}/ — the BaseAddress for <see cref="TcgplayerClient"/>.</summary>
    public Uri VersionedBaseUri => new($"{BaseUrl.TrimEnd('/')}/{ApiVersion.Trim('/')}/");
}
