using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using PokemonTCG.API.Product;

namespace PokemonTCG.API.Accounts;

/// <summary>Met only when the signed-in user's verified subscription state grants Pro.</summary>
public sealed class ProRequirement : IAuthorizationRequirement;

public sealed class ProRequirementHandler(EntitlementService entitlements) : AuthorizationHandler<ProRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, ProRequirement requirement)
    {
        if (context.User.UserId() is { } id && (await entitlements.GetAccountAsync(id, CancellationToken.None)).IsPro)
        {
            context.Succeed(requirement);
        }
    }
}

/// <summary>401 and 403 answers carry a ProblemDetails body the UI can act on (sign in, or upgrade).</summary>
public sealed class ProblemAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult result)
    {
        if (result.Challenged)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { type = "sign_in_required", title = "Sign in required", status = 401 },
                options: null, contentType: "application/problem+json");
            return;
        }
        if (result.Forbidden)
        {
            var pro = result.AuthorizationFailure?.FailedRequirements.OfType<ProRequirement>().Any() == true;
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(
                pro
                    ? new { type = "pro_required", title = "TCG Signal Pro required", status = 403 }
                    : new { type = "forbidden", title = "You don't have access to this.", status = 403 },
                options: null, contentType: "application/problem+json");
            return;
        }
        await _default.HandleAsync(next, context, policy, result);
    }
}

/// <summary>
/// CSRF protection for cookie sessions: every state-changing /api request must be JSON or carry X-Requested-With (a
/// cross-site form can't send either without a CORS preflight, which this API never grants), and when the browser sends
/// an Origin it must be this site. The Stripe webhook is exempt: it authenticates with its signature instead.
/// </summary>
public sealed class SameSiteRequestMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        if (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method) || HttpMethods.IsOptions(request.Method)
            || !request.Path.StartsWithSegments("/api") || request.Path.StartsWithSegments("/api/billing/webhook"))
        {
            return next(context);
        }
        if (request.Headers.Origin is [{ } origin] && !string.Equals(origin, $"{request.Scheme}://{request.Host}", StringComparison.OrdinalIgnoreCase))
        {
            return Reject(context, "Requests must come from this site.");
        }
        var isJson = request.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) == true;
        if (!isJson && !request.Headers.ContainsKey("X-Requested-With"))
        {
            return Reject(context, "Send requests as JSON.");
        }
        return next(context);
    }

    private static Task Reject(HttpContext context, string title)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return context.Response.WriteAsJsonAsync(new { type = "csrf", title, status = 403 }, options: null, contentType: "application/problem+json");
    }
}
