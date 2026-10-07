using System.Globalization;
using JobPilot.Domain.Jobs;
using JobPilot.Domain.Pipeline;
using JobPilot.Mobile.Core.Features.Transfer;
using JobPilot.Mobile.Core.Infrastructure.Google;
using JobPilot.Mobile.Core.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JobPilot.Mobile.Core.Features.Pipeline;

/// <summary>
/// Gmail and Google Calendar actions of one application. Every action is started by the owner from the UI after seeing
/// what will be sent; nothing runs in the background and nothing is ever sent by e-mail.
/// </summary>
public sealed class ApplicationGoogleService(
    IDbContextFactory<MobileDbContext> dbFactory,
    ApplicationStore applications,
    GmailClient gmail,
    GoogleCalendarClient calendar,
    TimeProvider time)
{
    /// <summary>A Gmail search for the company: its name and the e-mail domains/addresses of the contacts.</summary>
    public static string SuggestedQuery(ApplicationDetail detail)
    {
        ArgumentNullException.ThrowIfNull(detail);
        var terms = new List<string> { $"\"{detail.Posting.Company.Replace("\"", string.Empty, StringComparison.Ordinal)}\"" };
        foreach (var email in detail.Application.Contacts.Select(c => c.Email?.Trim()).Where(e => !string.IsNullOrEmpty(e) && e.Contains('@', StringComparison.Ordinal)))
        {
            terms.Add($"from:{email} OR to:{email}");
        }

        return string.Join(" OR ", terms);
    }

    public async Task<IReadOnlyList<ApplicationMailThread>> LinkedThreadsAsync(Guid applicationId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var threads = await db.ApplicationMailThreads.AsNoTracking().Where(t => t.ApplicationId == applicationId).ToListAsync(cancellationToken);
        return threads.OrderByDescending(t => t.LinkedAtUtc).ToList();
    }

    public async Task LinkAsync(Guid applicationId, MailThreadSummary thread, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(thread);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (await db.ApplicationMailThreads.AnyAsync(t => t.ApplicationId == applicationId && t.ThreadId == thread.Id, cancellationToken))
        {
            return;
        }

        db.ApplicationMailThreads.Add(new ApplicationMailThread
        {
            ApplicationId = applicationId,
            ThreadId = thread.Id,
            Subject = thread.Subject.Length > 1000 ? thread.Subject[..1000] : thread.Subject,
            LinkedAtUtc = time.GetUtcNow().UtcDateTime,
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UnlinkAsync(Guid applicationId, long linkId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await DeletionLog.DeleteAsync(db, TransferSchema.ApplicationMailThreads, linkId,
            ct => db.ApplicationMailThreads.Where(t => t.Id == linkId && t.ApplicationId == applicationId).ExecuteDeleteAsync(ct), time.GetUtcNow().UtcDateTime, cancellationToken);
    }

    /// <summary>Records an e-mail from the company as a response in the timeline (sender, subject and date only).</summary>
    public Task RecordResponseAsync(Guid applicationId, MailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var date = message.DateUtc?.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.GetCultureInfo("es-AR"));
        return applications.RecordResponseAsync(applicationId, $"E-mail de {message.From}: {message.Subject}" + (date is null ? string.Empty : $" ({date})"), cancellationToken);
    }

    /// <summary>
    /// A reply draft for a linked thread: it answers the last message that is not the owner's own, and is addressed to its
    /// sender unless <paramref name="to"/> is given.
    /// </summary>
    /// <param name="ownEmail">The owner's address, so their own messages are not used as the recipient.</param>
    public async Task<GoogleResult<DraftRequest>> ReplyDraftAsync(string threadId, string? to, string body, string? ownEmail, CancellationToken cancellationToken)
    {
        var thread = await gmail.GetThreadAsync(threadId, cancellationToken);
        if (thread.Value is not { } value)
        {
            return GoogleResult<DraftRequest>.Fail(thread.Error!);
        }

        var replyTo = value.Messages.LastOrDefault(m => ownEmail is null || !m.From.Contains(ownEmail, StringComparison.OrdinalIgnoreCase))
            ?? value.Messages.LastOrDefault();
        var subject = value.Subject.StartsWith("Re:", StringComparison.OrdinalIgnoreCase) ? value.Subject : "Re: " + value.Subject;
        return GoogleResult<DraftRequest>.Ok(new DraftRequest(to ?? replyTo?.From, subject, body, value.Id, replyTo?.MessageIdHeader is { Length: > 0 } id ? id : null));
    }

    /// <summary>What would be created in Google Calendar for an interview (shown to the owner before creating it).</summary>
    public static CalendarEventRequest EventFor(JobPosting posting, ApplicationEvent interview)
    {
        ArgumentNullException.ThrowIfNull(posting);
        ArgumentNullException.ThrowIfNull(interview);
        var start = interview.ScheduledAtUtc ?? throw new ArgumentException("The event is not a scheduled interview.", nameof(interview));
        var link = posting.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? posting.Url : null;
        var description = string.Join("\n", new[] { interview.Text, link, "Creado con JobPilot." }.Where(s => !string.IsNullOrWhiteSpace(s)));
        return new CalendarEventRequest($"Entrevista: {posting.Title} — {posting.Company}", description, start, start.AddMinutes(interview.DurationMinutes ?? 60));
    }

    public async Task<string?> AddToCalendarAsync(ApplicationDetail detail, long eventId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(detail);
        var interview = detail.Application.Events.First(e => e.Id == eventId && e.Kind == ApplicationEventKind.InterviewScheduled);
        if (interview.ExternalId is not null)
        {
            return null;
        }

        var created = await calendar.CreateEventAsync(EventFor(detail.Posting, interview), cancellationToken);
        if (created.Value is not { } externalId)
        {
            return created.Error;
        }

        await applications.SetExternalIdAsync(detail.Application.Id, eventId, externalId, cancellationToken);
        return null;
    }

    public async Task<string?> RemoveFromCalendarAsync(Guid applicationId, ApplicationEvent interview, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(interview);
        if (interview.ExternalId is null)
        {
            return null;
        }

        var deleted = await calendar.DeleteEventAsync(interview.ExternalId, cancellationToken);
        if (!deleted.IsSuccess)
        {
            return deleted.Error;
        }

        await applications.SetExternalIdAsync(applicationId, interview.Id, null, cancellationToken);
        return null;
    }
}
