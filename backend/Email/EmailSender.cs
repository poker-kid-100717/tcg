using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace PokemonTCG.API.Email;

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>Resend API key (starts with <c>re_</c>).</summary>
    public string ResendApiKey { get; set; } = "";

    /// <summary>Sender, such as <c>TCG Signal &lt;alerts@example.com&gt;</c>. Its domain must be verified in Resend.</summary>
    public string From { get; set; } = "";

    /// <summary>Optional address that replies go to.</summary>
    public string ReplyTo { get; set; } = "";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ResendApiKey) && !string.IsNullOrWhiteSpace(From);
}

public sealed record EmailMessage(
    string To,
    string Subject,
    string Html,
    string Text,
    IReadOnlyDictionary<string, string>? Headers = null,
    string? IdempotencyKey = null);

/// <summary>The message was not accepted by the email provider. Safe to retry.</summary>
public sealed class EmailDeliveryException(string message, Exception? inner = null) : Exception(message, inner);

public interface IEmailSender
{
    /// <summary>False when no provider is configured: sign-in and alert emails are then unavailable.</summary>
    bool IsConfigured { get; }

    /// <exception cref="EmailDeliveryException">The provider did not accept the message.</exception>
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

/// <summary>Sends through Resend's HTTP API (https://resend.com/docs/api-reference/emails/send-email).</summary>
public sealed class ResendEmailSender(HttpClient http, EmailOptions options, ILogger<ResendEmailSender> logger) : IEmailSender
{
    public bool IsConfigured => true;

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, object>
        {
            ["from"] = options.From,
            ["to"] = new[] { message.To },
            ["subject"] = message.Subject,
            ["html"] = message.Html,
            ["text"] = message.Text,
        };
        if (!string.IsNullOrWhiteSpace(options.ReplyTo)) payload["reply_to"] = options.ReplyTo;
        if (message.Headers is { Count: > 0 }) payload["headers"] = message.Headers;

        using var request = new HttpRequestMessage(HttpMethod.Post, "emails")
        {
            Content = JsonContent.Create(payload),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ResendApiKey);
        // Lets the resilience handler retry a send without delivering it twice.
        if (!string.IsNullOrWhiteSpace(message.IdempotencyKey))
            request.Headers.Add("Idempotency-Key", message.IdempotencyKey);

        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                // The response body can echo the recipient, so only the status is logged.
                logger.LogWarning("Resend did not accept an email: HTTP {Status}", (int)response.StatusCode);
                throw new EmailDeliveryException($"The email provider returned HTTP {(int)response.StatusCode}.");
            }
        }
        catch (Exception ex) when (ex is not EmailDeliveryException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Resend could not be reached");
            throw new EmailDeliveryException("The email provider could not be reached.", ex);
        }
    }
}

/// <summary>Local development without an email provider: the message, including any sign-in code, goes to the log.</summary>
public sealed class DevelopmentEmailSender(ILogger<DevelopmentEmailSender> logger) : IEmailSender
{
    public bool IsConfigured => true;

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        logger.LogInformation("Development email to {To}: {Subject}\n{Text}", message.To, message.Subject, message.Text);
        return Task.CompletedTask;
    }
}

/// <summary>Deployed without an email provider: sign-in reports itself unavailable instead of failing silently.</summary>
public sealed class UnavailableEmailSender : IEmailSender
{
    public bool IsConfigured => false;

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken) =>
        throw new EmailDeliveryException("Email is not configured.");
}
