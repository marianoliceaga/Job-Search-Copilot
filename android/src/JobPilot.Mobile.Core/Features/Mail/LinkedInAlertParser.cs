using System.Globalization;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using JobPilot.Domain.Mail;

namespace JobPilot.Mobile.Core.Features.Mail;

/// <summary>
/// Reads roles out of LinkedIn job-alert e-mails (template email_job_alert_digest_*), ported from JobSearchDesk. Only the
/// e-mail is read; linkedin.com is never fetched. Each role is a <c>[data-test-id=job-card]</c> block with the title link,
/// a "Company · Location (Remote)" line, an optional pay line and optional flags. If the HTML changes shape, the plain-text
/// part is used instead (no workplace or pay there). The HTML is parsed, never rendered.
/// </summary>
public static partial class LinkedInAlertParser
{
    private static readonly HtmlParser Parser = new();

    public static AlertParseResult Parse(MailMessageInfo message, string threadId)
    {
        ArgumentNullException.ThrowIfNull(message);
        var alert = AlertName(message);
        var fromHtml = ParseHtml(message, alert, threadId);
        return fromHtml.Count > 0
            ? new AlertParseResult(fromHtml, UsedTextFallback: false)
            : new AlertParseResult(ParseText(message, alert, threadId), UsedTextFallback: true);
    }

    internal static (string Company, string Location, string Workplace) SplitCompanyLine(string line)
    {
        var parts = line.Split(" · ", 2);
        var company = parts[0].Trim();
        var location = parts.Length > 1 ? parts[1].Trim() : string.Empty;
        var workplace = string.Empty;
        var suffix = WorkplaceSuffixRegex().Match(location);
        if (suffix.Success)
        {
            workplace = suffix.Groups[1].Value.ToLowerInvariant() switch
            {
                "remote" or "remoto" => "remote",
                "hybrid" or "híbrido" or "hibrido" => "hybrid",
                _ => "onsite",
            };
            location = location[..suffix.Index].Trim();
        }

        return (company, location, workplace);
    }

    internal static AlertPay? ParsePay(string text)
    {
        var m = PayRegex().Match(text);
        if (!m.Success)
        {
            return null;
        }

        var low = Amount(m.Groups["lo"].Value, m.Groups["lk"].Value);
        var high = m.Groups["hi"].Success ? Amount(m.Groups["hi"].Value, m.Groups["hk"].Success ? m.Groups["hk"].Value : m.Groups["lk"].Value) : low;
        if (low is null || high is null)
        {
            return null;
        }

        var currency = m.Groups["cur"].Value.ToUpperInvariant() switch
        {
            "$" or "US$" or "USD" => "USD",
            "€" or "EUR" => "EUR",
            "£" or "GBP" => "GBP",
            var other => other,
        };
        var period = m.Groups["per"].Value.ToLowerInvariant() switch
        {
            "year" or "yr" or "annum" or "año" or "ano" => "year",
            "month" or "mo" or "mes" => "month",
            _ => "hour",
        };
        return new AlertPay(low.Value, high.Value, currency, period, m.Value.Trim());
    }

    private static List<AlertListing> ParseHtml(MailMessageInfo message, string alert, string threadId)
    {
        if (string.IsNullOrWhiteSpace(message.HtmlBody))
        {
            return [];
        }

        using var document = Parser.ParseDocument(message.HtmlBody);
        var listings = new List<AlertListing>();
        foreach (var card in document.QuerySelectorAll("[data-test-id='job-card']"))
        {
            var jobId = card.QuerySelectorAll("a[href]")
                .Select(a => JobIdRegex().Match(a.GetAttribute("href") ?? string.Empty))
                .FirstOrDefault(m => m.Success)?.Groups[1].Value;
            if (jobId is null)
            {
                continue;
            }

            // Some cards wrap the whole block in a second job link; the title is the link that holds only text.
            var title = card.QuerySelectorAll("a[href*='/jobs/view/']")
                .Where(a => a.QuerySelector("table, p, img") is null)
                .Select(a => Clean(a.TextContent))
                .FirstOrDefault(t => t.Length > 0);
            var lines = card.QuerySelectorAll("p").Select(p => Clean(p.TextContent)).Where(t => t.Length > 0).Distinct(StringComparer.Ordinal).ToList();
            var companyLine = lines.FirstOrDefault(l => l.Contains(" · ", StringComparison.Ordinal));
            if (title is null || companyLine is null)
            {
                continue;
            }

            var (company, location, workplace) = SplitCompanyLine(companyLine);
            var pay = lines.Select(ParsePay).FirstOrDefault(p => p is not null);
            var flags = lines.Where(l => l != companyLine && l != title && ParsePay(l) is null).ToList();
            listings.Add(new AlertListing(jobId, title, company, location, workplace, pay, flags, alert, threadId, message.Received));
        }

        return listings;
    }

