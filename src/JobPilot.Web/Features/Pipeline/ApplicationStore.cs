using System.Text.Json;
using JobPilot.Domain.Jobs;
using JobPilot.Domain.Pipeline;
using JobPilot.Web.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JobPilot.Web.Features.Pipeline;

/// <summary>A Kanban card: the application, its offer and the reminder computed on load.</summary>
public sealed record PipelineCard(
    Guid Id,
    Guid PostingId,
    string Title,
    string Company,
    ApplicationStage Stage,
    DateTime StageChangedAtUtc,
    FollowUpReminder? Reminder,
    DateTime? NextInterviewUtc);

public sealed record ApplicationDetail(JobApplication Application, JobPosting Posting, FollowUpReminder? Reminder);

/// <summary>Content of a sent tailored document as it was when the owner marked it as sent.</summary>
public sealed record SentDocument(TailoredKind Kind, int MasterCvVersion, string Json);

/// <summary>Applications in the pipeline. Every change goes through the domain methods, which append timeline events.</summary>
public sealed class ApplicationStore(IDbContextFactory<JobPilotDbContext> dbFactory, PipelineSettingsStore settings, TimeProvider time)
{
    public const int MaxTextLength = 20000;

    /// <summary>Adds the offer to the pipeline, or returns the existing application.</summary>
    public async Task<Guid> StartAsync(Guid postingId, ApplicationStage stage, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var existing = await db.Applications.Where(a => a.JobPostingId == postingId).Select(a => (Guid?)a.Id).FirstOrDefaultAsync(cancellationToken);
        if (existing is { } id)
        {
            return id;
        }

        if (!await db.JobPostings.AnyAsync(p => p.Id == postingId, cancellationToken))
        {
            throw new InvalidOperationException("La oferta no existe.");
        }

        var application = JobApplication.Start(postingId, stage, Now);
        db.Applications.Add(application);
        await db.SaveChangesAsync(cancellationToken);
        return application.Id;
    }

    public async Task<Guid?> FindByPostingAsync(Guid postingId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Applications.Where(a => a.JobPostingId == postingId).Select(a => (Guid?)a.Id).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<ApplicationDetail?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var application = await WithChildren(db).AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (application is null)
        {
            return null;
        }

        var posting = await db.JobPostings.AsNoTracking().FirstAsync(p => p.Id == application.JobPostingId, cancellationToken);
        var policy = await settings.GetAsync(cancellationToken);
        return new ApplicationDetail(application, posting, FollowUpRules.Evaluate(application, policy));
    }

