using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Xml.Linq;
using JobPilot.Web.Infrastructure.Text;
using Microsoft.Extensions.Options;

namespace JobPilot.Web.Features.Jobs.Sources;

/// <summary>Remotive public API (verified 2026-10-02). Terms: link back and credit Remotive; at most ~4 requests a day.</summary>
public sealed class RemotiveSource(HttpClient http, IOptions<JobSourceOptions> options) : IJobSource
{
    public const string SourceKey = "remotive";

    public string Key => SourceKey;

    public string DisplayName => "Remotive";

    public string TermsSummary => "API pública. Hay que enlazar la oferta original y citar a Remotive; máximo ~4 consultas por día.";

    public async Task<SourceFetchResult> FetchAsync(SourceFetchContext context, CancellationToken cancellationToken)
    {
        var jobs = new List<SourcedJob>();
        foreach (var url in options.Value.For(Key).Urls)
        {
            jobs.AddRange(Parse(await http.GetStringAsync(url, cancellationToken)));
        }

        return new SourceFetchResult(jobs, []);
    }

    public static IReadOnlyList<SourcedJob> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("jobs", out var jobs))
        {
            return [];
        }

        return jobs.EnumerateArray().Select(job => new SourcedJob
        {
            SourceKey = SourceKey,
            SourceName = "Remotive",
            SourceJobId = job.GetProperty("id").ToString(),
            Title = SourceParsing.String(job, "title") ?? "(sin título)",
            Company = SourceParsing.String(job, "company_name") ?? "(sin empresa)",
            Url = SourceParsing.String(job, "url")!,
            Location = SourceParsing.String(job, "candidate_required_location"),
            EmploymentType = SourceParsing.String(job, "job_type"),
            Tags = SourceParsing.Strings(job, "tags"),
            Description = HtmlText.ToPlainText(SourceParsing.String(job, "description")),
            PublishedAtUtc = SourceParsing.Date(job, "publication_date"),
            SalaryText = SourceParsing.String(job, "salary"),
        }).Where(j => j.Url is not null).ToList();
    }
}

/// <summary>Remote OK public API (verified 2026-10-02). Terms: link back and mention Remote OK; no logo.</summary>
public sealed class RemoteOkSource(HttpClient http, IOptions<JobSourceOptions> options) : IJobSource
{
    public const string SourceKey = "remoteok";

    public string Key => SourceKey;

    public string DisplayName => "Remote OK";

    public string TermsSummary => "API pública. Hay que enlazar la oferta original y citar a Remote OK; no usar su logo.";

    public async Task<SourceFetchResult> FetchAsync(SourceFetchContext context, CancellationToken cancellationToken)
    {
        var jobs = new List<SourcedJob>();
        foreach (var url in options.Value.For(Key).Urls)
        {
            jobs.AddRange(Parse(await http.GetStringAsync(url, cancellationToken)));
        }

        return new SourceFetchResult(jobs, []);
    }

    public static IReadOnlyList<SourcedJob> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var result = new List<SourcedJob>();
        foreach (var job in document.RootElement.EnumerateArray())
        {
            // The first element is the legal notice, not a job.
            if (!job.TryGetProperty("position", out _) || SourceParsing.String(job, "url") is not { } url)
            {
                continue;
            }

            var min = SourceParsing.Number(job, "salary_min");
            var max = SourceParsing.Number(job, "salary_max");
            result.Add(new SourcedJob
            {
                SourceKey = SourceKey,
                SourceName = "Remote OK",
                SourceJobId = job.TryGetProperty("id", out var id) ? id.ToString() : null,
                Title = SourceParsing.String(job, "position") ?? "(sin título)",
                Company = SourceParsing.String(job, "company") ?? "(sin empresa)",
                Url = url,
                ApplyUrl = SourceParsing.String(job, "apply_url"),
                Location = SourceParsing.String(job, "location"),
                Tags = SourceParsing.Strings(job, "tags"),
                Description = HtmlText.ToPlainText(SourceParsing.String(job, "description")),
                PublishedAtUtc = SourceParsing.Date(job, "date"),
                // Remote OK publishes annual USD ranges.
                SalaryMin = min,
                SalaryMax = max,
                SalaryCurrency = min is null && max is null ? null : "USD",
                SalaryPeriod = min is null && max is null ? null : Domain.Jobs.SalaryPeriod.Year,
                SalaryText = SourceParsing.SalaryText(min, max, "USD", Domain.Jobs.SalaryPeriod.Year),
            });
        }

