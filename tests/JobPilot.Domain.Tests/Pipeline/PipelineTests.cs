using JobPilot.Domain.Pipeline;

namespace JobPilot.Domain.Tests.Pipeline;

public class JobApplicationTests
{
    private static readonly DateTime T0 = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Every_stage_change_is_recorded_in_the_timeline()
    {
        var application = JobApplication.Start(Guid.NewGuid(), ApplicationStage.Detected, T0);

        application.MoveTo(ApplicationStage.Interested, T0.AddDays(1));
        application.MoveTo(ApplicationStage.Applied, T0.AddDays(2), "Applied through the ATS");
        Assert.Null(application.MoveTo(ApplicationStage.Applied, T0.AddDays(3)));
        application.MoveTo(ApplicationStage.Interview, T0.AddDays(9));

        var changes = application.Timeline().Where(e => e.Kind == ApplicationEventKind.StageChanged).ToList();
        Assert.Equal(
            [(ApplicationStage.Detected, ApplicationStage.Interested), (ApplicationStage.Interested, ApplicationStage.Applied), (ApplicationStage.Applied, ApplicationStage.Interview)],
            changes.Select(c => (c.FromStage!.Value, c.ToStage!.Value)));
        Assert.Equal("Applied through the ATS", changes[1].Text);
        Assert.Equal(ApplicationStage.Interview, application.Stage);
        Assert.Equal(T0.AddDays(9), application.StageChangedAtUtc);
        Assert.Equal(ApplicationEventKind.Created, application.Timeline().First().Kind);
        Assert.All(application.Events, e => Assert.Equal(application.Id, e.ApplicationId));
    }

    [Fact]
    public void Stage_visits_have_entry_and_exit_times()
    {
        var application = JobApplication.Start(Guid.NewGuid(), ApplicationStage.Interested, T0);
        application.MoveTo(ApplicationStage.Applied, T0.AddDays(2));

        Assert.Equal(
            [new StageVisit(ApplicationStage.Interested, T0, T0.AddDays(2)), new StageVisit(ApplicationStage.Applied, T0.AddDays(2), null)],
            application.StageVisits());
    }

    [Fact]
    public void Contacts_documents_and_notes_are_timeline_events()
    {
        var application = JobApplication.Start(Guid.NewGuid(), ApplicationStage.Applied, T0);

        application.AddContact(new ApplicationContact { Name = " Jordan Lee ", Role = "Recruiter" }, T0);
        application.RecordDocumentSent(7, "CV a medida (CV maestro v2)", "{}", T0);
        application.AddNote("Referred by a former colleague", T0);

        Assert.Equal("Jordan Lee", Assert.Single(application.Contacts).Name);
        Assert.Equal(
            ["Jordan Lee (Recruiter)", "CV a medida (CV maestro v2)", "Referred by a former colleague"],
            application.Timeline().Skip(1).Select(e => e.Text));
        Assert.Throws<ArgumentException>(() => application.AddNote("  ", T0));
    }
}

public class FollowUpRulesTests
{
    private static readonly DateTime T0 = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly FollowUpPolicy Policy = new(AfterAppliedDays: 7, AfterInterviewDays: 3, MaxFollowUps: 2);

    private static JobApplication Applied()
    {
        var application = JobApplication.Start(Guid.NewGuid(), ApplicationStage.Interested, T0.AddDays(-3));
        application.MoveTo(ApplicationStage.Applied, T0);
        return application;
    }

    [Fact]
    public void A_follow_up_is_due_after_the_configured_days_without_news()
    {
        var reminder = FollowUpRules.Evaluate(Applied(), Policy)!;

        Assert.Equal(ReminderKind.FollowUp, reminder.Kind);
        Assert.Equal(T0.AddDays(7), reminder.DueAtUtc);
        Assert.False(reminder.IsDue(T0.AddDays(6)));
        Assert.True(reminder.IsDue(T0.AddDays(7)));
        Assert.Equal(2, reminder.DaysOverdue(T0.AddDays(9.5)));
    }

    [Fact]
    public void Sending_a_follow_up_or_getting_a_response_restarts_the_clock()
    {
        var application = Applied();
        application.RecordFollowUpSent("Short note", T0.AddDays(8));

        var afterFollowUp = FollowUpRules.Evaluate(application, Policy)!;
        Assert.Equal(T0.AddDays(15), afterFollowUp.DueAtUtc);
        Assert.Equal("7 días sin novedades desde el último follow-up.", afterFollowUp.Reason);
        Assert.Equal(1, afterFollowUp.FollowUpsSent);

        application.RecordResponse("They will answer next week", T0.AddDays(10));
        Assert.Equal(T0.AddDays(17), FollowUpRules.Evaluate(application, Policy)!.DueAtUtc);
    }

