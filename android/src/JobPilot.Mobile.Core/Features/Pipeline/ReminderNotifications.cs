using System.Globalization;
using JobPilot.Domain.Pipeline;
using JobPilot.Mobile.Core.Infrastructure.Notifications;
using JobPilot.Mobile.Core.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JobPilot.Mobile.Core.Features.Pipeline;

/// <summary>
/// Android notifications for follow-up reminders and interviews (agreed with the owner for phase A5). Off until the owner
/// turns them on in Configuración, which asks for Android's notification permission.
/// </summary>
/// <param name="ReminderHour">Local time of day for follow-up and custom reminders, on the day they are due.</param>
/// <param name="InterviewLeadMinutes">How long before an interview starts.</param>
public sealed record ReminderNotificationSettings(bool Enabled = false, int ReminderHour = 9, int ReminderMinute = 0, int InterviewLeadMinutes = 60)
{
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        if (ReminderHour is < 0 or > 23 || ReminderMinute is < 0 or > 59)
        {
            problems.Add("La hora de los recordatorios no es válida.");
        }

        if (InterviewLeadMinutes is < 5 or > 1440)
        {
            problems.Add("El aviso de entrevista tiene que ser entre 5 minutos y 24 horas antes.");
        }

        return problems;
    }
}

public sealed class ReminderNotificationSettingsStore(SettingsDocumentStore documents)
{
    private const string Key = "pipeline-notifications";

    public async Task<ReminderNotificationSettings> GetAsync(CancellationToken cancellationToken) =>
        await documents.GetAsync<ReminderNotificationSettings>(Key, cancellationToken) ?? new ReminderNotificationSettings();

    public async Task<IReadOnlyList<string>> SaveAsync(ReminderNotificationSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var problems = settings.Problems();
        if (problems.Count == 0)
        {
            await documents.SetAsync(Key, settings, cancellationToken);
        }

        return problems;
    }
}

/// <summary>An application with the offer labels used in notification texts.</summary>
public sealed record NotificationSubject(JobApplication Application, string Title, string Company);

/// <summary>Turns reminders and interviews into notification times. Pure: computed from the timeline, like the reminders.</summary>
public static class ReminderNotificationPlanner
{
    /// <summary>Android limits the alarms an app may keep; the nearest ones are scheduled and the rest on a later sync.</summary>
    public const int MaxNotifications = 50;

    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("es-AR");

    /// <summary>Only future times: an overdue reminder is already on the Panel and is not notified again on every sync.</summary>
    public static IReadOnlyList<ScheduledNotification> Plan(
        IEnumerable<NotificationSubject> subjects, FollowUpPolicy policy, ReminderNotificationSettings settings, DateTime nowUtc, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(subjects);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(zone);
        var planned = new List<(DateTime At, string Title, string Text, string Route)>();
        foreach (var (application, title, company) in subjects)
        {
            var offer = $"{title} — {company}";
            var route = $"/pipeline/{application.Id}";
            if (FollowUpRules.Evaluate(application, policy) is { } reminder)
            {
                var day = TimeZoneInfo.ConvertTimeFromUtc(reminder.DueAtUtc, zone).Date;
                var at = ToUtc(day.AddHours(settings.ReminderHour).AddMinutes(settings.ReminderMinute), zone);
                var what = reminder.Kind switch
                {
                    ReminderKind.NoResponse => "Revisar postulación",
                    ReminderKind.Custom => "Recordatorio",
                    _ => "Follow-up",
                };
                planned.Add((at, $"{what}: {offer}", reminder.Reason, route));
            }

            foreach (var interview in application.Events.Where(e => e.Kind == ApplicationEventKind.InterviewScheduled && e.ScheduledAtUtc is not null))
            {
                var start = interview.ScheduledAtUtc!.Value;
                var local = TimeZoneInfo.ConvertTimeFromUtc(start, zone).ToString("HH:mm", Culture);
                var details = string.IsNullOrWhiteSpace(interview.Text) ? $"Entrevista de {interview.DurationMinutes ?? 60} min." : interview.Text.Trim();
                planned.Add((start.AddMinutes(-settings.InterviewLeadMinutes), $"Entrevista a las {local}: {offer}", details, route));
            }
        }

        return planned
            .Where(p => p.At > nowUtc)
            .OrderBy(p => p.At)
            .Take(MaxNotifications)
            .Select((p, index) => new ScheduledNotification(index + 1, p.At, p.Title, Short(p.Text), p.Route))
            .ToList();
    }

    private static DateTime ToUtc(DateTime local, TimeZoneInfo zone)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        // A time skipped by a daylight saving change does not exist: use the same time one hour later.
        return TimeZoneInfo.ConvertTimeToUtc(zone.IsInvalidTime(unspecified) ? unspecified.AddHours(1) : unspecified, zone);
    }

    private static string Short(string text) => text.Length <= 300 ? text : text[..300] + "…";
}

/// <summary>
/// Keeps Android's alarms in line with the pipeline: called on app start and after every change of an application, the
/// follow-up rule or the notification settings. Failures are logged; they never block the change that triggered them.
/// </summary>
public sealed partial class ReminderNotificationSync(
    IDbContextFactory<MobileDbContext> dbFactory,
    SettingsDocumentStore documents,
    ReminderNotificationSettingsStore settings,
    INotificationScheduler scheduler,
    TimeProvider time,
    ILogger<ReminderNotificationSync> logger)
{
    /// <summary>The notifications the current data produces, whether or not they are turned on (shown in Configuración).</summary>
    public async Task<IReadOnlyList<ScheduledNotification>> PlanAsync(CancellationToken cancellationToken)
    {
        var current = await settings.GetAsync(cancellationToken);
        var policy = await documents.GetAsync<FollowUpPolicy>(PipelineSettingsStore.Key, cancellationToken) ?? new FollowUpPolicy();
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var applications = await db.Applications.Include(a => a.Events).AsNoTracking().AsSplitQuery().ToListAsync(cancellationToken);
        var ids = applications.Select(a => a.JobPostingId).ToList();
        var postings = await db.JobPostings.AsNoTracking().Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.Title, p.Company })
            .ToDictionaryAsync(p => p.Id, cancellationToken);
        var subjects = applications.Select(a => new NotificationSubject(a, postings[a.JobPostingId].Title, postings[a.JobPostingId].Company));
        return ReminderNotificationPlanner.Plan(subjects, policy, current, time.GetUtcNow().UtcDateTime, time.LocalTimeZone);
    }

    public async Task SyncAsync(CancellationToken cancellationToken)
    {
        try
        {
            var enabled = (await settings.GetAsync(cancellationToken)).Enabled;
            await scheduler.ReplaceAsync(enabled ? await PlanAsync(cancellationToken) : []);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
        {
            LogSyncFailed(logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not update the scheduled notifications.")]
    private static partial void LogSyncFailed(ILogger logger, Exception exception);
}
