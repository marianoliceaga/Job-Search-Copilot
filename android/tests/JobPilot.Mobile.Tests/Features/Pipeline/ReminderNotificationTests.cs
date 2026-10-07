using JobPilot.Domain.Pipeline;
using JobPilot.Mobile.Core.Features.Pipeline;

namespace JobPilot.Mobile.Tests.Features.Pipeline;

/// <summary>Android notifications for reminders and interviews: when they ring, what they say, and when they are rescheduled.</summary>
public class ReminderNotificationTests
{
    private static readonly DateTime Now = new(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc);

    private static readonly TimeZoneInfo BuenosAires = TimeZoneInfo.FindSystemTimeZoneById("America/Argentina/Buenos_Aires");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static IReadOnlyList<Core.Infrastructure.Notifications.ScheduledNotification> Plan(
        IEnumerable<JobApplication> applications, ReminderNotificationSettings? settings = null, TimeZoneInfo? zone = null, DateTime? now = null) =>
        ReminderNotificationPlanner.Plan(
            applications.Select(a => new NotificationSubject(a, "Senior .NET Engineer", "Northwind")),
            new FollowUpPolicy(), settings ?? new ReminderNotificationSettings(Enabled: true), now ?? Now, zone ?? BuenosAires);

    [Fact]
    public void Reminders_ring_at_the_chosen_local_time_and_interviews_before_they_start()
    {
        var application = JobApplication.Start(Guid.NewGuid(), ApplicationStage.Applied, Now);
        var interview = new DateTime(2026, 10, 5, 15, 0, 0, DateTimeKind.Utc);
        application.ScheduleInterview(interview, 45, "With the hiring manager", Now);

        var planned = Plan([application]);

        Assert.Collection(planned,
            first =>
            {
                // 60 minutes before; the title shows the local start time (UTC-3).
                Assert.Equal((1, interview.AddHours(-1)), (first.Id, first.AtUtc));
                Assert.Equal("Entrevista a las 12:00: Senior .NET Engineer — Northwind", first.Title);
                Assert.Equal("With the hiring manager", first.Text);
                Assert.Equal($"/pipeline/{application.Id}", first.Route);
            },
            second =>
            {
                // Still "Postulada": 7 days after the last contact, the end of the interview (5 Oct 12:45 local) → 12 Oct, 09:00 local.
                Assert.Equal((2, new DateTime(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc)), (second.Id, second.AtUtc));
                Assert.StartsWith("Follow-up: Senior .NET Engineer — Northwind", second.Title, StringComparison.Ordinal);
            });
    }

    [Fact]
    public void The_reminder_time_and_interview_lead_come_from_the_settings()
    {
        var application = JobApplication.Start(Guid.NewGuid(), ApplicationStage.Applied, Now);
        var interview = new DateTime(2026, 10, 20, 15, 0, 0, DateTimeKind.Utc);
        application.ScheduleInterview(interview, 60, null, Now);
        application.SetReminder(new DateTime(2026, 10, 6, 3, 0, 0, DateTimeKind.Utc), Now);

        var planned = Plan([application], new ReminderNotificationSettings(true, 18, 30, 30));

        Assert.Equal(
            [(new DateTime(2026, 10, 6, 21, 30, 0, DateTimeKind.Utc), "Recordatorio: Senior .NET Engineer — Northwind"), (interview.AddMinutes(-30), "Entrevista a las 12:00: Senior .NET Engineer — Northwind")],
            planned.Select(p => (p.AtUtc, p.Title)));
        Assert.Equal("Entrevista de 60 min.", planned[1].Text);
    }

    [Fact]
    public void Past_times_are_not_notified_and_closed_applications_get_no_reminder()
    {
        var overdue = JobApplication.Start(Guid.NewGuid(), ApplicationStage.Applied, Now.AddDays(-30));
        var rejected = JobApplication.Start(Guid.NewGuid(), ApplicationStage.Applied, Now);
        rejected.MoveTo(ApplicationStage.Rejected, Now);
        var interested = JobApplication.Start(Guid.NewGuid(), ApplicationStage.Interested, Now);
        var startsSoon = JobApplication.Start(Guid.NewGuid(), ApplicationStage.Interview, Now);
        startsSoon.ScheduleInterview(Now.AddMinutes(30), 60, null, Now);

        var planned = Plan([overdue, rejected, interested, startsSoon]);

        // Only the follow-up of the interview stage, 3 days after it ends; the overdue one is already on the Panel.
        var single = Assert.Single(planned);
        Assert.Equal($"/pipeline/{startsSoon.Id}", single.Route);
        Assert.StartsWith("Follow-up:", single.Title, StringComparison.Ordinal);
    }

