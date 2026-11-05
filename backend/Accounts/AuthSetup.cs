using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace PokemonTCG.API.Accounts;

public static class TcgClaims
{
    /// <summary>The app's own user id, added at sign-in. Everything account-scoped keys off this, never a client-supplied id.</summary>
    public const string UserIdType = "tcg_uid";

    public static Guid? UserId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(UserIdType), out var id) ? id : null;

    /// <summary>The signed-in user's id; only call behind RequireAuthorization.</summary>
    public static Guid RequireUserId(this ClaimsPrincipal principal) =>
        principal.UserId() ?? throw new InvalidOperationException("No signed-in user.");
}

public static class AuthPolicies
{
    public const string RequirePro = "RequirePro";
    public const string AuthRateLimit = "auth";
    public const string BillingRateLimit = "billing";
    public const string ToolRateLimit = "tools";
}

public static class AuthSetup
{
    public const string CookieScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    public const string OidcScheme = OpenIdConnectDefaults.AuthenticationScheme;
    public const string LegacySessionCookie = "tcg_signal_session";

    public static void AddTcgSignalAuth(this WebApplicationBuilder builder)
    {
        var options = builder.Configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();
        if (options.LocalLogin && !(builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing")))
        {
            throw new InvalidOperationException("Auth:LocalLogin is only allowed in the Development and Testing environments.");
        }
        builder.Services.AddSingleton(options);

        var auth = builder.Services.AddAuthentication(o =>
            {
                o.DefaultScheme = CookieScheme;
                o.DefaultChallengeScheme = options.OidcConfigured ? OidcScheme : CookieScheme;
            })
            .AddCookie(CookieScheme, cookie =>
            {
                cookie.Cookie.Name = "tcg_auth";
                cookie.Cookie.HttpOnly = true;
                cookie.Cookie.SameSite = SameSiteMode.Lax;
                cookie.Cookie.SecurePolicy = builder.Environment.IsProduction() ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
                cookie.ExpireTimeSpan = TimeSpan.FromDays(30);
                cookie.SlidingExpiration = true;
                // An API: answer 401/403 rather than redirecting to a login page.
                cookie.Events.OnRedirectToLogin = context => { context.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
                cookie.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
                // A deleted account's cookie stops working at once.
                cookie.Events.OnValidatePrincipal = async context =>
                {
                    var id = context.Principal?.UserId();
                    var store = context.HttpContext.RequestServices.GetRequiredService<AccountStore>();
                    if (id is null || await store.GetAsync(id.Value, context.HttpContext.RequestAborted) is null)
                    {
                        context.RejectPrincipal();
                        await context.HttpContext.SignOutAsync(CookieScheme);
                    }
                };
            });

        if (options.OidcConfigured)
        {
            auth.AddOpenIdConnect(OidcScheme, oidc =>
            {
                oidc.Authority = options.Authority;
                oidc.ClientId = options.ClientId;
                oidc.ClientSecret = options.ClientSecret;
                oidc.ResponseType = OpenIdConnectResponseType.Code;
                oidc.UsePkce = true;
                oidc.ResponseMode = OpenIdConnectResponseMode.Query;
                oidc.CallbackPath = "/api/auth/callback";
                oidc.SignedOutCallbackPath = "/api/auth/signed-out";
                oidc.SaveTokens = false;
                oidc.GetClaimsFromUserInfoEndpoint = true;
                oidc.MapInboundClaims = false;
                oidc.Scope.Clear();
                oidc.Scope.Add("openid");
                oidc.Scope.Add("profile");
                oidc.Scope.Add("email");
                oidc.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                oidc.NonceCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                oidc.Events.OnTicketReceived = async context =>
                {
                    var principal = context.Principal!;
                    var issuer = principal.FindFirstValue("iss") ?? options.Authority!;
                    var subject = principal.FindFirstValue("sub") ?? throw new InvalidOperationException("The provider didn't return a subject.");
                    var email = principal.FindFirstValue("email");
                    var name = principal.FindFirstValue("name");
                    context.Principal = await CreateSessionPrincipalAsync(context.HttpContext, issuer, subject, email, name);
                };
                oidc.Events.OnRemoteFailure = context =>
                {
                    context.Response.Redirect("/account?signin=failed");
                    context.HandleResponse();
                    return Task.CompletedTask;
                };
            });
        }

        builder.Services.AddAuthorization(o =>
        {
            o.AddPolicy(AuthPolicies.RequirePro, policy => policy.RequireAuthenticatedUser().AddRequirements(new ProRequirement()));
        });
        builder.Services.AddScoped<IAuthorizationHandler, ProRequirementHandler>();
        builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, ProblemAuthorizationResultHandler>();

        // Per user (or per client address before sign-in). Limits are configuration so tests and load checks can raise them.
        var limits = builder.Configuration.GetSection("RateLimits");
        int authLimit = limits.GetValue("AuthPerFiveMinutes", 20), billingLimit = limits.GetValue("BillingPerMinute", 10),
            toolLimit = limits.GetValue("ToolsPerMinute", 120);
        builder.Services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            static string Key(HttpContext c) => c.User.UserId()?.ToString() ?? c.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            o.AddPolicy(AuthPolicies.AuthRateLimit, c => RateLimitPartition.GetFixedWindowLimiter(Key(c),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = authLimit, Window = TimeSpan.FromMinutes(5) }));
            o.AddPolicy(AuthPolicies.BillingRateLimit, c => RateLimitPartition.GetFixedWindowLimiter(Key(c),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = billingLimit, Window = TimeSpan.FromMinutes(1) }));
            o.AddPolicy(AuthPolicies.ToolRateLimit, c => RateLimitPartition.GetFixedWindowLimiter(Key(c),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = toolLimit, Window = TimeSpan.FromMinutes(1) }));
        });

        builder.Services.AddSingleton(sp => new AccountStore(sp.GetRequiredService<PokemonTCG.API.Product.ProductStore>().ConnectionString));
    }

    /// <summary>Maps a provider identity to the app's account and returns the principal the session cookie carries.</summary>
    public static async Task<ClaimsPrincipal> CreateSessionPrincipalAsync(HttpContext context, string issuer, string subject, string? email, string? name)
    {
        var store = context.RequestServices.GetRequiredService<AccountStore>();
        var clock = context.RequestServices.GetRequiredService<TimeProvider>();
        var legacy = context.Request.Cookies.TryGetValue(LegacySessionCookie, out var token) && token.Length >= 32
            ? Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token))).ToLowerInvariant()
            : null;
        var userId = await store.SignInAsync(issuer, subject, email, name, legacy, clock.GetUtcNow(), context.RequestAborted);
        if (legacy is not null) context.Response.Cookies.Delete(LegacySessionCookie);

        // Only what the app needs: its own user id and a display email. No provider tokens are kept.
        var claims = new List<Claim> { new(TcgClaims.UserIdType, userId.ToString()) };
        if (!string.IsNullOrWhiteSpace(email)) claims.Add(new Claim(ClaimTypes.Email, email));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, CookieScheme));
    }
}
