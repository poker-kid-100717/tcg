using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PokemonTCG.API.Data;

namespace PokemonTCG.API.Collections;

public record AccountView(bool SignedIn, bool IsGuest, string? Email);
public record Credentials([property: Required, EmailAddress, MaxLength(256)] string Email, [property: Required, MinLength(10), MaxLength(200)] string Password);

/// <summary>
/// Guest-first accounts. Anyone can start a collection immediately: the first add creates a guest account kept by a
/// long-lived cookie. Adding an email and password turns that same account into a regular one, and signing in to an
/// existing account from a guest session moves the guest's cards across.
/// </summary>
public static class AccountEndpoints
{
    public const string AuthRateLimit = "auth";

    public static void MapAccount(this IEndpointRouteBuilder app)
    {
        var account = app.MapGroup("/api/account");

        account.MapGet("", async (ClaimsPrincipal principal, UserManager<AppUser> users) =>
            await users.GetUserAsync(principal) is { } user
                ? new AccountView(true, user.IsGuest, user.Email)
                : new AccountView(false, false, null));

        account.MapPost("/guest", async (ClaimsPrincipal principal, UserManager<AppUser> users, SignInManager<AppUser> signIn, TimeProvider clock) =>
        {
            if (await users.GetUserAsync(principal) is { } existing) return Results.Ok(new AccountView(true, existing.IsGuest, existing.Email));
            var guest = new AppUser { Id = Guid.CreateVersion7(), UserName = $"guest-{Guid.NewGuid():N}", IsGuest = true, CreatedAt = clock.GetUtcNow() };
            var created = await users.CreateAsync(guest);
            if (!created.Succeeded) return Problem(created);
            await signIn.SignInAsync(guest, isPersistent: true);
            return Results.Ok(new AccountView(true, true, null));
        }).RequireRateLimiting(AuthRateLimit);

        account.MapPost("/register", async (Credentials body, ClaimsPrincipal principal, UserManager<AppUser> users, SignInManager<AppUser> signIn, TimeProvider clock) =>
        {
            if (Validate(body) is { } invalid) return invalid;
            var email = body.Email.Trim();
            if (await users.FindByEmailAsync(email) is not null)
            {
                return Results.Problem(statusCode: 409, title: "An account with that email already exists. Sign in instead.");
            }

            var current = await users.GetUserAsync(principal);
            if (current is { IsGuest: false }) return Results.Problem(statusCode: 400, title: "You're already signed in.");

            IdentityResult result;
            AppUser user;
            if (current is { IsGuest: true })
            {
                // Keep the guest's account (and collection); it just gains an email and password.
                user = current;
                user.UserName = email;
                user.Email = email;
                user.IsGuest = false;
                result = await users.UpdateAsync(user);
                if (result.Succeeded) result = await users.AddPasswordAsync(user, body.Password);
            }
            else
            {
                user = new AppUser { Id = Guid.CreateVersion7(), UserName = email, Email = email, CreatedAt = clock.GetUtcNow() };
                result = await users.CreateAsync(user, body.Password);
            }
            if (!result.Succeeded) return Problem(result);
            await signIn.SignInAsync(user, isPersistent: true);
            return Results.Ok(new AccountView(true, false, user.Email));
        }).RequireRateLimiting(AuthRateLimit);

        account.MapPost("/login", async (Credentials body, ClaimsPrincipal principal, UserManager<AppUser> users, SignInManager<AppUser> signIn, AppDbContext db) =>
        {
            var user = await users.FindByEmailAsync(body.Email?.Trim() ?? "");
            // One message for a wrong password and an unknown email, so the form doesn't reveal who has an account.
            var check = user is null ? SignInResult.Failed : await signIn.CheckPasswordSignInAsync(user, body.Password ?? "", lockoutOnFailure: true);
            if (check.IsLockedOut) return Results.Problem(statusCode: 429, title: "Too many attempts. Try again in 15 minutes.");
            if (!check.Succeeded || user is null) return Results.Problem(statusCode: 401, title: "That email and password don't match an account.");

            if (await users.GetUserAsync(principal) is { IsGuest: true } guest && guest.Id != user.Id)
            {
                await MergeGuestAsync(db, guest.Id, user.Id);
                await users.DeleteAsync(guest);
            }
            await signIn.SignInAsync(user, isPersistent: true);
            return Results.Ok(new AccountView(true, false, user.Email));
        }).RequireRateLimiting(AuthRateLimit);

        account.MapPost("/logout", async (SignInManager<AppUser> signIn) =>
        {
            await signIn.SignOutAsync();
            return Results.Ok(new AccountView(false, false, null));
        });

        // Deletes the account and its collection and wishlist.
        account.MapDelete("", async (ClaimsPrincipal principal, UserManager<AppUser> users, SignInManager<AppUser> signIn) =>
        {
            if (await users.GetUserAsync(principal) is not { } user) return Results.Unauthorized();
            await users.DeleteAsync(user);
            await signIn.SignOutAsync();
            return Results.NoContent();
        });
    }

    /// <summary>Moves a guest's cards and wishlist into an existing account, adding quantities where both have a printing.</summary>
    public static async Task MergeGuestAsync(AppDbContext db, Guid guestId, Guid userId)
    {
        var guestItems = await db.CollectionItems.Where(i => i.UserId == guestId).ToListAsync();
        var existing = await db.CollectionItems.Where(i => i.UserId == userId).ToListAsync();
        foreach (var item in guestItems)
        {
            var match = existing.FirstOrDefault(e => e.CardId == item.CardId && e.Variant == item.Variant && e.Condition == item.Condition);
            if (match is null)
            {
                item.UserId = userId;
                continue;
            }
            match.CostEach = CollectionService.CombineCost(match.Quantity, match.CostEach, item.Quantity, item.CostEach);
            match.Quantity += item.Quantity;
            db.CollectionItems.Remove(item);
        }

        var wanted = await db.WishlistItems.Where(w => w.UserId == userId).Select(w => w.CardId).ToListAsync();
        foreach (var wish in await db.WishlistItems.Where(w => w.UserId == guestId).ToListAsync())
        {
            if (wanted.Contains(wish.CardId)) db.WishlistItems.Remove(wish);
            else wish.UserId = userId;
        }
        await db.SaveChangesAsync();
    }

    private static IResult? Validate(Credentials body)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(body.Email) || !new EmailAddressAttribute().IsValid(body.Email.Trim()))
            errors["email"] = ["Enter a valid email address."];
        if (string.IsNullOrEmpty(body.Password) || body.Password.Length < 10)
            errors["password"] = ["Use at least 10 characters."];
        return errors.Count > 0 ? Results.ValidationProblem(errors) : null;
    }

    private static IResult Problem(IdentityResult result) =>
        Results.ValidationProblem(result.Errors.GroupBy(e => e.Code.Contains("Password") ? "password" : "email")
            .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray()));
}
