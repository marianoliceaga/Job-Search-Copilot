namespace JobPilot.Domain.Pipeline;

/// <summary>An entry of the exported calendar: a timed event (interview) or an all-day event (reminder).</summary>
/// <param name="Uid">Stable identifier, so importing the file again updates the entry instead of duplicating it.</param>
public sealed record CalendarItem(string Uid, string Summary, string Description, DateTime? StartUtc, DateTime? EndUtc, DateOnly? Date, string? Url)
{
    public bool IsAllDay => Date is not null;
}

/// <summary>Builds calendar entries for upcoming interviews and follow-up reminders.</summary>
public static class PipelineCalendar
{
    /// <param name="offer">"Title — Company", used in summaries.</param>
    /// <param name="fromUtc">Interviews that ended before this are left out.</param>
    /// <param name="zone">Owner's time zone, for the date of all-day reminders.</param>
    public static IEnumerable<CalendarItem> Items(JobApplication application, string offer, string? url, FollowUpPolicy policy, DateTime fromUtc, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(zone);
        foreach (var interview in application.Events.Where(e => e.Kind == ApplicationEventKind.InterviewScheduled && e.ScheduledAtUtc is not null))
        {
            var start = interview.ScheduledAtUtc!.Value;
            var end = start.AddMinutes(interview.DurationMinutes ?? 60);
            if (end < fromUtc)
            {
                continue;
            }

            yield return new CalendarItem(
                $"jobpilot-interview-{application.Id:N}-{interview.Id}",
                $"Entrevista: {offer}",
                Description(interview.Text, url),
                start, end, null, url);
        }

        if (FollowUpRules.Evaluate(application, policy) is { } reminder)
        {
            var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(reminder.DueAtUtc, zone));
            var summary = reminder.Kind == ReminderKind.NoResponse ? $"Revisar postulación: {offer}" : $"Follow-up: {offer}";
            yield return new CalendarItem($"jobpilot-follow-up-{application.Id:N}", summary, Description(reminder.Reason, url), null, null, date, url);
        }
    }

    private static string Description(string? text, string? url) =>
        string.Join("\n", new[] { text, url }.Where(s => !string.IsNullOrWhiteSpace(s)));
}
