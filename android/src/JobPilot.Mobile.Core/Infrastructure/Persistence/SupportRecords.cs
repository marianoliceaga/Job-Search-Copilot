namespace JobPilot.Mobile.Core.Infrastructure.Persistence;

/// <summary>A JSON document stored by key (preferences, rules, last reviews), as in the web app.</summary>
public sealed class SettingsDocument
{
    public required string Key { get; set; }

    public required string Json { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}

public enum SourceRunStatus
{
    Succeeded,
    Failed,
    Skipped,
    Cancelled,
}

/// <summary>One attempt to read a job source; also used to enforce each source's minimum request interval.</summary>
public sealed class SourceRun
{
    public long Id { get; set; }

    public required string SourceKey { get; set; }

    public DateTime StartedAtUtc { get; set; }

    public DateTime? FinishedAtUtc { get; set; }

    public SourceRunStatus Status { get; set; }

    public int Fetched { get; set; }

    public int Matched { get; set; }

    public int Added { get; set; }

    public int Updated { get; set; }

    public int Merged { get; set; }

    public string? Message { get; set; }
}

/// <summary>A verified LLM analysis of a job posting (JSON of <c>VerifiedJobAnalysis</c>). The latest one is current.</summary>
public sealed class JobAnalysisRecord
{
    public long Id { get; set; }

    public Guid JobPostingId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public required string PromptId { get; set; }

    public required string Model { get; set; }

    public int FitScore { get; set; }

    public required string Json { get; set; }
}
