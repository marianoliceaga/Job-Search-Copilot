using System.Net;
using System.Text.Json;
using JobPilot.Domain.Jobs;
using JobPilot.Web.Infrastructure.Text;
using Microsoft.Extensions.Options;

namespace JobPilot.Web.Features.Jobs.Sources;

/// <summary>Public job boards of followed companies on an ATS. Each board is one request.</summary>
public abstract class AtsSource(HttpClient http, IOptions<JobSourceOptions> options) : IJobSource
{
    public abstract AtsKind Ats { get; }

    public abstract string Key { get; }

    public abstract string DisplayName { get; }

    public string TermsSummary => "API pública del tablero de empleos de cada empresa seguida (sin autenticación). Se enlaza la oferta original.";

    public async Task<SourceFetchResult> FetchAsync(SourceFetchContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var jobs = new List<SourcedJob>();
        var warnings = new List<string>();
        foreach (var company in context.FollowedCompanies.Where(c => c.Ats == Ats))
        {
            var (boardJobs, warning) = await FetchBoardAsync(company.BoardToken, company.Name, cancellationToken);
            jobs.AddRange(boardJobs);
            if (warning is not null)
            {
                warnings.Add(warning);
            }
        }

        return new SourceFetchResult(jobs, warnings);
    }

    /// <summary>Reads one board. A missing board is a warning, not a failure of the whole source.</summary>
    public async Task<(IReadOnlyList<SourcedJob> Jobs, string? Warning)> FetchBoardAsync(string boardToken, string companyName, CancellationToken cancellationToken)
    {
        var url = options.Value.For(Key).Urls[0].Replace("{board}", Uri.EscapeDataString(boardToken), StringComparison.Ordinal);
        using var response = await http.GetAsync(url, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return ([], $"{DisplayName}: el tablero \"{boardToken}\" ({companyName}) no existe o ya no es público.");
        }

        response.EnsureSuccessStatusCode();
        return (Parse(await response.Content.ReadAsStringAsync(cancellationToken), companyName), null);
    }

    public abstract IReadOnlyList<SourcedJob> Parse(string json, string companyName);
}

/// <summary>Greenhouse Job Board API: <c>boards-api.greenhouse.io/v1/boards/{board}/jobs?content=true</c> (verified 2026-10-02).</summary>
public sealed class GreenhouseSource(HttpClient http, IOptions<JobSourceOptions> options) : AtsSource(http, options)
{
    public const string SourceKey = "greenhouse";

    public override AtsKind Ats => AtsKind.Greenhouse;

    public override string Key => SourceKey;

    public override string DisplayName => "Greenhouse";

    public override IReadOnlyList<SourcedJob> Parse(string json, string companyName)
    {
        using var document = JsonDocument.Parse(json);
        var jobs = document.RootElement.TryGetProperty("jobs", out var list) ? list.EnumerateArray().ToList() : [document.RootElement];
        return jobs.Select(job => new SourcedJob
        {
            SourceKey = SourceKey,
            SourceName = $"Greenhouse · {companyName}",
            SourceJobId = job.GetProperty("id").ToString(),
            Title = SourceParsing.String(job, "title") ?? "(sin título)",
            Company = SourceParsing.String(job, "company_name") ?? companyName,
            Url = SourceParsing.String(job, "absolute_url")!,
            Location = job.TryGetProperty("location", out var location) ? SourceParsing.String(location, "name") : null,
            Tags = job.TryGetProperty("departments", out var departments)
                ? departments.EnumerateArray().Select(d => SourceParsing.String(d, "name")).OfType<string>().ToList()
                : [],
            // Greenhouse returns the HTML escaped.
            Description = HtmlText.ToPlainText(WebUtility.HtmlDecode(SourceParsing.String(job, "content") ?? string.Empty)),
            PublishedAtUtc = SourceParsing.Date(job, "first_published") ?? SourceParsing.Date(job, "updated_at"),
        }).Where(j => j.Url is not null).ToList();
    }
}

/// <summary>Lever Postings API: <c>api.lever.co/v0/postings/{board}?mode=json</c> (verified 2026-10-02; board is case-sensitive).</summary>
public sealed class LeverSource(HttpClient http, IOptions<JobSourceOptions> options) : AtsSource(http, options)
{
    public const string SourceKey = "lever";

    public override AtsKind Ats => AtsKind.Lever;

    public override string Key => SourceKey;

    public override string DisplayName => "Lever";

