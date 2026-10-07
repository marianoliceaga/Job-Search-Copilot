using JobPilot.Domain.Mail;

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

/// <summary>Likely interview questions generated for one offer (JSON of the question list). The latest set is current.</summary>
public sealed class InterviewQuestionSetRecord
{
    public long Id { get; set; }

    public Guid JobPostingId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public required string PromptId { get; set; }

    public required string Model { get; set; }

    public required string Json { get; set; }
}

/// <summary>A STAR story built from the master CV (JSON of the domain story), optionally prepared for one offer.</summary>
public sealed class StarStoryRecord
{
    public long Id { get; set; }

    public Guid? JobPostingId { get; set; }

    public int MasterCvVersion { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public required string PromptId { get; set; }

    public required string Model { get; set; }

    public required string Json { get; set; }
}

/// <summary>A Gmail thread the owner linked to an application. Only the id and subject are stored; messages are read on demand.</summary>
public sealed class ApplicationMailThread
{
    public long Id { get; set; }

    public Guid ApplicationId { get; set; }

    public required string ThreadId { get; set; }

    public required string Subject { get; set; }

    public DateTime LinkedAtUtc { get; set; }
}

/// <summary>
/// One "Actualizar correo" run: counts, filter chips, warnings and the alert roles that became offers. Facts only: no
/// message bodies are stored.
/// </summary>
public sealed class MailCheckRecord
{
    public long Id { get; set; }

    public DateTime StartedAtUtc { get; set; }

    public DateTime FinishedAtUtc { get; set; }

    /// <summary>Start of the inbox window that was read.</summary>
    public DateTime WindowSinceUtc { get; set; }

    public bool Succeeded { get; set; }

    public string? Error { get; set; }

    public int ThreadsRead { get; set; }

    public int Waiting { get; set; }

    public int AlertThreads { get; set; }

    public int Filtered { get; set; }

    public int Listings { get; set; }

    public int OffersAdded { get; set; }

    public List<FilterChip> FilteredChips { get; set; } = [];

    public List<FilterChip> RoleFilters { get; set; } = [];

    public List<string> Warnings { get; set; } = [];

    public List<MailRoleSummary> Roles { get; set; } = [];
}

/// <summary>An alert role shown in the brief and the offer it became.</summary>
public sealed record MailRoleSummary(
    Guid PostingId,
    string Title,
    string Company,
    string Location,
    string Workplace,
    RoleFit Fit,
    int Score,
    IReadOnlyList<string> Why,
    decimal? PayMonthlyUsd,
    int Listings,
    bool IsNew);

/// <summary>
/// A "Te esperan" card, one per Gmail thread, refreshed by every check that reads the thread. "Listo" hides it until a new
/// message arrives in the thread.
/// </summary>
public sealed class MailWaitingRecord
{
    public long Id { get; set; }

    public required string ThreadId { get; set; }

    public required string LastMessageId { get; set; }

    public long LastCheckId { get; set; }

    public required string RuleId { get; set; }

    public required string Pill { get; set; }

    public MailTone Tone { get; set; }

    public required string Subject { get; set; }

    public required string SenderName { get; set; }

    public required string SenderAddress { get; set; }

    public required string Timeline { get; set; }

    public required string NextAction { get; set; }

    public DateTime LastReceivedUtc { get; set; }

    public DateTime RailUtc { get; set; }

    public required string RailNote { get; set; }

    public DateTime FirstSeenUtc { get; set; }

    /// <summary>Set by "Listo"; cleared when a newer message arrives in the thread.</summary>
    public DateTime? DoneAtUtc { get; set; }
}
