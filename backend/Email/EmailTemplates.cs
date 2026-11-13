using System.Net;
using System.Text;

namespace PokemonTCG.API.Email;

/// <summary>
/// Plain, inline-styled messages that render in every mail client. Every value that did not
/// come from this file is HTML-encoded.
/// </summary>
public static class EmailTemplates
{
    public static EmailMessage SignInCode(string to, string code, int validMinutes)
    {
        var subject = $"{code} is your TCG Signal sign-in code";
        var text =
            $"Your TCG Signal sign-in code is {code}\n\n" +
            $"It expires in {validMinutes} minutes and can be used once.\n\n" +
            "If you didn't try to sign in, you can ignore this email. Nobody can sign in without the code.";
        var html = Layout(
            $"""
            <p style="margin:0 0 16px">Your TCG Signal sign-in code is</p>
            <p style="margin:0 0 16px;font-size:32px;font-weight:700;letter-spacing:6px;font-family:Menlo,Consolas,monospace">{Encode(code)}</p>
            <p style="margin:0 0 16px">It expires in {validMinutes} minutes and can be used once.</p>
            <p style="margin:0;color:#64748b;font-size:13px">If you didn't try to sign in, you can ignore this email. Nobody can sign in without the code.</p>
            """);
        return new EmailMessage(to, subject, html, text);
    }

    public static EmailMessage AlertDigest(
        string to,
        IReadOnlyList<string> alerts,
        string siteUrl,
        Guid unsubscribeToken,
        string idempotencyKey,
        int maxListed = 20)
    {
        var site = siteUrl.TrimEnd('/');
        var dashboardUrl = $"{site}/dashboard";
        var unsubscribePage = $"{site}/email/unsubscribe?token={unsubscribeToken}";
        var oneClickUrl = $"{site}/api/email/unsubscribe?token={unsubscribeToken}";

        var listed = alerts.Take(maxListed).ToList();
        var more = alerts.Count - listed.Count;
        var subject = alerts.Count == 1
            ? "TCG Signal: 1 watchlist alert"
            : $"TCG Signal: {alerts.Count} watchlist alerts";

        var text = new StringBuilder();
        text.AppendLine("Your watchlist alerts from today's TCGplayer prices:").AppendLine();
        foreach (var alert in listed) text.Append("- ").AppendLine(alert);
        if (more > 0) text.AppendLine($"- and {more} more");
        text.AppendLine().AppendLine($"See them all: {dashboardUrl}");
        text.AppendLine().AppendLine($"Stop alert emails: {unsubscribePage}");

        var items = new StringBuilder();
        foreach (var alert in listed)
            items.Append($"""<li style="margin:0 0 8px">{Encode(alert)}</li>""");
        if (more > 0)
            items.Append($"""<li style="margin:0 0 8px">and {more} more</li>""");

        var html = Layout(
            $"""
            <p style="margin:0 0 16px">Your watchlist alerts from today's TCGplayer prices:</p>
            <ul style="margin:0 0 20px;padding-left:20px">{items}</ul>
            <p style="margin:0 0 24px"><a href="{Encode(dashboardUrl)}" style="color:#1d4ed8;font-weight:600">Open your dashboard</a></p>
            <p style="margin:0;color:#64748b;font-size:13px">You get these because you turned on watchlist alerts. <a href="{Encode(unsubscribePage)}" style="color:#64748b">Stop alert emails</a>.</p>
            """);

        // RFC 8058 one-click unsubscribe, which Gmail and Yahoo expect from bulk senders.
        var headers = new Dictionary<string, string>
        {
            ["List-Unsubscribe"] = $"<{oneClickUrl}>",
            ["List-Unsubscribe-Post"] = "List-Unsubscribe=One-Click",
        };

        return new EmailMessage(to, subject, html, text.ToString(), headers, idempotencyKey);
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);

    private static string Layout(string body) =>
        $"""
        <!doctype html>
        <html>
        <body style="margin:0;padding:24px;background:#f8fafc;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Arial,sans-serif;color:#0f172a;font-size:15px;line-height:1.5">
          <div style="max-width:520px;margin:0 auto;background:#ffffff;border:1px solid #e2e8f0;border-radius:12px;padding:28px">
            <p style="margin:0 0 20px;font-weight:800;font-size:18px;color:#1e3a8a">TCG Signal</p>
            {body}
          </div>
        </body>
        </html>
        """;
}