    public override IReadOnlyList<SourcedJob> Parse(string json, string companyName)
    {
        using var document = JsonDocument.Parse(json);
        var jobs = document.RootElement.ValueKind == JsonValueKind.Array ? document.RootElement.EnumerateArray().ToList() : [document.RootElement];
        return jobs.Select(job =>
        {
            var categories = job.TryGetProperty("categories", out var c) ? c : default;
            var lists = job.TryGetProperty("lists", out var l) && l.ValueKind == JsonValueKind.Array
                ? string.Join("\n\n", l.EnumerateArray().Select(item => SourceParsing.String(item, "text") + "\n" + HtmlText.ToPlainText(SourceParsing.String(item, "content"))))
                : string.Empty;
            var salary = job.TryGetProperty("salaryRange", out var range) && range.ValueKind == JsonValueKind.Object ? range : default;
            var min = salary.ValueKind == JsonValueKind.Object ? SourceParsing.Number(salary, "min") : null;
            var max = salary.ValueKind == JsonValueKind.Object ? SourceParsing.Number(salary, "max") : null;
            var currency = salary.ValueKind == JsonValueKind.Object ? SourceParsing.String(salary, "currency") : null;
            var period = salary.ValueKind == JsonValueKind.Object ? SourceParsing.Period(SourceParsing.String(salary, "interval")) : null;
            var workplace = SourceParsing.String(job, "workplaceType");
            var location = categories.ValueKind == JsonValueKind.Object ? SourceParsing.String(categories, "location") : null;

            return new SourcedJob
            {
                SourceKey = SourceKey,
                SourceName = $"Lever · {companyName}",
                SourceJobId = SourceParsing.String(job, "id"),
                Title = SourceParsing.String(job, "text") ?? "(sin título)",
                Company = companyName,
                Url = SourceParsing.String(job, "hostedUrl")!,
                ApplyUrl = SourceParsing.String(job, "applyUrl"),
                Location = workplace is null || location is null ? location ?? workplace : $"{location} ({workplace})",
                EmploymentType = categories.ValueKind == JsonValueKind.Object ? SourceParsing.String(categories, "commitment") : null,
                Tags = categories.ValueKind == JsonValueKind.Object && SourceParsing.String(categories, "team") is { } team ? [team] : [],
                Description = string.Join("\n\n", new[] { SourceParsing.String(job, "descriptionPlain"), lists, SourceParsing.String(job, "additionalPlain") }
                    .Where(s => !string.IsNullOrWhiteSpace(s))),
                PublishedAtUtc = SourceParsing.Date(job, "createdAt"),
                SalaryMin = min,
                SalaryMax = max,
                SalaryCurrency = min is null && max is null ? null : currency,
                SalaryPeriod = min is null && max is null ? null : period,
                SalaryText = SourceParsing.SalaryText(min, max, currency, period),
            };
        }).Where(j => j.Url is not null).ToList();
    }
}

/// <summary>Ashby posting API: <c>api.ashbyhq.com/posting-api/job-board/{board}?includeCompensation=true</c> (verified 2026-10-02).</summary>
public sealed class AshbySource(HttpClient http, IOptions<JobSourceOptions> options) : AtsSource(http, options)
{
    public const string SourceKey = "ashby";

    public override AtsKind Ats => AtsKind.Ashby;

    public override string Key => SourceKey;

    public override string DisplayName => "Ashby";

    public override IReadOnlyList<SourcedJob> Parse(string json, string companyName)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("jobs", out var jobs))
        {
            return [];
        }

        return jobs.EnumerateArray()
            .Where(job => !job.TryGetProperty("isListed", out var listed) || listed.ValueKind != JsonValueKind.False)
            .Select(job =>
            {
                var (min, max, currency, period, summary) = Salary(job);
                var secondary = job.TryGetProperty("secondaryLocations", out var s) && s.ValueKind == JsonValueKind.Array
                    ? s.EnumerateArray().Select(l => SourceParsing.String(l, "location")).OfType<string>()
                    : [];
                var location = string.Join(" · ", new[] { SourceParsing.String(job, "location") }.Concat(secondary).OfType<string>().Distinct());
                return new SourcedJob
                {
                    SourceKey = SourceKey,
                    SourceName = $"Ashby · {companyName}",
                    SourceJobId = SourceParsing.String(job, "id"),
                    Title = SourceParsing.String(job, "title") ?? "(sin título)",
                    Company = companyName,
                    Url = SourceParsing.String(job, "jobUrl")!,
                    ApplyUrl = SourceParsing.String(job, "applyUrl"),
                    Location = location.Length == 0 ? SourceParsing.String(job, "workplaceType") : location,
                    EmploymentType = SourceParsing.String(job, "employmentType"),
                    Tags = new[] { SourceParsing.String(job, "department"), SourceParsing.String(job, "team") }.OfType<string>().ToList(),
                    Description = SourceParsing.String(job, "descriptionPlain") ?? HtmlText.ToPlainText(SourceParsing.String(job, "descriptionHtml")),
                    PublishedAtUtc = SourceParsing.Date(job, "publishedAt"),
                    SalaryMin = min,
                    SalaryMax = max,
                    SalaryCurrency = currency,
                    SalaryPeriod = period,
                    SalaryText = summary,
                };
            }).Where(j => j.Url is not null).ToList();
    }

    private static (decimal? Min, decimal? Max, string? Currency, SalaryPeriod? Period, string? Summary) Salary(JsonElement job)
    {
        if (!job.TryGetProperty("compensation", out var compensation) || compensation.ValueKind != JsonValueKind.Object)
        {
            return (null, null, null, null, null);
        }

        var summary = SourceParsing.String(compensation, "compensationTierSummary") ?? SourceParsing.String(compensation, "scrapeableCompensationSalarySummary");
        var salary = compensation.TryGetProperty("summaryComponents", out var components) && components.ValueKind == JsonValueKind.Array
            ? components.EnumerateArray().FirstOrDefault(c => SourceParsing.String(c, "compensationType") == "Salary")
            : default;
        if (salary.ValueKind != JsonValueKind.Object)
        {
            return (null, null, null, null, summary);
        }

        return (SourceParsing.Number(salary, "minValue"), SourceParsing.Number(salary, "maxValue"),
            SourceParsing.String(salary, "currencyCode"), SourceParsing.Period(SourceParsing.String(salary, "interval")), summary);
    }
}
