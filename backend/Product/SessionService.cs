using System.Security.Cryptography;

namespace PokemonTCG.API.Product;

/// <summary>
/// Browser sessions. Every visitor gets an anonymous profile behind an HttpOnly cookie, so the
/// watchlist and master sets work before sign-in; signing in with an email code attaches the
/// browser to an account (see <see cref="SignInService"/>).
/// </summary>
public sealed class SessionService(AccountStore accounts, TimeProvider clock)
{
    private const string CookieName = "tcg_signal_session";
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(180);

    /// <summary>Active sessions are extended once less than this much time is left.</summary>
    private static readonly TimeSpan RenewWithin = TimeSpan.FromDays(90);

    /// <summary>The browser's profile, or null when it has no valid session. Never creates one.</summary>
    public async Task<AppUser?> FindAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var hash = CurrentTokenHash(context);
        if (hash is null) return null;

        var now = clock.GetUtcNow();
        var session = await accounts.FindSessionAsync(hash, now, cancellationToken);
        if (session is null) return null;

        DateTimeOffset? renewed = null;
        if (session.ExpiresAt - now < RenewWithin)
        {
            renewed = now + Lifetime;
            WriteCookie(context, context.Request.Cookies[CookieName]!, renewed.Value);
        }
        await accounts.TouchSessionAsync(hash, session.User.Id, now, renewed, cancellationToken);
        return session.User;
    }

    /// <summary>The browser's profile, creating an anonymous one on its first visit.</summary>
    public async Task<AppUser> GetOrCreateAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var existing = await FindAsync(context, cancellationToken);
        if (existing is not null) return existing;

        var token = NewToken();
        var now = clock.GetUtcNow();
        var user = await accounts.CreateAnonymousUserAsync(Hash(token), now, now + Lifetime, cancellationToken);
        WriteCookie(context, token, now + Lifetime);
        return user;
    }

    public async Task SignOutAsync(HttpContext context, CancellationToken cancellationToken)
    {
        if (CurrentTokenHash(context) is { } hash) await accounts.DeleteSessionAsync(hash, cancellationToken);
        context.Response.Cookies.Delete(CookieName, BuildCookieOptions(context, null));
    }

    /// <summary>Hash of the session token the request carried, if it carried a plausible one.</summary>
    public static string? CurrentTokenHash(HttpContext context) =>
        context.Request.Cookies.TryGetValue(CookieName, out var token) && token.Length >= 32 ? Hash(token) : null;

    public static string NewToken() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

    /// <summary>Only this hash is stored, so a database leak does not expose usable cookies.</summary>
    public static string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));

    public static void WriteCookie(HttpContext context, string token, DateTimeOffset expires) =>
        context.Response.Cookies.Append(CookieName, token, BuildCookieOptions(context, expires));

    private static CookieOptions BuildCookieOptions(HttpContext context, DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = context.Request.IsHttps,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        Expires = expires,
        IsEssential = true,
    };
}
