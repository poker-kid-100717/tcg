using Microsoft.AspNetCore.Authentication;
using PokemonTCG.API.Product;

namespace PokemonTCG.API.Accounts;

public record AuthOptionsView(bool SignInAvailable, bool ProviderSignIn, bool LocalLogin, string ProviderName);
public record LocalLoginRequest(string? Email);
public record MeView(bool SignedIn, string? Email, string? DisplayName, AccountView? Account);

public static class AuthEndpoints
{
    public static void MapAuth(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/auth");

        auth.MapGet("/options", (AuthOptions options) =>
            new AuthOptionsView(options.OidcConfigured || options.LocalLogin, options.OidcConfigured, options.LocalLogin, options.ProviderName));

        // Starts the provider's sign-in. The return path must be local, so this can't be used as an open redirect.
        auth.MapGet("/login", (string? returnUrl, AuthOptions options) =>
            options.OidcConfigured
                ? Results.Challenge(new AuthenticationProperties { RedirectUri = SafeReturnUrl(returnUrl) }, [AuthSetup.OidcScheme])
                : Results.Problem(statusCode: 503, title: "Sign-in isn't configured yet."))
            .RequireRateLimiting(AuthPolicies.AuthRateLimit);

        // Development and automated tests only (refused at startup elsewhere): sign in as an email address.
        auth.MapPost("/local-login", async (LocalLoginRequest body, HttpContext context, AuthOptions options) =>
        {
            if (!options.LocalLogin) return Results.NotFound();
            var email = body.Email?.Trim();
            if (string.IsNullOrWhiteSpace(email) || email.Length > 320 || !email.Contains('@'))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["email"] = ["Enter an email address."] });
            var principal = await AuthSetup.CreateSessionPrincipalAsync(context, "local-dev", email.ToLowerInvariant(), email, null);
            await context.SignInAsync(AuthSetup.CookieScheme, principal, new AuthenticationProperties { IsPersistent = true });
            return Results.NoContent();
        }).RequireRateLimiting(AuthPolicies.AuthRateLimit);

        auth.MapPost("/logout", async (HttpContext context) =>
        {
            await context.SignOutAsync(AuthSetup.CookieScheme);
            return Results.NoContent();
        });

        app.MapGet("/api/me", async (HttpContext context, AccountStore accounts, EntitlementService entitlements, CancellationToken ct) =>
        {
            if (context.User.UserId() is not { } id || await accounts.GetAsync(id, ct) is not { } account)
                return new MeView(false, null, null, null);
            return new MeView(true, account.Email, account.DisplayName, await entitlements.GetAccountAsync(id, ct));
        });

        // Deletes the account and everything tied to it. An active Stripe subscription is cancelled first so billing
        // stops; Stripe keeps its own invoice records.
        app.MapDelete("/api/account", async (HttpContext context, AccountStore accounts, ProductStore store, StripeBillingService stripe, ILoggerFactory logs, CancellationToken ct) =>
        {
            var id = context.User.RequireUserId();
            var subscription = await store.GetSubscriptionAsync(id, ct);
            if (subscription?.SubscriptionId is { Length: > 0 } subscriptionId && subscription.Status is not ("canceled" or "incomplete_expired"))
            {
                try
                {
                    await stripe.CancelNowAsync(subscriptionId, ct);
                }
                catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
                {
                    logs.CreateLogger("Accounts").LogError(ex, "Couldn't cancel the subscription for account deletion");
                    return Results.Problem(statusCode: 502, title: "Your subscription couldn't be cancelled, so the account wasn't deleted. Try again, or cancel it from Manage subscription first.");
                }
            }
            await accounts.DeleteAsync(id, ct);
            await context.SignOutAsync(AuthSetup.CookieScheme);
            return Results.NoContent();
        }).RequireAuthorization();
    }

    /// <summary>Only a local path ("/dashboard"); anything else (other hosts, "//evil", "/\\evil") falls back to "/".</summary>
    public static string SafeReturnUrl(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && returnUrl.StartsWith('/') && !returnUrl.StartsWith("//") && !returnUrl.StartsWith("/\\")
        && returnUrl.Length <= 300 && Uri.IsWellFormedUriString(returnUrl, UriKind.Relative)
            ? returnUrl
            : "/";
}
