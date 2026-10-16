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

/// <summary>
/// Get on Board public API v0 (verified 2026-10-15): open to anyone without a key, same data as the site without login.
/// Salaries are gross USD per month. One request per keyword, remote jobs only.
/// </summary>
public sealed class GetOnBoardSource(HttpClient http, IOptions<JobSourceOptions> options) : IJobSource
{
    public const string SourceKey = "getonboard";

    public string Key => SourceKey;

    public string DisplayName => "Get on Board";

    public string TermsSummary => "API pública sin clave. Hay que enlazar la oferta original en Get on Board y citarla.";

    public async Task<SourceFetchResult> FetchAsync(SourceFetchContext context, CancellationToken cancellationToken)
    {
        var settings = options.Value.For(Key);
        var jobs = new List<SourcedJob>();
        // The search endpoint takes one text query; use the first keywords.
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
        if (!document.RootElement.TryGetProperty("data", out var data))
        {
            return [];
        }

        var result = new List<SourcedJob>();
        foreach (var job in data.EnumerateArray())
        {
            if (!job.TryGetProperty("attributes", out var attributes)
                || !job.TryGetProperty("links", out var links)
                || SourceParsing.String(links, "public_url") is not { } url)
            {
                continue;
            }

            var min = SourceParsing.Number(attributes, "min_salary");
            var max = SourceParsing.Number(attributes, "max_salary");
            var hasSalary = min is not null || max is not null;
            result.Add(new SourcedJob
            {
                SourceKey = SourceKey,
                SourceName = "Get on Board",
                SourceJobId = SourceParsing.String(job, "id"),
                Title = SourceParsing.String(attributes, "title") ?? "(sin título)",
                Company = CompanyName(attributes) ?? "(sin empresa)",
                Url = url,
                Location = Location(attributes),
                EmploymentType = SourceParsing.String(attributes, "remote_modality"),
                Tags = SourceParsing.Strings(attributes, "category_name"),
                Description = Description(attributes),
                PublishedAtUtc = SourceParsing.Date(attributes, "published_at"),
                // Get on Board shows "Gross salary $X - Y USD/month".
                SalaryMin = min,
                SalaryMax = max,
                SalaryCurrency = hasSalary ? "USD" : null,
                SalaryPeriod = hasSalary ? Domain.Jobs.SalaryPeriod.Month : null,
                SalaryText = SourceParsing.SalaryText(min, max, "USD", Domain.Jobs.SalaryPeriod.Month),
            });
        }

        return result;
    }

    // Present only when the request asks for expand=["company"].
    private static string? CompanyName(JsonElement attributes) =>
        attributes.TryGetProperty("company", out var company)
        && company.ValueKind == JsonValueKind.Object
        && company.TryGetProperty("data", out var data)
        && data.ValueKind == JsonValueKind.Object
        && data.TryGetProperty("attributes", out var companyAttributes)
            ? SourceParsing.String(companyAttributes, "name")
            : null;

    private static string? Location(JsonElement attributes)
    {
        // countries is a list ("Remote" for fully remote jobs); remote_zone is usually null.
        var parts = SourceParsing.Strings(attributes, "countries")
            .Append(SourceParsing.String(attributes, "remote_zone"))
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    // The posting is split into sections, each with an optional headline.
    private static string Description(JsonElement attributes)
    {
        var sections = new[] { "description", "projects", "functions", "desirable", "benefits" }
            .Select(name => (Headline: SourceParsing.String(attributes, name + "_headline"), Body: HtmlText.ToPlainText(SourceParsing.String(attributes, name))))
            .Where(s => s.Body.Length > 0)
            .Select(s => s.Headline is null ? s.Body : $"{s.Headline}\n\n{s.Body}");
        return string.Join("\n\n", sections);
    }
}

/// <summary>
/// Working Nomads public JSON feed, linked as "API" in the site's footer (verified 2026-10-15). Current jobs of every
/// category; robots.txt allows everything; the terms forbid resale and competing products, not personal reading.
/// </summary>
public sealed class WorkingNomadsSource(HttpClient http, IOptions<JobSourceOptions> options) : IJobSource
{
    public const string SourceKey = "workingnomads";

    public string Key => SourceKey;

    public string DisplayName => "Working Nomads";

    public string TermsSummary => "Feed JSON público (enlace \"API\" del sitio). Hay que enlazar la oferta original y citar a Working Nomads.";

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
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<SourcedJob>();
        foreach (var job in document.RootElement.EnumerateArray())
        {
            if (SourceParsing.String(job, "url") is not { } url)
            {
                continue;
            }

            // Tags are one comma-separated string; the category goes first.
            var tags = (SourceParsing.String(job, "tags") ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            result.Add(new SourcedJob
            {
                SourceKey = SourceKey,
                SourceName = "Working Nomads",
                SourceJobId = url,
                Title = SourceParsing.String(job, "title") ?? "(sin título)",
                Company = SourceParsing.String(job, "company_name") ?? "(sin empresa)",
                Url = url,
                Location = SourceParsing.String(job, "location"),
                Tags = SourceParsing.String(job, "category_name") is { } category ? [category, .. tags] : tags,
                Description = HtmlText.ToPlainText(SourceParsing.String(job, "description")),
                PublishedAtUtc = SourceParsing.Date(job, "pub_date"),
            });
        }

        return result;
    }
}

/// <summary>
/// Jobspresso job RSS at <c>/jobs/feed/</c> (verified 2026-10-15). robots.txt disallows URLs with a query string, so the
/// filterable <c>?feed=job_feed</c> is not used; no terms page addresses feeds.
/// </summary>
public sealed class JobspressoSource(HttpClient http, IOptions<JobSourceOptions> options) : IJobSource
{
    public const string SourceKey = "jobspresso";

    private static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";

    private static readonly XNamespace Content = "http://purl.org/rss/1.0/modules/content/";

    public string Key => SourceKey;

    public string DisplayName => "Jobspresso";

    public string TermsSummary => "RSS público. Hay que enlazar la oferta original en Jobspresso.";

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
            // dc:creator is "Company<br>⚲&nbsp;Location".
            var creator = HtmlText.ToPlainText((string?)item.Element(Dc + "creator"))
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var location = creator.Length > 1 ? creator[1].TrimStart('⚲').Trim() : null;
            var published = DateTimeOffset.TryParse((string?)item.Element("pubDate"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)
                ? date.UtcDateTime
                : (DateTime?)null;

            return new SourcedJob
            {
                SourceKey = SourceKey,
                SourceName = "Jobspresso",
                SourceJobId = (string?)item.Element("guid"),
                Title = WebUtility.HtmlDecode(((string?)item.Element("title"))?.Trim() ?? "(sin título)"),
                Company = creator.Length > 0 ? creator[0] : "(sin empresa)",
                Url = (((string?)item.Element("link"))?.Trim())!,
                Location = string.IsNullOrEmpty(location) ? null : location,
                Description = HtmlText.ToPlainText((string?)item.Element(Content + "encoded") ?? (string?)item.Element("description")),
                PublishedAtUtc = published,
            };
        }).Where(j => !string.IsNullOrWhiteSpace(j.Url)).ToList();
    }
}
