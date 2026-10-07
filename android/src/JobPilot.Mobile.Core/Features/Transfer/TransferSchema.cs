namespace JobPilot.Mobile.Core.Features.Transfer;

/// <summary>How a table's primary key is assigned.</summary>
public enum TransferKeyKind
{
    /// <summary>A GUID kept across devices (a new one only if it collides with an unrelated row).</summary>
    Guid,

    /// <summary>An integer assigned by each database; imported rows get the next free number.</summary>
    AutoIncrement,

    /// <summary>The key is the identity itself (settings keys, use case names).</summary>
    Natural,
}

/// <summary>What happens when a row of the file matches a row the device already has.</summary>
public enum TransferPolicy
{
    /// <summary>Immutable records (logs, analyses, CV versions): the device keeps its row.</summary>
    KeepExisting,

    /// <summary>Editable rows without a modification date: the file's row wins (owner's choice), except sticky columns.</summary>
    FileWins,

    /// <summary>The row with the later <see cref="TransferTable.NewerColumn"/> wins.</summary>
    Newer,

    /// <summary>Job postings: the file wins, a pasted description is never lost, sightings are combined.</summary>
    Posting,

    /// <summary>Applications: stage and reminder recomputed from the combined timeline.</summary>
    Application,

    /// <summary>Practice sessions: a completed session wins, otherwise the one with more turns.</summary>
    Practice,

    /// <summary>Mail checks: only checks newer than the device's last one are added (the Panel shows the latest).</summary>
    MailCheck,

    /// <summary>"Te esperan" cards: the latest message wins; "Listo" on either device is kept.</summary>
    MailWaiting,
}

/// <summary>How a foreign key column is translated to the receiving device's keys.</summary>
public enum TransferReferenceKind
{
    /// <summary>The row cannot exist without the referenced row: it is skipped when the reference is unknown.</summary>
    Required,

    /// <summary>Unknown references become null (as when the referenced row is deleted locally).</summary>
    Optional,

    /// <summary>A plain number (no constraint): unknown references keep the device's value, or 0.</summary>
    Value,
}

/// <param name="Column">Foreign key column of this table.</param>
/// <param name="Table">Referenced table; <see cref="TransferSchema.CvVersionReference"/> means master CV version numbers.</param>
public sealed record TransferReference(string Column, string Table, TransferReferenceKind Kind);

/// <param name="Name">Table name (the same in both apps' databases).</param>
/// <param name="Label">Spanish label for the import summary.</param>
/// <param name="Columns">Every column, in the order written to the file.</param>
/// <param name="Identity">Columns that identify the same row on both devices; empty = the table has a single row.</param>
/// <param name="ChangedColumns">Last-change dates (the latest non-null one counts): a deletion only removes rows not changed after it.</param>
/// <param name="StickyColumns">With <see cref="TransferPolicy.FileWins"/>, values that are never cleared by the file (e.g. "sent").</param>
public sealed record TransferTable(
    string Name,
    string Label,
    string Key,
    TransferKeyKind KeyKind,
    IReadOnlyList<string> Columns,
    IReadOnlyList<string> Identity,
    TransferPolicy Policy,
    string? NewerColumn = null,
    IReadOnlyList<TransferReference>? References = null,
    IReadOnlyList<string>? ChangedColumns = null,
    IReadOnlyList<string>? StickyColumns = null)
{
    public IReadOnlyList<TransferReference> Refs => References ?? [];

    /// <summary>Whether owner deletions of this table are recorded and travel in the file.</summary>
    public bool TracksDeletions => ChangedColumns is { Count: > 0 };
}

/// <summary>
/// The tables in a transfer file, in dependency order (parents first). Both apps map the same tables and columns; tests in each
/// app compare this list with its EF Core model, so a schema change without updating it (and <see cref="Version"/>) fails.
/// </summary>
public static class TransferSchema
{
    /// <summary>Bumped whenever a table or column changes; files of another version are refused.</summary>
    public const int Version = 1;

    /// <summary>Pseudo-table for references to master CV version numbers.</summary>
    public const string CvVersionReference = "#MasterCvVersion";

