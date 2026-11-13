using System.Globalization;
using System.Net.Mail;
using System.Security.Cryptography;
using PokemonTCG.API.Email;

namespace PokemonTCG.API.Product;

public enum CodeRequestResult
{
    Sent,
    TooManyRequests,
}

/// <summary>
/// Passwordless sign-in: a six-digit code emailed to the address. Signing up and signing in are
/// the same flow, so the API never reveals whether an address already has an account.
/// </summary>
public sealed class SignInService(
    AccountStore accounts,
    IEmailSender email,
    TimeProvider clock,
    ILogger<SignInService> logger)
{
    public const int CodeLifetimeMinutes = 10;
    public const int MaxCodesPerEmailPerHour = 5;

    /// <summary>Lower-cased, trimmed address, or null when it isn't a single plain email address.</summary>
    public static string? NormalizeEmail(string? input)
    {
        var candidate = input?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(candidate) || candidate.Length > 254) return null;
        return MailAddress.TryCreate(candidate, out var parsed) &&
               parsed.Address == candidate &&
               candidate.IndexOf('@') > 0 &&
               candidate[(candidate.IndexOf('@') + 1)..].Contains('.')
            ? candidate
            : null;
    }

    /// <exception cref="EmailDeliveryException">The code was stored but could not be emailed.</exception>
    public async Task<CodeRequestResult> RequestCodeAsync(string normalizedEmail, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        if (await accounts.CountCodesSinceAsync(normalizedEmail, now.AddHours(-1), cancellationToken) >= MaxCodesPerEmailPerHour)
            return CodeRequestResult.TooManyRequests;

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
        await accounts.CreateCodeAsync(normalizedEmail, code, now, now.AddMinutes(CodeLifetimeMinutes), cancellationToken);
        await email.SendAsync(EmailTemplates.SignInCode(normalizedEmail, code, CodeLifetimeMinutes), cancellationToken);
        return CodeRequestResult.Sent;
    }

    /// <returns>The signed-in account, or null when the code is wrong, used, expired or locked.</returns>
    public async Task<AppUser?> VerifyAsync(
        HttpContext context,
        SessionService sessions,
        string normalizedEmail,
        string code,
        CancellationToken cancellationToken)
    {
        var current = await sessions.FindAsync(context, cancellationToken);
        var token = SessionService.NewToken();
        var now = clock.GetUtcNow();
        var expires = now + SessionService.Lifetime;

        var user = await accounts.CompleteSignInAsync(
            normalizedEmail,
            code,
            current,
            SessionService.CurrentTokenHash(context),
            SessionService.Hash(token),
            now,
            expires,
            cancellationToken);
        if (user is null) return null;

        SessionService.WriteCookie(context, token, expires);
        logger.LogInformation("User {UserId} signed in", user.Id);
        return user;
    }
}
