using JobPilot.Domain.Jobs;

namespace JobPilot.Mobile.Core.Features.Jobs.Sources;

/// <summary>An offer as read from a source, before deduplication and storage.</summary>
public sealed record SourcedJob
{
    public required string SourceKey { get; init; }

    public required string SourceName { get; init; }

    public string? SourceJobId { get; init; }

    public required string Title { get; init; }

    public required string Company { get; init; }

    public required string Url { get; init; }

    public string? ApplyUrl { get; init; }

    public string? Location { get; init; }

    public string? EmploymentType { get; init; }

    public IReadOnlyList<string> Tags { get; init; } = [];

    public string Description { get; init; } = string.Empty;

    public DateTime? PublishedAtUtc { get; init; }

    public decimal? SalaryMin { get; init; }

    public decimal? SalaryMax { get; init; }

    public string? SalaryCurrency { get; init; }

    public SalaryPeriod? SalaryPeriod { get; init; }

    public string? SalaryText { get; init; }
}

public sealed record SourceFetchContext(IReadOnlyList<FollowedCompany> FollowedCompanies, IReadOnlyList<string> Keywords);

/// <param name="Warnings">Non-fatal problems (e.g. a followed board that no longer exists).</param>
public sealed record SourceFetchResult(IReadOnlyList<SourcedJob> Jobs, IReadOnlyList<string> Warnings);

/// <summary>
/// A job source with a public API, RSS or feed whose terms allow this use. Implementations must keep the
/// canonical link and source name (attribution) and respect the configured minimum interval between requests.
/// </summary>
public interface IJobSource
{
    string Key { get; }

    string DisplayName { get; }

    /// <summary>Short description of the source's terms, shown in the UI.</summary>
    string TermsSummary { get; }

    Task<SourceFetchResult> FetchAsync(SourceFetchContext context, CancellationToken cancellationToken);
}

public sealed class JobSourceOptions
{
    public const string SectionName = "JobSources";

    public string UserAgent { get; set; } = "JobPilot/1.0 (personal job search)";

    public Dictionary<string, JobSourceSettings> Sources { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public JobSourceSettings For(string key) =>
        Sources.TryGetValue(key, out var settings) ? settings : throw new InvalidOperationException($"Missing configuration JobSources:Sources:{key}.");
}

public sealed class JobSourceSettings
{
    /// <summary>Endpoint(s). ATS sources use a <c>{board}</c> placeholder.</summary>
    public List<string> Urls { get; set; } = [];

    /// <summary>Minimum time between two requests to this source (respecting its terms).</summary>
    public int MinIntervalMinutes { get; set; } = 60;

    /// <summary>Maximum keywords used as separate queries, for sources searched by keyword.</summary>
    public int MaxKeywordQueries { get; set; } = 3;
}