    /// <summary>Text part: blocks separated by dashed rules, each "title / company / location / flags... / View job: url".</summary>
    private static List<AlertListing> ParseText(MailMessageInfo message, string alert, string threadId)
    {
        var listings = new List<AlertListing>();
        foreach (var block in BlockSeparatorRegex().Split((message.TextBody ?? string.Empty).Replace("\r", string.Empty, StringComparison.Ordinal)))
        {
            var jobId = JobIdRegex().Match(block);
            if (!jobId.Success)
            {
                continue;
            }

            var lines = block.Split('\n').Select(Clean)
                .Where(l => l.Length > 0 && !l.StartsWith("View job:", StringComparison.OrdinalIgnoreCase) && !l.StartsWith("Ver empleo:", StringComparison.OrdinalIgnoreCase))
                .ToList();
            // The first block also carries the alert heading and intro; the role starts after the intro line.
            var intro = lines.FindIndex(l => IntroRegex().IsMatch(l));
            if (intro >= 0)
            {
                lines = lines.Skip(intro + 1).ToList();
            }

            if (lines.Count < 3)
            {
                continue;
            }

            listings.Add(new AlertListing(jobId.Groups[1].Value, lines[0], lines[1], lines[2], string.Empty, null, lines.Skip(3).ToList(), alert, threadId, message.Received));
        }

        return listings;
    }

    private static decimal? Amount(string digits, string multiplier)
    {
        // "1.500.000" (dots as thousands) or "105" / "1,500" / "12.5".
        var normalized = ThousandsWithDotsRegex().IsMatch(digits) ? digits.Replace(".", string.Empty, StringComparison.Ordinal) : digits.Replace(",", string.Empty, StringComparison.Ordinal);
        if (!decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
        {
            return null;
        }

        return multiplier.ToUpperInvariant() switch
        {
            "K" => value * 1_000m,
            "M" => value * 1_000_000m,
            _ => value,
        };
    }

    private static string AlertName(MailMessageInfo message)
    {
        var first = (message.TextBody ?? string.Empty).Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n').Select(Clean).FirstOrDefault(l => l.Length > 0) ?? string.Empty;
        var m = AlertHeadingRegex().Match(first);
        return m.Success ? m.Groups[1].Value.Trim() : first.Length > 0 ? first : message.Subject;
    }

    private static string Clean(string value) => WhitespaceRegex().Replace(value, " ").Trim();

    [GeneratedRegex(@"/jobs/view/(\d+)")]
    private static partial Regex JobIdRegex();

    [GeneratedRegex(@"\n-{20,}\n")]
    private static partial Regex BlockSeparatorRegex();

    [GeneratedRegex(@"^(new jobs match|a new job matches|nuevos empleos|un nuevo empleo)", RegexOptions.IgnoreCase)]
    private static partial Regex IntroRegex();

    [GeneratedRegex(@"^(?:Your job alert for|Tu alerta de empleo para) (.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex AlertHeadingRegex();

    [GeneratedRegex(@"\((remote|remoto|hybrid|h[ií]brido|on-site|onsite|presencial)\)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex WorkplaceSuffixRegex();

    [GeneratedRegex(@"(?<cur>US\$|USD|EUR|GBP|ARS|\$|€|£)\s?(?<lo>\d[\d.,]*)\s?(?<lk>[KkMm])?\s?(?:[-–]\s?(?:US\$|USD|EUR|GBP|ARS|\$|€|£)?\s?(?<hi>\d[\d.,]*)\s?(?<hk>[KkMm])?)?\s*/\s*(?<per>year|yr|annum|month|mo|hour|hr|año|ano|mes|hora)\b", RegexOptions.IgnoreCase)]
    private static partial Regex PayRegex();

    [GeneratedRegex(@"^\d{1,3}(\.\d{3})+$")]
    private static partial Regex ThousandsWithDotsRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