    [Fact]
    public void After_the_maximum_follow_ups_the_reminder_suggests_closing()
    {
        var application = Applied();
        application.RecordFollowUpSent(null, T0.AddDays(7));
        application.RecordFollowUpSent(null, T0.AddDays(14));

        var reminder = FollowUpRules.Evaluate(application, Policy)!;

        Assert.Equal(ReminderKind.NoResponse, reminder.Kind);
        Assert.Equal(T0.AddDays(21), reminder.DueAtUtc);
    }

    [Fact]
    public void Interview_reminders_start_when_the_interview_ends()
    {
        var application = Applied();
        application.ScheduleInterview(T0.AddDays(10), 60, "Video call", T0.AddDays(5));
        application.MoveTo(ApplicationStage.Interview, T0.AddDays(5));

        var reminder = FollowUpRules.Evaluate(application, Policy)!;

        Assert.Equal(T0.AddDays(13).AddHours(1), reminder.DueAtUtc);
        Assert.Equal("3 días sin novedades desde la última entrevista.", reminder.Reason);
        // Before the interview, the last real contact is the stage change.
        Assert.Equal((T0.AddDays(5), "pasar a entrevista"), FollowUpRules.LastContact(application, asOfUtc: T0.AddDays(6)));
    }

    [Fact]
    public void Follow_ups_count_per_stage()
    {
        var application = Applied();
        application.RecordFollowUpSent(null, T0.AddDays(7));
        application.RecordFollowUpSent(null, T0.AddDays(14));
        application.MoveTo(ApplicationStage.Interview, T0.AddDays(16));

        var reminder = FollowUpRules.Evaluate(application, Policy)!;

        Assert.Equal(ReminderKind.FollowUp, reminder.Kind);
        Assert.Equal(0, reminder.FollowUpsSent);
        Assert.Equal(T0.AddDays(19), reminder.DueAtUtc);
    }

    [Fact]
    public void A_custom_reminder_wins_until_a_follow_up_is_sent()
    {
        var application = Applied();
        application.SetReminder(T0.AddDays(2), T0);

        Assert.Equal((ReminderKind.Custom, T0.AddDays(2)), (FollowUpRules.Evaluate(application, Policy)!.Kind, FollowUpRules.Evaluate(application, Policy)!.DueAtUtc));

        application.RecordFollowUpSent(null, T0.AddDays(2));
        Assert.Equal(ReminderKind.FollowUp, FollowUpRules.Evaluate(application, Policy)!.Kind);
        Assert.Null(application.ReminderAtUtc);
    }

    [Theory]
    [InlineData(ApplicationStage.Detected)]
    [InlineData(ApplicationStage.Interested)]
    [InlineData(ApplicationStage.Offer)]
    [InlineData(ApplicationStage.Rejected)]
    [InlineData(ApplicationStage.Discarded)]
    public void Other_stages_have_no_rule_reminder(ApplicationStage stage)
    {
        var application = JobApplication.Start(Guid.NewGuid(), stage, T0);

        Assert.Null(FollowUpRules.Evaluate(application, Policy));
    }

    [Fact]
    public void Closed_applications_ignore_custom_reminders()
    {
        var application = Applied();
        application.SetReminder(T0.AddDays(1), T0);
        application.MoveTo(ApplicationStage.Rejected, T0.AddDays(1));

        Assert.Null(FollowUpRules.Evaluate(application, Policy));
    }

    [Fact]
    public void Invalid_policies_are_reported()
    {
        Assert.Empty(Policy.Problems());
        Assert.Equal(3, new FollowUpPolicy(0, 400, -1).Problems().Count);
    }
}

public class PipelineMetricsTests
{
    private static readonly DateTime T0 = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

    private static JobApplication Path(params (ApplicationStage Stage, double Day)[] steps)
    {
        var application = JobApplication.Start(Guid.NewGuid(), steps[0].Stage, T0.AddDays(steps[0].Day));
        foreach (var (stage, day) in steps.Skip(1))
        {
            application.MoveTo(stage, T0.AddDays(day));
        }

        return application;
    }

