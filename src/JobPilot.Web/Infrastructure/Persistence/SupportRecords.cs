namespace JobPilot.Web.Infrastructure.Persistence;

/// <summary>A JSON document stored by key (preferences, one-time seeding flags).</summary>
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

public enum TailoredKind
{
    Cv,
    CoverLetter,
}

public enum TailoredStatus
{
    Draft,
    Approved,
}

/// <summary>A tailored CV or cover letter for one offer (JSON of the domain document), tied to a master CV version.</summary>
public sealed class TailoredDocumentRecord
{
    public long Id { get; set; }

    public Guid JobPostingId { get; set; }

    public TailoredKind Kind { get; set; }

    /// <summary>Master CV version the content was generated from (sources are validated against it).</summary>
    public int MasterCvVersion { get; set; }

    public TailoredStatus Status { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public DateTime? ApprovedAtUtc { get; set; }

    public required string PromptId { get; set; }

    public required string Model { get; set; }

    /// <summary>Model's explanation of what it prioritised (Spanish).</summary>
    public string? Notes { get; set; }

    public required string Json { get; set; }
}

public enum OutreachChannel
{
    LinkedInConnection,
    LinkedInMessage,
    Email,
}

public enum OutreachLength
{
    Short,
    Medium,
}

/// <summary>Outreach drafts generated for a recruiter or hiring manager (JSON of the variants). The owner sends them.</summary>
public sealed class OutreachRecord
{
    public long Id { get; set; }

    public Guid? CompanyId { get; set; }

    public Guid? JobPostingId { get; set; }

    public OutreachChannel Channel { get; set; }

    public OutreachLength Length { get; set; }

    public string? RecipientName { get; set; }

    public string? RecipientRole { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public required string PromptId { get; set; }

    public required string Model { get; set; }

    public required string Json { get; set; }

    /// <summary>The text the owner says they sent (after editing), recorded when they mark it as sent.</summary>
    public string? SentText { get; set; }

    public DateTime? SentAtUtc { get; set; }
}