    public const string Profiles = "Profiles";
    public const string MasterCvVersions = "MasterCvVersions";
    public const string MasterCvs = "MasterCvs";
    public const string SettingsDocuments = "SettingsDocuments";
    public const string LlmUseCaseSettings = "LlmUseCaseSettings";
    public const string LlmCalls = "LlmCalls";
    public const string SourceRuns = "SourceRuns";
    public const string FollowedCompanies = "FollowedCompanies";
    public const string JobPostings = "JobPostings";
    public const string Companies = "Companies";
    public const string JobAnalyses = "JobAnalyses";
    public const string TailoredDocuments = "TailoredDocuments";
    public const string Applications = "Applications";
    public const string ApplicationEvents = "ApplicationEvents";
    public const string ApplicationContacts = "ApplicationContacts";
    public const string ApplicationMailThreads = "ApplicationMailThreads";
    public const string CompensationOffers = "CompensationOffers";
    public const string InterviewQuestionSets = "InterviewQuestionSets";
    public const string StarStories = "StarStories";
    public const string PracticeSessions = "PracticeSessions";
    public const string OutreachMessages = "OutreachMessages";
    public const string MailChecks = "MailChecks";
    public const string MailWaiting = "MailWaiting";
    public const string DeletedRecords = "DeletedRecords";

    private static readonly TransferReference PostingRequired = new("JobPostingId", JobPostings, TransferReferenceKind.Required);
    private static readonly TransferReference PostingOptional = new("JobPostingId", JobPostings, TransferReferenceKind.Optional);
    private static readonly TransferReference ApplicationRequired = new("ApplicationId", Applications, TransferReferenceKind.Required);

