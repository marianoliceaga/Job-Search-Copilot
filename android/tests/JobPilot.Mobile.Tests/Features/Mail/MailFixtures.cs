using System.Text;
using JobPilot.Domain.Mail;
using JobPilot.Mobile.Core.Infrastructure.Google;

namespace JobPilot.Mobile.Tests.Features.Mail;

/// <summary>
/// Fictional mail in the shapes Gmail and LinkedIn use (alert cards mirror template email_job_alert_digest_01, reduced to
/// what the parser reads). Companies, people and ids are made up.
/// </summary>
internal static class MailFixtures
{
    public const string AlertSender = "jobalerts-noreply@linkedin.com";

    public static readonly DateTimeOffset Received = new(2026, 10, 6, 11, 0, 0, TimeSpan.Zero);

    public static string AlertCard(string jobId, string title, string companyLine, params string[] extraLines)
    {
        var href = $"https://www.linkedin.com/comm/jobs/view/{jobId}/?trackingId=x";
        var extras = string.Concat(extraLines.Select(l => $"<tr><td><p class=\"text-system-gray-70 text-xs\">{l}</p></td></tr>"));
        return $"""
            <tr><td class="pt-3" data-test-id="job-card"><table><tbody><tr>
              <td class="pr-1 w-6"><a href="{href}"><img alt="logo" src="x.png"></a></td>
              <td><a href="{href}"><table><tbody>
                <tr><td class="pb-0"><a href="{href}" class="font-bold text-md">{title}</a></td></tr>
                <tr><td><p class="text-system-gray-100 text-xs">{companyLine}</p></td></tr>
                {extras}
              </tbody></table></a></td>
            </tr></tbody></table></td></tr>
            """;
    }

    public static string AlertHtml(params string[] cards) => $"<html><body><table><tbody>{string.Concat(cards)}</tbody></table></body></html>";

    public static string AlertText(string alertName) => $"Your job alert for {alertName}\nNew jobs match your preferences.\n";

    public static MailMessageInfo AlertMessage(string id, string alertName, string? html, string? text = null) => new(
        id, Received, new MailPerson("LinkedIn Job Alerts", AlertSender), "New jobs for you", html, text ?? AlertText(alertName),
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["X-LinkedIn-Template"] = "email_job_alert_digest_01", ["List-Unsubscribe"] = "<https://example.com/u>" },
        [], false);

    public static string Ics(string method, DateTimeOffset start, string partstat = "NEEDS-ACTION", string uid = "uid-1@example.com") => $"""
        BEGIN:VCALENDAR
        PRODID:-//Test//EN
        VERSION:2.0
        METHOD:{method}
        BEGIN:VEVENT
        UID:{uid}
        SEQUENCE:0
        DTSTAMP:20261004T120000Z
        DTSTART:{start.UtcDateTime:yyyyMMdd'T'HHmmss'Z'}
        DTEND:{start.UtcDateTime.AddHours(1):yyyyMMdd'T'HHmmss'Z'}
        SUMMARY:Technical interview - Contoso
        ORGANIZER;CN=Ana Recruiter:mailto:ana@example.com
        ATTENDEE;CN=Owner;PARTSTAT={partstat};RSVP=TRUE:mailto:owner@example.com
        END:VEVENT
        END:VCALENDAR
        """.Replace("\n", "\r\n", StringComparison.Ordinal);

    public static string B64(string text) => GmailClient.ToBase64Url(Encoding.UTF8.GetBytes(text));

    public static object Header(string name, string value) => new { name, value };

    /// <summary>A Gmail <c>format=full</c> message with text and/or HTML parts.</summary>
    public static object GmailMessage(string id, DateTimeOffset received, string from, string subject, string? text = null, string? html = null,
        IEnumerable<object>? extraHeaders = null, IEnumerable<object>? extraParts = null, string[]? labels = null)
    {
        var parts = new List<object>();
        if (text is not null)
        {
            parts.Add(new { mimeType = "text/plain", headers = new[] { Header("Content-Type", "text/plain; charset=UTF-8") }, body = new { data = B64(text) } });
        }

        if (html is not null)
        {
            parts.Add(new { mimeType = "text/html", headers = new[] { Header("Content-Type", "text/html; charset=\"UTF-8\"") }, body = new { data = B64(html) } });
        }

        parts.AddRange(extraParts ?? []);
        return new
        {
            id,
            labelIds = labels ?? ["INBOX"],
            internalDate = received.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture),
            payload = new
            {
                mimeType = "multipart/mixed",
                headers = new[] { Header("From", from), Header("Subject", subject) }.Concat(extraHeaders ?? []).ToArray(),
                parts,
            },
        };
    }
}