        return result;
    }
}

/// <summary>Himalayas public search API (verified 2026-10-02). Terms: link back and credit Himalayas; server-side only.</summary>
public sealed class HimalayasSource(HttpClient http, IOptions<JobSourceOptions> options) : IJobSource
{
    public const string SourceKey = "himalayas";

    public string Key => SourceKey;

    public string DisplayName => "Himalayas";

    public string TermsSummary => "API pública. Hay que enlazar la oferta original y citar a Himalayas; no reenviar a otros agregadores.";

    public async Task<SourceFetchResult> FetchAsync(SourceFetchContext context, CancellationToken cancellationToken)
    {
        var settings = options.Value.For(Key);
        var jobs = new List<SourcedJob>();
        // The search endpoint takes one query; use the first keywords (20 results each).
        foreach (var keyword in context.Keywords.Take(settings.MaxKeywordQueries))
        {
            foreach (var url in settings.Urls)
            {
                jobs.AddRange(Parse(await http.GetStringAsync(url + Uri.EscapeDataString(keyword), cancellationToken)));
            }
        }

        return new SourceFetchResult(jobs, []);
    }

    public static IReadOnlyList<SourcedJob> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("jobs", out var jobs))
        {
            return [];
        }

        return jobs.EnumerateArray().Select(job =>
        {
            var min = SourceParsing.Number(job, "minSalary");
            var max = SourceParsing.Number(job, "maxSalary");
            var currency = min is null && max is null ? null : SourceParsing.String(job, "currency");
            var period = min is null && max is null ? null : SourceParsing.Period(SourceParsing.String(job, "salaryPeriod"));
            var restrictions = SourceParsing.Strings(job, "locationRestrictions");
            return new SourcedJob
            {
                SourceKey = SourceKey,
                SourceName = "Himalayas",
                SourceJobId = SourceParsing.String(job, "guid"),
                Title = SourceParsing.String(job, "title") ?? "(sin título)",
                Company = SourceParsing.String(job, "companyName") ?? "(sin empresa)",
                Url = SourceParsing.String(job, "applicationLink") ?? SourceParsing.String(job, "guid")!,
                Location = restrictions.Count == 0 ? "Worldwide" : string.Join(", ", restrictions),
                EmploymentType = SourceParsing.String(job, "employmentType"),
                Tags = SourceParsing.Strings(job, "categories"),
                Description = HtmlText.ToPlainText(SourceParsing.String(job, "description")),
                PublishedAtUtc = SourceParsing.Date(job, "pubDate"),
                SalaryMin = min,
                SalaryMax = max,
                SalaryCurrency = currency,
                SalaryPeriod = period,
                SalaryText = SourceParsing.SalaryText(min, max, currency, period),
            };
        }).Where(j => j.Url is not null).ToList();
    }
}

/// <summary>Jobicy public API v2 (verified 2026-10-02). Terms: credit Jobicy and keep the canonical job URL; ≤ 1 request/hour.</summary>
public sealed class JobicySource(HttpClient http, IOptions<JobSourceOptions> options) : IJobSource
{
    public const string SourceKey = "jobicy";

    public string Key => SourceKey;

    public string DisplayName => "Jobicy";

    public string TermsSummary => "API pública. Hay que citar a Jobicy y conservar el enlace original; máximo una consulta por hora.";

    public async Task<SourceFetchResult> FetchAsync(SourceFetchContext context, CancellationToken cancellationToken)
    {
        var jobs = new List<SourcedJob>();
        foreach (var url in options.Value.For(Key).Urls)
        {
            jobs.AddRange(Parse(await http.GetStringAsync(url, cancellationToken)));
        }

        return new SourceFetchResult(jobs, []);
    }