    /// <summary>All applications with their timelines (single user: the set is small).</summary>
    public async Task<IReadOnlyList<JobApplication>> GetAllAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await WithChildren(db).AsNoTracking().AsSplitQuery().ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PipelineCard>> GetBoardAsync(CancellationToken cancellationToken)
    {
        var applications = await GetAllAsync(cancellationToken);
        var policy = await settings.GetAsync(cancellationToken);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var ids = applications.Select(a => a.JobPostingId).ToList();
        var postings = await db.JobPostings.AsNoTracking().Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.Title, p.Company })
            .ToDictionaryAsync(p => p.Id, cancellationToken);
        var now = Now;
        return applications
            .Select(a => new PipelineCard(
                a.Id, a.JobPostingId, postings[a.JobPostingId].Title, postings[a.JobPostingId].Company, a.Stage, a.StageChangedAtUtc,
                FollowUpRules.Evaluate(a, policy),
                a.Events.Where(e => e.Kind == ApplicationEventKind.InterviewScheduled && e.ScheduledAtUtc >= now).Min(e => e.ScheduledAtUtc)))
            .OrderByDescending(c => c.StageChangedAtUtc)
            .ToList();
    }

    public Task MoveAsync(Guid id, ApplicationStage stage, string? comment, CancellationToken cancellationToken) =>
        UpdateAsync(id, (a, now) => a.MoveTo(stage, now, Limit(comment)), cancellationToken);

    public Task AddNoteAsync(Guid id, string text, CancellationToken cancellationToken) =>
        UpdateAsync(id, (a, now) => a.AddNote(Limit(text)!, now), cancellationToken);

    public Task AddContactAsync(Guid id, ApplicationContact contact, CancellationToken cancellationToken) =>
        UpdateAsync(id, (a, now) => a.AddContact(contact, now), cancellationToken);

    public Task ScheduleInterviewAsync(Guid id, DateTime startUtc, int durationMinutes, string? details, CancellationToken cancellationToken) =>
        UpdateAsync(id, (a, now) => a.ScheduleInterview(startUtc, durationMinutes, Limit(details), now), cancellationToken);

    public Task RecordFollowUpDraftAsync(Guid id, string text, CancellationToken cancellationToken) =>
        UpdateAsync(id, (a, now) => a.RecordFollowUpDraft(Limit(text)!, now), cancellationToken);

    public Task RecordFollowUpSentAsync(Guid id, string? text, CancellationToken cancellationToken) =>
        UpdateAsync(id, (a, now) => a.RecordFollowUpSent(Limit(text), now), cancellationToken);

    public Task RecordMessageSentAsync(Guid id, string text, CancellationToken cancellationToken) =>
        UpdateAsync(id, (a, now) => a.RecordMessageSent(Limit(text)!, now), cancellationToken);

    public Task RecordResponseAsync(Guid id, string? text, CancellationToken cancellationToken) =>
        UpdateAsync(id, (a, now) => a.RecordResponse(Limit(text), now), cancellationToken);

    public Task SetReminderAsync(Guid id, DateTime? atUtc, CancellationToken cancellationToken) =>
        UpdateAsync(id, (a, now) => a.SetReminder(atUtc, now), cancellationToken);

    /// <summary>Records that an approved tailored document of this offer was sent, keeping a copy of its content.</summary>
    public async Task<string?> RecordDocumentSentAsync(Guid id, long documentId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var application = await db.Applications.Include(a => a.Events).FirstAsync(a => a.Id == id, cancellationToken);
        var document = await db.TailoredDocuments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken);
        if (document is null || document.JobPostingId != application.JobPostingId)
        {
            return "El documento no pertenece a esta oferta.";
        }

        if (document.Status != TailoredStatus.Approved)
        {
            return "Solo se pueden registrar como enviados documentos aprobados.";
        }

        var label = document.Kind == TailoredKind.Cv ? "CV a medida" : "Carta de presentación";
        var snapshot = JsonSerializer.Serialize(new SentDocument(document.Kind, document.MasterCvVersion, document.Json), DomainJson.Options);
        application.RecordDocumentSent(document.Id, $"{label} (CV maestro v{document.MasterCvVersion})", snapshot, Now);
        await db.SaveChangesAsync(cancellationToken);
        return null;
    }

    public static SentDocument? ReadSnapshot(ApplicationEvent sent) =>
        sent?.DocumentSnapshot is { } json ? JsonSerializer.Deserialize<SentDocument>(json, DomainJson.Options) : null;

    /// <summary>Stores (or clears) the external calendar event id of a scheduled interview.</summary>
    public async Task SetExternalIdAsync(Guid id, long eventId, string? externalId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var item = await db.ApplicationEvents.FirstAsync(e => e.Id == eventId && e.ApplicationId == id, cancellationToken);
        item.ExternalId = externalId;
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Deletes a note or a scheduled interview. Stage changes and other records cannot be deleted.</summary>
    public async Task DeleteEventAsync(Guid id, long eventId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var item = await db.ApplicationEvents.FirstOrDefaultAsync(e => e.Id == eventId && e.ApplicationId == id, cancellationToken);
        if (item is null)
        {
            return;
        }

        if (!item.IsDeletable)
        {
            throw new InvalidOperationException("Solo se pueden borrar notas y entrevistas agendadas.");
        }

        db.ApplicationEvents.Remove(item);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateContactAsync(Guid id, ApplicationContact contact, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contact);
        if (string.IsNullOrWhiteSpace(contact.Name))
        {
            throw new ArgumentException("El nombre es obligatorio.", nameof(contact));
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var stored = await db.ApplicationContacts.FirstAsync(c => c.Id == contact.Id && c.ApplicationId == id, cancellationToken);
        stored.Name = contact.Name.Trim();
        stored.Role = contact.Role?.Trim();
        stored.Email = contact.Email?.Trim();
        stored.ProfileUrl = contact.ProfileUrl?.Trim();
        stored.Notes = contact.Notes?.Trim();
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteContactAsync(Guid id, long contactId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await db.ApplicationContacts.Where(c => c.Id == contactId && c.ApplicationId == id).ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>Removes the application and its timeline from the pipeline (the offer is kept).</summary>
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await db.Applications.Where(a => a.Id == id).ExecuteDeleteAsync(cancellationToken);
    }

    private async Task UpdateAsync(Guid id, Action<JobApplication, DateTime> change, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var application = await WithChildren(db).AsSplitQuery().FirstAsync(a => a.Id == id, cancellationToken);
        change(application, Now);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static IQueryable<JobApplication> WithChildren(JobPilotDbContext db) =>
        db.Applications.Include(a => a.Events).Include(a => a.Contacts);

    private static string? Limit(string? text) => text is { Length: > MaxTextLength } ? text[..MaxTextLength] : text;

    private DateTime Now => time.GetUtcNow().UtcDateTime;
}

/// <summary>Follow-up rule edited in the UI; defaults come from <see cref="FollowUpPolicy"/>.</summary>
public sealed class PipelineSettingsStore(SettingsDocumentStore documents)
{
    private const string Key = "pipeline-follow-up";

    public async Task<FollowUpPolicy> GetAsync(CancellationToken cancellationToken) =>
        await documents.GetAsync<FollowUpPolicy>(Key, cancellationToken) ?? new FollowUpPolicy();

    public async Task<IReadOnlyList<string>> SaveAsync(FollowUpPolicy policy, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(policy);
        var problems = policy.Problems();
        if (problems.Count == 0)
        {
            await documents.SetAsync(Key, policy, cancellationToken);
        }

        return problems;
    }
}
