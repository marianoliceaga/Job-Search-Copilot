namespace JobPilot.Domain.Pipeline;

/// <summary>Kanban columns, in board order.</summary>
public enum ApplicationStage
{
    Detected,
    Interested,
    Applied,
    Interview,
    Offer,
    Rejected,
    Discarded,
}

public enum ApplicationEventKind
{
    Created,
    StageChanged,
    Note,
    ContactAdded,
    DocumentSent,
    InterviewScheduled,
    FollowUpDrafted,
    FollowUpSent,
    ResponseReceived,
    ReminderSet,
}

public static class ApplicationStageRules
{
    /// <summary>Closed applications get no reminders.</summary>
    public static bool IsClosed(this ApplicationStage stage) => stage is ApplicationStage.Rejected or ApplicationStage.Discarded;

    /// <summary>Stages that imply the owner applied (an interview or offer also counts when "Postulada" was skipped).</summary>
    public static bool ImpliesApplied(this ApplicationStage stage) => stage is ApplicationStage.Applied or ApplicationStage.Interview or ApplicationStage.Offer;

    /// <summary>Stages that mean the company answered an application.</summary>
    public static bool IsResponse(this ApplicationStage stage) => stage is ApplicationStage.Interview or ApplicationStage.Offer or ApplicationStage.Rejected;
}

/// <summary>One entry of an application's timeline. Stage changes are always recorded here.</summary>
public sealed class ApplicationEvent
{
    public long Id { get; set; }

    public Guid ApplicationId { get; set; }

    public DateTime OccurredAtUtc { get; set; }

    public ApplicationEventKind Kind { get; set; }

    public ApplicationStage? FromStage { get; set; }

    public ApplicationStage? ToStage { get; set; }

    /// <summary>Note, follow-up text, contact or document description.</summary>
    public string? Text { get; set; }

    /// <summary>Tailored document that was sent (<see cref="ApplicationEventKind.DocumentSent"/>).</summary>
    public long? DocumentId { get; set; }

    /// <summary>Exact content of the sent document at the time it was sent (JSON), so later edits do not rewrite history.</summary>
    public string? DocumentSnapshot { get; set; }

    /// <summary>Interview start (<see cref="ApplicationEventKind.InterviewScheduled"/>) or reminder date (<see cref="ApplicationEventKind.ReminderSet"/>).</summary>
    public DateTime? ScheduledAtUtc { get; set; }

    public int? DurationMinutes { get; set; }

    /// <summary>Events the owner may delete: their own notes and scheduled interviews. Stage changes are permanent.</summary>
    public bool IsDeletable => Kind is ApplicationEventKind.Note or ApplicationEventKind.InterviewScheduled;
}

/// <summary>A person related to an application (recruiter, hiring manager...). Entered by the owner; stays local.</summary>
public sealed class ApplicationContact
{
    public long Id { get; set; }

    public Guid ApplicationId { get; set; }

    public required string Name { get; set; }

    public string? Role { get; set; }

    public string? Email { get; set; }

    public string? ProfileUrl { get; set; }

