namespace JobPilot.Domain.Pipeline;

/// <summary>When to remind the owner to follow up (edited in the UI).</summary>
/// <param name="AfterAppliedDays">Days without news after applying (or after the last follow-up or response).</param>
/// <param name="AfterInterviewDays">Days without news after the last interview.</param>
/// <param name="MaxFollowUps">Follow-ups per stage; after that the reminder suggests closing the application.</param>
public sealed record FollowUpPolicy(int AfterAppliedDays = 7, int AfterInterviewDays = 3, int MaxFollowUps = 2)
{
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        if (AfterAppliedDays is < 1 or > 365)
        {
            problems.Add("Los días después de postularte tienen que estar entre 1 y 365.");
        }

        if (AfterInterviewDays is < 1 or > 365)
        {
            problems.Add("Los días después de una entrevista tienen que estar entre 1 y 365.");
        }

        if (MaxFollowUps is < 0 or > 10)
        {
            problems.Add("La cantidad de follow-ups tiene que estar entre 0 y 10.");
        }

        return problems;
    }
}

public enum ReminderKind
{
    /// <summary>Time to send a follow-up.</summary>
    FollowUp,

    /// <summary>The owner chose the date.</summary>
    Custom,

    /// <summary>No answer after the maximum number of follow-ups: consider closing the application.</summary>
    NoResponse,
}

/// <param name="LastContactUtc">Last time something happened with the company (stage change, follow-up, response, interview).</param>
public sealed record FollowUpReminder(ReminderKind Kind, DateTime DueAtUtc, DateTime LastContactUtc, int FollowUpsSent, string Reason)
{
    public bool IsDue(DateTime nowUtc) => DueAtUtc <= nowUtc;

    /// <summary>Whole days past the due date (0 when due today or not due yet).</summary>
    public int DaysOverdue(DateTime nowUtc) => Math.Max(0, (int)(nowUtc - DueAtUtc).TotalDays);
}

/// <summary>Computes reminders when the data is loaded; there is no background process.</summary>
public static class FollowUpRules
{
    public static FollowUpReminder? Evaluate(JobApplication application, FollowUpPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(policy);
        if (application.Stage.IsClosed())
        {
            return null;
        }

        var followUps = FollowUpsInStage(application);
        var (lastContact, since) = LastContact(application);

        if (application.ReminderAtUtc is { } custom)
        {
            return new FollowUpReminder(ReminderKind.Custom, custom, lastContact, followUps, "Recordatorio que elegiste.");
        }

        var days = application.Stage switch
        {
            ApplicationStage.Applied => policy.AfterAppliedDays,
            ApplicationStage.Interview => policy.AfterInterviewDays,
            _ => (int?)null,
        };
        if (days is null)
        {
            return null;
        }

        var due = lastContact.AddDays(days.Value);
        return followUps >= policy.MaxFollowUps
            ? new FollowUpReminder(ReminderKind.NoResponse, due, lastContact, followUps,
                $"Sin respuesta después de {followUps} follow-up(s): considerá cerrarla o esperar.")
            : new FollowUpReminder(ReminderKind.FollowUp, due, lastContact, followUps, $"{days} días sin novedades desde {since}.");
    }

    /// <summary>Follow-ups the owner sent since the application entered its current stage.</summary>
    public static int FollowUpsInStage(JobApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);
        return application.Events.Count(e => e.Kind == ApplicationEventKind.FollowUpSent && e.OccurredAtUtc >= application.StageChangedAtUtc);
    }

    /// <summary>
    /// Last thing that happened with the company in the current stage: entering the stage, a follow-up, a response or the
    /// end of an interview. Interviews count even if they are in the future (the clock starts when they end) unless
    /// <paramref name="asOfUtc"/> is given.
    /// </summary>
    /// <returns>The time and a Spanish description ("postularte", "la última entrevista"...).</returns>
    public static (DateTime At, string Label) LastContact(JobApplication application, DateTime? asOfUtc = null)
    {
        ArgumentNullException.ThrowIfNull(application);
        var last = (At: application.StageChangedAtUtc, Label: application.Stage switch
        {
            ApplicationStage.Applied => "postularte",
            ApplicationStage.Interview => "pasar a entrevista",
            _ => "el último cambio de etapa",
        });
        foreach (var item in application.Events)
        {
            (DateTime At, string Label)? candidate = item.Kind switch
            {
                ApplicationEventKind.FollowUpSent when item.OccurredAtUtc >= application.StageChangedAtUtc => (item.OccurredAtUtc, "el último follow-up"),
                ApplicationEventKind.ResponseReceived => (item.OccurredAtUtc, "la última respuesta"),
                // Scheduled before or after the stage change alike: the interview itself is the contact.
                ApplicationEventKind.InterviewScheduled when item.ScheduledAtUtc is { } start => (start.AddMinutes(item.DurationMinutes ?? 0), "la última entrevista"),
                _ => null,
            };
            if (candidate is { } found && found.At > last.At && (asOfUtc is null || found.At <= asOfUtc))
            {
                last = found;
            }
        }

        return last;
    }
}
