using PokemonTCG.API.Email;

namespace PokemonTCG.API.Product;

public record RequestCodeRequest(string? Email);

public record VerifyCodeRequest(string? Email, string? Code);

public record UpdatePreferencesRequest(bool EmailAlerts);

public static class AccountEndpoints
{
    /// <summary>Per-client-IP rate limiter policies (configured in Program.cs).</summary>
    public const string CodeRateLimit = "sign-in-code";
    public const string VerifyRateLimit = "sign-in-verify";

    public static void MapAccounts(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapPost("/auth/code", async Task<IResult> (
            RequestCodeRequest request,
            IEmailSender email,
            SignInService signIn,
            CancellationToken ct) =>
        {
            if (!email.IsConfigured)
                return Results.Problem(statusCode: 503, title: "Email sign-in isn't available yet.");

            var address = SignInService.NormalizeEmail(request.Email);
            if (address is null)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["email"] = ["Enter a valid email address."] });

            try
            {
                return await signIn.RequestCodeAsync(address, ct) == CodeRequestResult.Sent
                    ? Results.Accepted()
                    : Results.Problem(statusCode: 429, title: "Too many codes requested", detail: "Wait a little while before asking for another code.");
            }
            catch (EmailDeliveryException)
            {
                return Results.Problem(statusCode: 502, title: "We couldn't send the email", detail: "Try again in a minute.");
            }
        }).RequireRateLimiting(CodeRateLimit);

        api.MapPost("/auth/verify", async Task<IResult> (
            VerifyCodeRequest request,
            HttpContext context,
            SessionService sessions,
            SignInService signIn,
            EntitlementService entitlements,
            CancellationToken ct) =>
        {
            var address = SignInService.NormalizeEmail(request.Email);
            var code = request.Code?.Trim() ?? "";
            if (address is null || code.Length != 6 || !code.All(char.IsAsciiDigit))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["code"] = ["Enter the 6-digit code from the email."] });

            var user = await signIn.VerifyAsync(context, sessions, address, code, ct);
            return user is null
                ? Results.Problem(statusCode: 400, title: "That code didn't work", detail: "Check the most recent email, or request a new code.")
                : Results.Ok(await entitlements.GetAccountAsync(user, ct));
        }).RequireRateLimiting(VerifyRateLimit);

        api.MapPost("/auth/sign-out", async (HttpContext context, SessionService sessions, CancellationToken ct) =>
        {
            await sessions.SignOutAsync(context, ct);
            return Results.NoContent();
        });

        api.MapPut("/account/preferences", async Task<IResult> (
            UpdatePreferencesRequest request,
            HttpContext context,
            SessionService sessions,
            AccountStore accounts,
            CancellationToken ct) =>
        {
            var user = await sessions.GetOrCreateAsync(context, ct);
            if (!user.SignedIn) return SignInRequired("Sign in to choose how alerts reach you.");
            await accounts.SetEmailAlertsAsync(user.Id, request.EmailAlerts, ct);
            return Results.NoContent();
        });

        // One-click unsubscribe (RFC 8058): mail providers POST to the List-Unsubscribe URL, and the
        // site's unsubscribe page posts the same token. There is deliberately no GET, so link
        // scanners that prefetch URLs in emails can't unsubscribe anyone.
        api.MapPost("/email/unsubscribe", async Task<IResult> (Guid? token, AccountStore accounts, CancellationToken ct) =>
            token is { } value && await accounts.UnsubscribeAsync(value, ct)
                ? Results.NoContent()
                : Results.NotFound());
    }

    public static IResult SignInRequired(string detail) =>
        Results.Problem(statusCode: 401, title: "Sign in required", detail: detail);
}
