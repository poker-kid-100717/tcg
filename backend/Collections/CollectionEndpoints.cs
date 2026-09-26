using System.Security.Claims;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Identity;

namespace PokemonTCG.API.Collections;

/// <summary>The signed-in collector's cards and wishlist. Every route needs a session (a guest one is fine).</summary>
public static class CollectionEndpoints
{
    public static void MapCollection(this IEndpointRouteBuilder app)
    {
        var collection = app.MapGroup("/api/collection").RequireAuthorization();

        collection.MapGet("", (ClaimsPrincipal user, CollectionService service, CancellationToken ct) =>
            service.GetAsync(UserId(user), ct));

        collection.MapPost("/items", async (AddItemRequest body, ClaimsPrincipal user, CollectionService service, CancellationToken ct) =>
            Results.Ok(await service.AddAsync(UserId(user), body, ct)));

        collection.MapPatch("/items/{id:guid}", async (Guid id, UpdateItemRequest body, ClaimsPrincipal user, CollectionService service, CancellationToken ct) =>
            Results.Ok(await service.UpdateAsync(UserId(user), id, body, ct)));

        collection.MapDelete("/items/{id:guid}", async (Guid id, ClaimsPrincipal user, CollectionService service, CancellationToken ct) =>
            await service.RemoveAsync(UserId(user), id, ct) ? Results.NoContent() : Results.NotFound());

        collection.MapGet("/cards/{cardId}", (string cardId, ClaimsPrincipal user, CollectionService service, CancellationToken ct) =>
            service.GetCardAsync(UserId(user), cardId, ct));

        collection.MapGet("/sets/{setId}", async (string setId, ClaimsPrincipal user, CollectionService service, CancellationToken ct) =>
            await service.GetSetAsync(UserId(user), setId, ct) is { } checklist ? Results.Ok(checklist) : Results.NotFound());

        collection.MapPost("/sample", async (ClaimsPrincipal user, CollectionService service, CancellationToken ct) =>
            Results.Ok(new { Added = await service.AddSampleAsync(UserId(user), ct) }));

        collection.MapGet("/export.csv", async (ClaimsPrincipal user, CollectionService service, TimeProvider clock, CancellationToken ct) =>
            Results.File(System.Text.Encoding.UTF8.GetBytes(await service.ExportCsvAsync(UserId(user), ct)), "text/csv; charset=utf-8",
                $"collection-{DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime):yyyy-MM-dd}.csv"));

        var wishlist = app.MapGroup("/api/wishlist").RequireAuthorization();

        wishlist.MapGet("", (ClaimsPrincipal user, CollectionService service, CancellationToken ct) =>
            service.GetWishlistAsync(UserId(user), ct));

        wishlist.MapPost("", async (WishRequest body, ClaimsPrincipal user, CollectionService service, CancellationToken ct) =>
            Results.Ok(await service.WishAsync(UserId(user), body, ct)));

        wishlist.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal user, CollectionService service, CancellationToken ct) =>
            await service.UnwishAsync(UserId(user), id, ct) ? Results.NoContent() : Results.NotFound());
    }

    private static Guid UserId(ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new CollectionException(401, "Not signed in."));
}

/// <summary>Turns a <see cref="CollectionException"/> into a ProblemDetails response with its status.</summary>
public class CollectionExceptionHandler(IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not CollectionException collection) return false;
        context.Response.StatusCode = collection.Status;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = { Status = collection.Status, Title = collection.Message },
            Exception = exception,
        });
    }
}

/// <summary>
/// Cookie sessions need CSRF protection. Every state-changing /api call must be JSON (a cross-site form can't send
/// that without a CORS preflight, which this API never grants) and, when the browser says where it came from, come
/// from this site.
/// </summary>
public class SameSiteRequestMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        if (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method) || HttpMethods.IsOptions(request.Method)
            || !request.Path.StartsWithSegments("/api"))
        {
            return next(context);
        }

        if (request.Headers.Origin is [{ } origin] && !string.Equals(origin, $"{request.Scheme}://{request.Host}", StringComparison.OrdinalIgnoreCase))
        {
            return Reject(context, "Requests must come from this site.");
        }
        // Bodyless POST/DELETE still need a JSON content type or the custom header, so a plain form or <img> can't fire them.
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
        return context.Response.WriteAsJsonAsync(new { title, status = 403 });
    }
}