    [Fact]
    public void After_the_last_follow_up_the_notification_suggests_reviewing_the_application()
    {
        var application = JobApplication.Start(Guid.NewGuid(), ApplicationStage.Applied, Now);
        application.RecordFollowUpSent(null, Now);
        application.RecordFollowUpSent(null, Now);

        Assert.StartsWith("Revisar postulación: ", Assert.Single(Plan([application])).Title, StringComparison.Ordinal);
    }

    [Fact]
    public void A_reminder_time_skipped_by_a_clock_change_moves_one_hour_later()
    {
        // A zone (UTC-3) whose clocks jump from 09:00 to 10:00 on 9 October 2026.
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            new DateTime(2026, 1, 1), new DateTime(2026, 12, 31), TimeSpan.FromHours(1),
            TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 9, 0, 0), 10, 9),
            TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 3, 0, 0), 12, 20));
        var zone = TimeZoneInfo.CreateCustomTimeZone("Test/Gap", TimeSpan.FromHours(-3), "Gap", "Gap", "Gap summer", [rule]);
        var application = JobApplication.Start(Guid.NewGuid(), ApplicationStage.Applied, Now);

        var single = Assert.Single(Plan([application], zone: zone));

        // 10:00 local on 9 October, already at UTC-2.
        Assert.Equal(new DateTime(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc), single.AtUtc);
    }

    [Fact]
    public void Only_the_nearest_notifications_are_scheduled()
    {
        var applications = Enumerable.Range(1, ReminderNotificationPlanner.MaxNotifications + 5)
            .Select(i => JobApplication.Start(Guid.NewGuid(), ApplicationStage.Applied, Now.AddHours(i * 24)))
            .ToList();

        var planned = Plan(applications);

        Assert.Equal(ReminderNotificationPlanner.MaxNotifications, planned.Count);
        Assert.Equal(Enumerable.Range(1, planned.Count), planned.Select(p => p.Id));
        Assert.Equal(planned.OrderBy(p => p.AtUtc).Select(p => p.Id), planned.Select(p => p.Id));
        Assert.Equal($"/pipeline/{applications[0].Id}", planned[0].Route);
    }

    [Theory]
    [InlineData(24, 0, 60)]
    [InlineData(9, 60, 60)]
    [InlineData(9, 0, 4)]
    [InlineData(9, 0, 1441)]
    public void Invalid_settings_are_rejected(int hour, int minute, int lead)
    {
        Assert.NotEmpty(new ReminderNotificationSettings(true, hour, minute, lead).Problems());
    }

    [Fact]
    public async Task Notifications_are_off_until_the_owner_turns_them_on_and_follow_every_change()
    {
        await using var context = await PipelineTestContext.CreateAsync();
        var id = await context.Store.StartAsync(await context.AddPostingAsync(), ApplicationStage.Applied, Ct);

        // Off by default: every sync cancels whatever was scheduled.
        Assert.Empty(context.Notifications.Scheduled!);
        Assert.Single(await context.Sync.PlanAsync(Ct));

        Assert.Empty(await context.NotificationSettings.SaveAsync(new ReminderNotificationSettings(Enabled: true), Ct));
        await context.Sync.SyncAsync(Ct);
        Assert.Single(context.Notifications.Scheduled!);

        await context.Store.ScheduleInterviewAsync(id, context.Now.AddDays(3), 60, null, Ct);
        Assert.Equal(2, context.Notifications.Scheduled!.Count);
        Assert.Contains(context.Notifications.Scheduled, n => n.Title.StartsWith("Entrevista a las", StringComparison.Ordinal));

        var before = context.Notifications.Scheduled.Single(n => n.Title.StartsWith("Follow-up", StringComparison.Ordinal)).AtUtc;
        await context.Settings.SaveAsync(new FollowUpPolicy(AfterAppliedDays: 14), Ct);
        Assert.Equal(before.AddDays(7), context.Notifications.Scheduled.Single(n => n.Title.StartsWith("Follow-up", StringComparison.Ordinal)).AtUtc);

        await context.Store.DeleteAsync(id, Ct);
        Assert.Empty(context.Notifications.Scheduled);
    }

    [Fact]
    public async Task Turning_the_notifications_off_cancels_them()
    {
        await using var context = await PipelineTestContext.CreateAsync();
        await context.NotificationSettings.SaveAsync(new ReminderNotificationSettings(Enabled: true), Ct);
        await context.Store.StartAsync(await context.AddPostingAsync(), ApplicationStage.Applied, Ct);
        Assert.Single(context.Notifications.Scheduled!);

        await context.NotificationSettings.SaveAsync(new ReminderNotificationSettings(Enabled: false), Ct);
        await context.Sync.SyncAsync(Ct);

        Assert.Empty(context.Notifications.Scheduled!);
    }
}