    public static IReadOnlyList<TransferTable> Tables { get; } =
    [
        new(Profiles, "Perfil", "Id", TransferKeyKind.AutoIncrement,
            ["Id", "ApplicationEmphases", "ApplicationLanguage", "Domains", "Headline", "Priorities", "Roles", "Stack", "UpdatedAtUtc", "WorkFromCountry",
             "WorkModality", "YearsOfExperience"],
            [], TransferPolicy.Newer, NewerColumn: "UpdatedAtUtc"),
        new(MasterCvVersions, "Versiones del CV maestro", "Id", TransferKeyKind.AutoIncrement,
            ["Id", "CreatedAtUtc", "Json", "Source", "Version"],
            ["CreatedAtUtc"], TransferPolicy.KeepExisting),
        new(MasterCvs, "CV maestro", "Id", TransferKeyKind.AutoIncrement,
            ["Id", "Json", "UpdatedAtUtc", "Version"],
            [], TransferPolicy.Newer, NewerColumn: "UpdatedAtUtc",
            References: [new("Version", CvVersionReference, TransferReferenceKind.Value)]),
        new(SettingsDocuments, "Preferencias, reglas y ajustes", "Key", TransferKeyKind.Natural,
            ["Key", "Json", "UpdatedAtUtc"],
            ["Key"], TransferPolicy.Newer, NewerColumn: "UpdatedAtUtc"),
        new(LlmUseCaseSettings, "Modelos por uso", "UseCase", TransferKeyKind.Natural,
            ["UseCase", "Model", "Temperature", "ThinkingLevel", "UpdatedAtUtc"],
            ["UseCase"], TransferPolicy.Newer, NewerColumn: "UpdatedAtUtc", ChangedColumns: ["UpdatedAtUtc"]),
        new(LlmCalls, "Llamadas a Gemini", "Id", TransferKeyKind.AutoIncrement,
            ["Id", "Attempt", "CachedTokens", "ErrorKind", "ErrorMessage", "EstimatedCostUsd", "FinishReason", "LatencyMs", "Model", "Outcome",
             "OutputTokens", "PromptId", "PromptTokens", "ResponseId", "StartedAtUtc", "ThoughtsTokens", "ToolUsePromptTokens", "UseCase"],
            ["StartedAtUtc", "UseCase", "Attempt"], TransferPolicy.KeepExisting),
        new(SourceRuns, "Búsquedas en fuentes", "Id", TransferKeyKind.AutoIncrement,
            ["Id", "Added", "Fetched", "FinishedAtUtc", "Matched", "Merged", "Message", "SourceKey", "StartedAtUtc", "Status", "Updated"],
            ["SourceKey", "StartedAtUtc"], TransferPolicy.KeepExisting),
        new(FollowedCompanies, "Empresas seguidas", "Id", TransferKeyKind.AutoIncrement,
            ["Id", "AddedAtUtc", "Ats", "BoardToken", "Name", "Notes"],
            ["Ats", "BoardToken"], TransferPolicy.FileWins, ChangedColumns: ["AddedAtUtc"]),
        new(JobPostings, "Ofertas", "Id", TransferKeyKind.Guid,
            ["Id", "ApplyUrl", "Company", "Description", "EmploymentType", "Fingerprint", "FirstSeenAtUtc", "IsDiscarded", "LastSeenAtUtc",
             "Location", "PublishedAtUtc", "SalaryCurrency", "SalaryMax", "SalaryMin", "SalaryPeriod", "SalaryText", "Sightings", "SourceJobId",
             "SourceKey", "SourceName", "Tags", "Title", "Url", "UrlKey"],
            ["UrlKey"], TransferPolicy.Posting),
        new(Companies, "Empresas", "Id", TransferKeyKind.Guid,
            ["Id", "CreatedAtUtc", "Facts", "Gaps", "Model", "Name", "NameKey", "Notes", "PromptId", "ResearchedAtUtc", "Sources", "UpdatedAtUtc",
             "Website"],
            ["NameKey"], TransferPolicy.Newer, NewerColumn: "UpdatedAtUtc", ChangedColumns: ["UpdatedAtUtc"]),
        new(JobAnalyses, "Análisis de ofertas", "Id", TransferKeyKind.AutoIncrement,
            ["Id", "CreatedAtUtc", "FitScore", "JobPostingId", "Json", "Model", "PromptId"],
            ["JobPostingId", "CreatedAtUtc"], TransferPolicy.KeepExisting, References: [PostingRequired]),
        new(TailoredDocuments, "CV y cartas a medida", "Id", TransferKeyKind.AutoIncrement,
            ["Id", "ApprovedAtUtc", "CreatedAtUtc", "JobPostingId", "Json", "Kind", "MasterCvVersion", "Model", "Notes", "PromptId", "Status",
             "UpdatedAtUtc"],
            ["JobPostingId", "Kind", "CreatedAtUtc"], TransferPolicy.Newer, NewerColumn: "UpdatedAtUtc",
            References: [PostingRequired, new("MasterCvVersion", CvVersionReference, TransferReferenceKind.Value)],
            ChangedColumns: ["UpdatedAtUtc"]),
        new(Applications, "Postulaciones", "Id", TransferKeyKind.Guid,
            ["Id", "CreatedAtUtc", "JobPostingId", "ReminderAtUtc", "Stage", "StageChangedAtUtc"],
            ["JobPostingId"], TransferPolicy.Application, References: [PostingRequired], ChangedColumns: ["StageChangedAtUtc"]),
        new(ApplicationEvents, "Línea de tiempo de postulaciones", "Id", TransferKeyKind.AutoIncrement,
            ["Id", "ApplicationId", "DocumentId", "DocumentSnapshot", "DurationMinutes", "ExternalId", "FromStage", "Kind", "OccurredAtUtc",
             "ScheduledAtUtc", "Text", "ToStage"],
            ["ApplicationId", "Kind", "OccurredAtUtc"], TransferPolicy.FileWins,
            References: [ApplicationRequired, new("DocumentId", TailoredDocuments, TransferReferenceKind.Optional)],
            ChangedColumns: ["OccurredAtUtc"]),
        new(ApplicationContacts, "Contactos de postulaciones", "Id", TransferKeyKind.AutoIncrement,
            ["Id", "ApplicationId", "CreatedAtUtc", "Email", "Name", "Notes", "ProfileUrl", "Role"],
            ["ApplicationId", "CreatedAtUtc"], TransferPolicy.FileWins, References: [ApplicationRequired], ChangedColumns: ["CreatedAtUtc"]),
        new(ApplicationMailThreads, "Hilos de correo vinculados", "Id", TransferKeyKind.AutoIncrement,
            ["Id", "ApplicationId", "LinkedAtUtc", "Subject", "ThreadId"],
            ["ApplicationId", "ThreadId"], TransferPolicy.KeepExisting, References: [ApplicationRequired], ChangedColumns: ["LinkedAtUtc"]),
        new(CompensationOffers, "Ofertas del comparador", "Id", TransferKeyKind.AutoIncrement,
            ["Id", "AnnualBenefits", "AnnualBonus", "AnnualEquity", "BaseAmount", "Company", "CreatedAtUtc", "Currency", "HoursPerMonth",
             "JobPostingId", "Model", "Notes", "PaidTimeOffDays", "Period", "Role"],
            ["CreatedAtUtc"], TransferPolicy.FileWins, References: [PostingOptional], ChangedColumns: ["CreatedAtUtc"]),
        new(InterviewQuestionSets, "Preguntas de entrevista", "Id", TransferKeyKind.AutoIncrement,
            ["Id", "CreatedAtUtc", "JobPostingId", "Json", "Model", "PromptId"],
            ["JobPostingId", "CreatedAtUtc"], TransferPolicy.KeepExisting, References: [PostingRequired]),
        new(StarStories, "Historias STAR", "Id", TransferKeyKind.AutoIncrement,
            ["Id", "CreatedAtUtc", "JobPostingId", "Json", "MasterCvVersion", "Model", "PromptId", "UpdatedAtUtc"],
            ["CreatedAtUtc"], TransferPolicy.Newer, NewerColumn: "UpdatedAtUtc",
            References: [PostingOptional, new("MasterCvVersion", CvVersionReference, TransferReferenceKind.Value)],
            ChangedColumns: ["UpdatedAtUtc"]),
        new(PracticeSessions, "Simulacros", "Id", TransferKeyKind.Guid,
            ["Id", "CompletedAtUtc", "Feedback", "Focus", "JobPostingId", "MaxFollowUps", "Mode", "Model", "OfferId", "OpeningMessage",
             "OverallScore", "PlannedQuestions", "PromptId", "StartedAtUtc", "Status", "SystemInstruction", "Turns"],
            ["Id"], TransferPolicy.Practice,
            References: [PostingOptional, new("OfferId", CompensationOffers, TransferReferenceKind.Optional)],
            ChangedColumns: ["StartedAtUtc", "CompletedAtUtc"]),
        new(OutreachMessages, "Mensajes de outreach", "Id", TransferKeyKind.AutoIncrement,
            ["Id", "Channel", "CompanyId", "CreatedAtUtc", "JobPostingId", "Json", "Length", "Model", "PromptId", "RecipientName", "RecipientRole",
             "SentAtUtc", "SentText"],
            ["CreatedAtUtc", "Channel"], TransferPolicy.FileWins,
            References: [new("CompanyId", Companies, TransferReferenceKind.Optional), PostingOptional],
            StickyColumns: ["SentAtUtc", "SentText"]),
        new(MailChecks, "Revisiones de correo", "Id", TransferKeyKind.AutoIncrement,
            ["Id", "AlertThreads", "Error", "Filtered", "FilteredChips", "FinishedAtUtc", "Listings", "OffersAdded", "RoleFilters", "Roles",
             "StartedAtUtc", "Succeeded", "ThreadsRead", "Waiting", "Warnings", "WindowSinceUtc"],
            ["StartedAtUtc"], TransferPolicy.MailCheck),
        new(MailWaiting, "Correos que te esperan", "Id", TransferKeyKind.AutoIncrement,
            ["Id", "DoneAtUtc", "FirstSeenUtc", "LastCheckId", "LastMessageId", "LastReceivedUtc", "NextAction", "Pill", "RailNote", "RailUtc",
             "RuleId", "SenderAddress", "SenderName", "Subject", "ThreadId", "Timeline", "Tone"],
            ["ThreadId"], TransferPolicy.MailWaiting, References: [new("LastCheckId", MailChecks, TransferReferenceKind.Value)]),
        new(DeletedRecords, "Registro de borrados", "Id", TransferKeyKind.AutoIncrement,
            ["Id", "DeletedAtUtc", "Identity", "TableName"],
            ["TableName", "Identity", "DeletedAtUtc"], TransferPolicy.KeepExisting),
    ];

    public static TransferTable Get(string name) =>
        Tables.FirstOrDefault(t => t.Name == name) ?? throw new ArgumentException($"Unknown transfer table '{name}'.", nameof(name));
}