    [Fact]
    public void Response_rate_counts_interviews_offers_rejections_and_recorded_responses()
    {
        var interviewed = Path((ApplicationStage.Interested, 0), (ApplicationStage.Applied, 2), (ApplicationStage.Interview, 6));
        var rejected = Path((ApplicationStage.Applied, 0), (ApplicationStage.Rejected, 10));
        var silent = Path((ApplicationStage.Applied, 0));
        var answered = Path((ApplicationStage.Applied, 0));
        answered.RecordResponse("Under review", T0.AddDays(2));
        var neverApplied = Path((ApplicationStage.Detected, 0), (ApplicationStage.Discarded, 1));
        var directInterview = Path((ApplicationStage.Interested, 0), (ApplicationStage.Interview, 3));

        var metrics = PipelineMetrics.Compute([interviewed, rejected, silent, answered, neverApplied, directInterview]);

        Assert.Equal(6, metrics.Total);
        Assert.Equal(5, metrics.Applied);
        Assert.Equal(4, metrics.Responded);
        Assert.Equal(0.8, metrics.ResponseRate!.Value, 6);
        // (4 + 10 + 2 + 0) / 4
        Assert.Equal(4, metrics.AverageDaysToFirstResponse!.Value, 6);
        Assert.Equal(2, metrics.ByStage[ApplicationStage.Applied]);
        Assert.Equal(2, metrics.ByStage[ApplicationStage.Interview]);
        Assert.Equal(0, metrics.ByStage[ApplicationStage.Offer]);
    }

    [Fact]
    public void Average_time_per_stage_uses_completed_stays_only()
    {
        var a = Path((ApplicationStage.Interested, 0), (ApplicationStage.Applied, 2), (ApplicationStage.Interview, 6));
        var b = Path((ApplicationStage.Interested, 0), (ApplicationStage.Applied, 4), (ApplicationStage.Rejected, 14));

        var times = PipelineMetrics.Compute([a, b]).TimeInStage.ToDictionary(t => t.Stage);

        Assert.Equal((2, 3d), (times[ApplicationStage.Interested].Visits, times[ApplicationStage.Interested].AverageDays!.Value));
        Assert.Equal((2, 7d), (times[ApplicationStage.Applied].Visits, times[ApplicationStage.Applied].AverageDays!.Value));
        Assert.Equal(0, times[ApplicationStage.Interview].Visits);
        Assert.Null(times[ApplicationStage.Interview].AverageDays);
    }

    [Fact]
    public void Empty_pipeline_has_no_rates()
    {
        var metrics = PipelineMetrics.Compute([]);

        Assert.Equal(0, metrics.Applied);
        Assert.Null(metrics.ResponseRate);
        Assert.Null(metrics.AverageDaysToFirstResponse);
    }
}

public class PipelineCalendarTests
{
    private static readonly DateTime T0 = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Upcoming_interviews_and_the_next_reminder_become_calendar_items()
    {
        var application = JobApplication.Start(Guid.NewGuid(), ApplicationStage.Applied, T0);
        application.ScheduleInterview(T0.AddDays(-5), 30, "Old screening", T0.AddDays(-6));
        var upcoming = application.ScheduleInterview(T0.AddDays(3), 45, "Technical interview", T0);
        upcoming.Id = 42;
        var zone = TimeZoneInfo.CreateCustomTimeZone("UTC-3", TimeSpan.FromHours(-3), "UTC-3", "UTC-3");

        var items = PipelineCalendar.Items(application, "Senior Engineer — Contoso", "https://jobs.example/1", new FollowUpPolicy(), T0, zone).ToList();

        Assert.Equal(2, items.Count);
        var interview = items[0];
        Assert.Equal($"jobpilot-interview-{application.Id:N}-42", interview.Uid);
        Assert.Equal((T0.AddDays(3), T0.AddDays(3).AddMinutes(45)), (interview.StartUtc, interview.EndUtc));
        Assert.Equal("Technical interview\nhttps://jobs.example/1", interview.Description);
        var reminder = items[1];
        Assert.True(reminder.IsAllDay);
        // Interview ends Sep 4 12:45 UTC; + 7 days = Sep 11 12:45 UTC = Sep 11 09:45 in UTC-3.
        Assert.Equal(new DateOnly(2026, 9, 11), reminder.Date);
        Assert.StartsWith("Follow-up: ", reminder.Summary, StringComparison.Ordinal);
    }
}
