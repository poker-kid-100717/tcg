using System.Security.Cryptography;

namespace PokemonTCG.API.Product;

public sealed class SessionService(ProductStore store, TimeProvider clock)
{
    private const string CookieName = "tcg_signal_session";

    public async Task<ProductStore.UserRow> GetOrCreateAsync(HttpContext context, CancellationToken cancellationToken)
    {
        if (context.Request.Cookies.TryGetValue(CookieName, out var token) && token.Length >= 32)
        {
            var existing = await store.FindUserByTokenHashAsync(Hash(token), cancellationToken);
            if (existing is not null)
            {
                await store.TouchUserAsync(existing.Id, clock.GetUtcNow(), cancellationToken);
                return existing;
            }
        }

        var raw = RandomNumberGenerator.GetBytes(32);
        var newToken = Convert.ToHexString(raw).ToLowerInvariant();
        var user = await store.CreateUserAsync(Guid.NewGuid(), Hash(newToken), clock.GetUtcNow(), cancellationToken);

        context.Response.Cookies.Append(CookieName, newToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = context.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            Expires = clock.GetUtcNow().AddDays(180),
            IsEssential = true,
        });

        return user;
    }

    private static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}