    public static IReadOnlyList<SourcedJob> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("jobs", out var jobs))
        {
            return [];
        }

        return jobs.EnumerateArray().Select(job =>
        {
            var min = SourceParsing.Number(job, "salaryMin");
            var max = SourceParsing.Number(job, "salaryMax");
            var currency = min is null && max is null ? null : SourceParsing.String(job, "salaryCurrency");
            var period = min is null && max is null ? null : SourceParsing.Period(SourceParsing.String(job, "salaryPeriod"));
            return new SourcedJob
            {
                SourceKey = SourceKey,
                SourceName = "Jobicy",
                SourceJobId = job.TryGetProperty("id", out var id) ? id.ToString() : null,
                Title = WebUtility.HtmlDecode(SourceParsing.String(job, "jobTitle") ?? "(sin título)"),
                Company = WebUtility.HtmlDecode(SourceParsing.String(job, "companyName") ?? "(sin empresa)"),
                Url = SourceParsing.String(job, "url")!,
                Location = SourceParsing.String(job, "jobGeo"),
                EmploymentType = string.Join(", ", SourceParsing.Strings(job, "jobType")),
                Tags = SourceParsing.Strings(job, "jobIndustry"),
                Description = HtmlText.ToPlainText(SourceParsing.String(job, "jobDescription")),
                PublishedAtUtc = SourceParsing.Date(job, "pubDate"),
                SalaryMin = min,
                SalaryMax = max,
                SalaryCurrency = currency,
                SalaryPeriod = period,
                SalaryText = SourceParsing.SalaryText(min, max, currency, period),
            };
        }).Where(j => j.Url is not null).ToList();
    }
}

/// <summary>We Work Remotely RSS feeds (enabled by the owner on 2026-10-02). Terms: link back to We Work Remotely.</summary>
public sealed class WeWorkRemotelySource(HttpClient http, IOptions<JobSourceOptions> options) : IJobSource
{
    public const string SourceKey = "weworkremotely";

    public string Key => SourceKey;

    public string DisplayName => "We Work Remotely";

    public string TermsSummary => "RSS público. Hay que enlazar la oferta original en We Work Remotely.";

    public async Task<SourceFetchResult> FetchAsync(SourceFetchContext context, CancellationToken cancellationToken)
    {
        var jobs = new List<SourcedJob>();
        foreach (var url in options.Value.For(Key).Urls)
        {
            jobs.AddRange(Parse(await http.GetStringAsync(url, cancellationToken)));
        }

        return new SourceFetchResult(jobs, []);
    }

    public static IReadOnlyList<SourcedJob> Parse(string rss)
    {
        var document = XDocument.Parse(rss);
        return document.Descendants("item").Select(item =>
        {
            // Titles are "Company: Job title".
            var rawTitle = (string?)item.Element("title") ?? string.Empty;
            var separator = rawTitle.IndexOf(": ", StringComparison.Ordinal);
            var company = separator > 0 ? rawTitle[..separator].Trim() : "(sin empresa)";
            var title = separator > 0 ? rawTitle[(separator + 2)..].Trim() : rawTitle.Trim();
            var published = DateTimeOffset.TryParse((string?)item.Element("pubDate"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)
                ? date.UtcDateTime
                : (DateTime?)null;
            var skills = ((string?)item.Element("skills") ?? string.Empty)
                .Replace(", and ", ", ", StringComparison.Ordinal).Replace(" and ", ", ", StringComparison.Ordinal)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            return new SourcedJob
            {
                SourceKey = SourceKey,
                SourceName = "We Work Remotely",
                SourceJobId = (string?)item.Element("guid"),
                Title = title,
                Company = company,
                Url = ((string?)item.Element("link") ?? (string?)item.Element("guid"))!,
                // <country> (when present) is more precise than <region>: "Anywhere in the World" may list only European countries.
                Location = Countries((string?)item.Element("country")) ?? (string?)item.Element("region"),
                EmploymentType = (string?)item.Element("type"),
                Tags = skills,
                Description = HtmlText.ToPlainText((string?)item.Element("description")),
                PublishedAtUtc = published,
            };
        }).Where(j => !string.IsNullOrWhiteSpace(j.Url)).ToList();
    }

    private static string? Countries(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        // Drop flag emojis (pairs of regional indicator symbols) and the final "and".
        var withoutFlags = System.Text.RegularExpressions.Regex.Replace(text, @"\uD83C[\uDDE6-\uDDFF]", string.Empty);
        // Only the list's final ", and " is a separator: names like "Bosnia and Herzegovina" keep theirs.
        var countries = withoutFlags.Replace(", and ", ", ", StringComparison.Ordinal)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return countries.Length == 0 ? null : string.Join(", ", countries);
    }
}
