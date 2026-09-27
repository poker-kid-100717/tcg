namespace PokemonTCG.API.Accounts;

/// <summary>
/// Sign-in configuration. Accounts come from a standards-based OpenID Connect provider (Auth0, Microsoft Entra External
/// ID, Okta, Keycloak, Google…): the API runs the authorization-code flow with PKCE itself and keeps its own HttpOnly
/// session cookie, so the browser never holds provider tokens and this application stores no passwords.
/// </summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>The provider's issuer URL (its discovery document lives under it).</summary>
    public string? Authority { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    /// <summary>Shown on the sign-in button ("Continue with …").</summary>
    public string ProviderName { get; set; } = "your account provider";

    /// <summary>
    /// Email-only sign-in for local development and automated tests. Refused at startup outside the Development and
    /// Testing environments, so it can never be switched on in production.
    /// </summary>
    public bool LocalLogin { get; set; }

    public bool OidcConfigured =>
        !string.IsNullOrWhiteSpace(Authority) && !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}
