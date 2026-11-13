using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using PokemonTCG.API.Email;

namespace PokemonTcgMarketplace.Backend.Tests;

/// <summary>Records emails instead of sending them.</summary>
public sealed partial class FakeEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _sent = new();

    public bool IsConfigured => true;

    public IReadOnlyList<EmailMessage> Sent => _sent.ToArray();

    public IReadOnlyList<EmailMessage> SentTo(string address) =>
        _sent.Where(m => string.Equals(m.To, address, StringComparison.OrdinalIgnoreCase)).ToArray();

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        _sent.Enqueue(message);
        return Task.CompletedTask;
    }

    /// <summary>The code in the most recent sign-in email to <paramref name="address"/>.</summary>
    public string LatestCodeFor(string address)
    {
        var message = SentTo(address).Last(m => m.Subject.Contains("sign-in code"));
        return SixDigits().Match(message.Text).Value;
    }

    [GeneratedRegex(@"\b\d{6}\b")]
    private static partial Regex SixDigits();
}
