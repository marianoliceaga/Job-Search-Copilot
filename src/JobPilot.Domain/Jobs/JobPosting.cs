namespace JobPilot.Domain.Jobs;

/// <summary>
/// A job offer found in an allowed source or added manually. Third-party content: its text is untrusted data.
/// </summary>
public sealed class JobPosting
{
    public Guid Id { get; set; }

    /// <summary>Source key (e.g. <c>remotive</c>, <c>greenhouse</c>, <c>manual</c>).</summary>
    public required string SourceKey { get; set; }

    /// <summary>Source name shown for attribution (e.g. "Remotive", "Greenhouse · Current").</summary>
    public required string SourceName { get; set; }

    public string? SourceJobId { get; set; }

    public required string Title { get; set; }

    public required string Company { get; set; }

    /// <summary>Canonical link to the posting at its source (always shown, for attribution).</summary>
    public required string Url { get; set; }

    public string? ApplyUrl { get; set; }

    /// <summary>Location / remote restrictions as published.</summary>
    public string? Location { get; set; }

    public string? EmploymentType { get; set; }

    public List<string> Tags { get; set; } = [];

    /// <summary>Plain-text description (HTML removed).</summary>
    public string Description { get; set; } = string.Empty;

    public DateTime? PublishedAtUtc { get; set; }

    public DateTime FirstSeenAtUtc { get; set; }

    public DateTime LastSeenAtUtc { get; set; }

    /// <summary>Salary as published by the source in structured form; null fields mean "not informed".</summary>
    public decimal? SalaryMin { get; set; }

    public decimal? SalaryMax { get; set; }

    public string? SalaryCurrency { get; set; }

    public SalaryPeriod? SalaryPeriod { get; set; }

    /// <summary>Salary text as published (e.g. "$120k – $150k").</summary>
    public string? SalaryText { get; set; }

    /// <summary>Normalized canonical URL, unique.</summary>
    public required string UrlKey { get; set; }

    /// <summary>Normalized company + title, used to merge the same offer published in several sources.</summary>
    public required string Fingerprint { get; set; }

    /// <summary>Other sources where the same offer was seen.</summary>
    public List<JobSighting> Sightings { get; set; } = [];

    /// <summary>Hidden from the list by the owner.</summary>
    public bool IsDiscarded { get; set; }

    public bool HasPublishedSalary => SalaryMin is not null || SalaryMax is not null || !string.IsNullOrWhiteSpace(SalaryText);

    /// <summary>All the text that describes the offer, used for keyword matching and quote checks.</summary>
    public string SearchableText =>
        string.Join("\n", new[] { Title, Company, Location, EmploymentType, SalaryText, string.Join(", ", Tags), Description }
            .Where(s => !string.IsNullOrWhiteSpace(s)));
}

public sealed record JobSighting(string SourceKey, string SourceName, string Url, DateTime SeenAtUtc);

public enum SalaryPeriod
{
    Year,
    Month,
    Week,
    Day,
    Hour,
}