    public string? Notes { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>An offer followed in the pipeline. Every change goes through a method that appends a timeline event.</summary>
public sealed class JobApplication
{
    public Guid Id { get; set; }

    public Guid JobPostingId { get; set; }

    public ApplicationStage Stage { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime StageChangedAtUtc { get; set; }

    /// <summary>Owner-chosen reminder date; replaces the follow-up rule until a follow-up or a response is recorded.</summary>
    public DateTime? ReminderAtUtc { get; set; }

    public List<ApplicationEvent> Events { get; set; } = [];

    public List<ApplicationContact> Contacts { get; set; } = [];

    public static JobApplication Start(Guid postingId, ApplicationStage stage, DateTime nowUtc)
    {
        var application = new JobApplication
        {
            Id = Guid.NewGuid(),
            JobPostingId = postingId,
            Stage = stage,
            CreatedAtUtc = nowUtc,
            StageChangedAtUtc = nowUtc,
        };
        application.Append(new ApplicationEvent { Kind = ApplicationEventKind.Created, ToStage = stage, OccurredAtUtc = nowUtc });
        return application;
    }

    /// <returns>The timeline event, or null when the application is already in that stage.</returns>
    public ApplicationEvent? MoveTo(ApplicationStage stage, DateTime nowUtc, string? comment = null)
    {
        if (stage == Stage)
        {
            return null;
        }

        var change = Append(new ApplicationEvent
        {
            Kind = ApplicationEventKind.StageChanged,
            FromStage = Stage,
            ToStage = stage,
            Text = Clean(comment),
            OccurredAtUtc = nowUtc,
        });
        Stage = stage;
        StageChangedAtUtc = nowUtc;
        return change;
    }

    public ApplicationEvent AddNote(string text, DateTime nowUtc) =>
        Append(new ApplicationEvent { Kind = ApplicationEventKind.Note, Text = Required(text, nameof(text)), OccurredAtUtc = nowUtc });

    public ApplicationEvent AddContact(ApplicationContact contact, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(contact);
        contact.Name = Required(contact.Name, nameof(contact));
        contact.ApplicationId = Id;
        contact.CreatedAtUtc = nowUtc;
        Contacts.Add(contact);
        var label = string.IsNullOrWhiteSpace(contact.Role) ? contact.Name : $"{contact.Name} ({contact.Role.Trim()})";
        return Append(new ApplicationEvent { Kind = ApplicationEventKind.ContactAdded, Text = label, OccurredAtUtc = nowUtc });
    }

    /// <param name="description">What was sent, e.g. "CV a medida (CV maestro v3)".</param>
    /// <param name="snapshot">The document content as it was sent.</param>
    public ApplicationEvent RecordDocumentSent(long documentId, string description, string snapshot, DateTime nowUtc) =>
        Append(new ApplicationEvent
        {
            Kind = ApplicationEventKind.DocumentSent,
            DocumentId = documentId,
            Text = Required(description, nameof(description)),
            DocumentSnapshot = Required(snapshot, nameof(snapshot)),
            OccurredAtUtc = nowUtc,
        });

    public ApplicationEvent ScheduleInterview(DateTime startUtc, int durationMinutes, string? details, DateTime nowUtc)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(durationMinutes, 1);
        return Append(new ApplicationEvent
        {
            Kind = ApplicationEventKind.InterviewScheduled,
            ScheduledAtUtc = startUtc,
            DurationMinutes = durationMinutes,
            Text = Clean(details),
            OccurredAtUtc = nowUtc,
        });
    }

    public ApplicationEvent RecordFollowUpDraft(string text, DateTime nowUtc) =>
        Append(new ApplicationEvent { Kind = ApplicationEventKind.FollowUpDrafted, Text = Required(text, nameof(text)), OccurredAtUtc = nowUtc });

    /// <summary>The owner sent a follow-up themselves (the app never sends). Restarts the follow-up clock.</summary>
    public ApplicationEvent RecordFollowUpSent(string? text, DateTime nowUtc)
    {
        ReminderAtUtc = null;
        return Append(new ApplicationEvent { Kind = ApplicationEventKind.FollowUpSent, Text = Clean(text), OccurredAtUtc = nowUtc });
    }

    /// <summary>The company answered without a stage change (e.g. "we'll get back to you next week").</summary>
    public ApplicationEvent RecordResponse(string? text, DateTime nowUtc)
    {
        ReminderAtUtc = null;
        return Append(new ApplicationEvent { Kind = ApplicationEventKind.ResponseReceived, Text = Clean(text), OccurredAtUtc = nowUtc });
    }

    /// <param name="atUtc">Null removes the custom reminder (the follow-up rule applies again).</param>
    public ApplicationEvent SetReminder(DateTime? atUtc, DateTime nowUtc)
    {
        ReminderAtUtc = atUtc;
        return Append(new ApplicationEvent { Kind = ApplicationEventKind.ReminderSet, ScheduledAtUtc = atUtc, OccurredAtUtc = nowUtc });
    }

    /// <summary>Timeline in chronological order.</summary>
    public IEnumerable<ApplicationEvent> Timeline() => Events.OrderBy(e => e.OccurredAtUtc).ThenBy(e => e.Id);

    /// <summary>Stages visited, in order, with entry and exit times (exit is null for the current stage).</summary>
    public IReadOnlyList<StageVisit> StageVisits()
    {
        var entries = Timeline()
            .Where(e => e.Kind is ApplicationEventKind.Created or ApplicationEventKind.StageChanged && e.ToStage is not null)
            .Select(e => (Stage: e.ToStage!.Value, At: e.OccurredAtUtc))
            .ToList();
        if (entries.Count == 0)
        {
            entries.Add((Stage, CreatedAtUtc));
        }

        return entries.Select((entry, i) => new StageVisit(entry.Stage, entry.At, i + 1 < entries.Count ? entries[i + 1].At : null)).ToList();
    }

    private ApplicationEvent Append(ApplicationEvent applicationEvent)
    {
        applicationEvent.ApplicationId = Id;
        Events.Add(applicationEvent);
        return applicationEvent;
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static string Required(string? text, string name) =>
        string.IsNullOrWhiteSpace(text) ? throw new ArgumentException("The text is required.", name) : text.Trim();
}

/// <param name="LeftAtUtc">Null while the application is still in this stage.</param>
public sealed record StageVisit(ApplicationStage Stage, DateTime EnteredAtUtc, DateTime? LeftAtUtc);
