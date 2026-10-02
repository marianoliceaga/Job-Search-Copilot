using Ical.Net;
using JobPilot.Domain.Jobs;
using JobPilot.Domain.Pipeline;
using JobPilot.Domain.Tailoring;
using JobPilot.Web.Features.Pipeline;
using JobPilot.Web.Features.Tailoring;
using JobPilot.Web.Infrastructure.Persistence;
using JobPilot.Web.Tests.TestSupport;

namespace JobPilot.Web.Tests.Features.Pipeline;

/// <summary>Pipeline services on a temporary database.</summary>
public sealed class PipelineTestContext : IAsyncDisposable
{
    private PipelineTestContext(JobsTestContext jobs)
    {
        Jobs = jobs;
        Settings = new PipelineSettingsStore(jobs.Documents);
        Store = new ApplicationStore(jobs.Db, Settings, jobs.Time);
        Documents = new TailoredDocumentStore(jobs.Db, jobs.Time);
        Calendar = new CalendarExport(Store, Settings, jobs.List, jobs.Time);
    }

    public JobsTestContext Jobs { get; }

    public PipelineSettingsStore Settings { get; }

    public ApplicationStore Store { get; }

    public TailoredDocumentStore Documents { get; }

    public CalendarExport Calendar { get; }

    public DateTime Now => Jobs.Time.GetUtcNow().UtcDateTime;

    public static async Task<PipelineTestContext> CreateAsync() => new(await JobsTestContext.CreateAsync());

    public async Task<Guid> AddPostingAsync(string title = "Senior .NET Engineer", string company = "Northwind", string url = "https://remotive.com/jobs/1")
    {
        var result = await Jobs.Ingestion.IngestAsync([JobsTestContext.Job(title, company, url)], new JobSearchPreferences(), false, TestContext.Current.CancellationToken);
        return result.PostingIds[0];
    }

    public ValueTask DisposeAsync() => Jobs.DisposeAsync();
}

public class ApplicationStoreTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Every_status_change_is_persisted_in_the_timeline()
    {
        await using var context = await PipelineTestContext.CreateAsync();
        var postingId = await context.AddPostingAsync();

        var id = await context.Store.StartAsync(postingId, ApplicationStage.Detected, Ct);
        Assert.Equal(id, await context.Store.StartAsync(postingId, ApplicationStage.Applied, Ct));
        context.Jobs.Time.Advance(TimeSpan.FromDays(1));
        await context.Store.MoveAsync(id, ApplicationStage.Interested, null, Ct);
        context.Jobs.Time.Advance(TimeSpan.FromDays(1));
        await context.Store.MoveAsync(id, ApplicationStage.Applied, "Through the company ATS", Ct);
        await context.Store.MoveAsync(id, ApplicationStage.Applied, null, Ct);

        var detail = (await context.Store.GetAsync(id, Ct))!;
        var timeline = detail.Application.Timeline().ToList();
        Assert.Equal([ApplicationEventKind.Created, ApplicationEventKind.StageChanged, ApplicationEventKind.StageChanged], timeline.Select(e => e.Kind));
        Assert.Equal([ApplicationStage.Detected, ApplicationStage.Interested, ApplicationStage.Applied], timeline.Select(e => e.ToStage!.Value));
        Assert.Equal("Through the company ATS", timeline[2].Text);
        Assert.Equal(ApplicationStage.Applied, detail.Application.Stage);
        Assert.Equal(context.Now.AddDays(7), detail.Reminder!.DueAtUtc);
        Assert.Equal(id, await context.Store.FindByPostingAsync(postingId, Ct));
    }

    [Fact]
    public async Task Board_cards_carry_the_offer_reminder_and_next_interview()
    {
        await using var context = await PipelineTestContext.CreateAsync();
        var first = await context.Store.StartAsync(await context.AddPostingAsync(), ApplicationStage.Applied, Ct);
        context.Jobs.Time.Advance(TimeSpan.FromHours(1));
        var second = await context.Store.StartAsync(await context.AddPostingAsync("Staff Engineer", "Contoso", "https://remotive.com/jobs/2"), ApplicationStage.Interested, Ct);
        await context.Store.ScheduleInterviewAsync(first, context.Now.AddDays(2), 45, "Screening", Ct);

        var board = await context.Store.GetBoardAsync(Ct);

        Assert.Equal([second, first], board.Select(c => c.Id));
        Assert.Equal(("Staff Engineer", "Contoso"), (board[0].Title, board[0].Company));
        Assert.Null(board[0].Reminder);
        Assert.Equal(context.Now.AddDays(2), board[1].NextInterviewUtc);
        Assert.Equal(context.Now.AddDays(2).AddMinutes(45).AddDays(7), board[1].Reminder!.DueAtUtc);
    }

    [Fact]
    public async Task Only_approved_documents_of_the_offer_can_be_recorded_as_sent_and_the_sent_content_is_kept()
    {
        await using var context = await PipelineTestContext.CreateAsync();
        var postingId = await context.AddPostingAsync();
        var otherPosting = await context.AddPostingAsync("Other", "Other Co", "https://remotive.com/jobs/9");
        var id = await context.Store.StartAsync(postingId, ApplicationStage.Applied, Ct);
        var letter = new CoverLetter { Paragraphs = [new TraceableText { Text = "Original paragraph." }] };
        var documentId = await context.Documents.AddDraftAsync(postingId, TailoredKind.CoverLetter, 3, letter, "cover-letter.v1", "fake", null, Ct);
        var otherDocument = await context.Documents.AddDraftAsync(otherPosting, TailoredKind.CoverLetter, 3, letter, "cover-letter.v1", "fake", null, Ct);

        Assert.Equal("Solo se pueden registrar como enviados documentos aprobados.", await context.Store.RecordDocumentSentAsync(id, documentId, Ct));
        Assert.Equal("El documento no pertenece a esta oferta.", await context.Store.RecordDocumentSentAsync(id, otherDocument, Ct));

        await context.Documents.SetStatusAsync(documentId, TailoredStatus.Approved, Ct);
        Assert.Null(await context.Store.RecordDocumentSentAsync(id, documentId, Ct));
        await context.Documents.SetStatusAsync(documentId, TailoredStatus.Draft, Ct);
        await context.Documents.SaveAsync(documentId, new CoverLetter { Paragraphs = [new TraceableText { Text = "Edited later." }] }, Ct);

        var sent = Assert.Single((await context.Store.GetAsync(id, Ct))!.Application.Events, e => e.Kind == ApplicationEventKind.DocumentSent);
        Assert.Equal("Carta de presentación (CV maestro v3)", sent.Text);
        var snapshot = ApplicationStore.ReadSnapshot(sent)!;
        Assert.Equal((TailoredKind.CoverLetter, 3), (snapshot.Kind, snapshot.MasterCvVersion));
        Assert.Contains("Original paragraph.", snapshot.Json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Notes_and_interviews_can_be_deleted_but_stage_changes_cannot()
    {
        await using var context = await PipelineTestContext.CreateAsync();
        var id = await context.Store.StartAsync(await context.AddPostingAsync(), ApplicationStage.Interested, Ct);
        await context.Store.AddNoteAsync(id, "Looks promising", Ct);
        await context.Store.MoveAsync(id, ApplicationStage.Applied, null, Ct);
        var events = (await context.Store.GetAsync(id, Ct))!.Application.Events;

        await context.Store.DeleteEventAsync(id, events.Single(e => e.Kind == ApplicationEventKind.Note).Id, Ct);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            context.Store.DeleteEventAsync(id, events.Single(e => e.Kind == ApplicationEventKind.StageChanged).Id, Ct));

        Assert.DoesNotContain((await context.Store.GetAsync(id, Ct))!.Application.Events, e => e.Kind == ApplicationEventKind.Note);
    }

    [Fact]
    public async Task Contacts_are_added_edited_and_removed()
    {
        await using var context = await PipelineTestContext.CreateAsync();
        var id = await context.Store.StartAsync(await context.AddPostingAsync(), ApplicationStage.Applied, Ct);

        await context.Store.AddContactAsync(id, new ApplicationContact { Name = "Jordan Lee", Role = "Recruiter", Email = "jordan@example.com" }, Ct);
        var contact = Assert.Single((await context.Store.GetAsync(id, Ct))!.Application.Contacts);
        contact.Role = "Talent Partner";
        await context.Store.UpdateContactAsync(id, contact, Ct);
        Assert.Equal("Talent Partner", Assert.Single((await context.Store.GetAsync(id, Ct))!.Application.Contacts).Role);

        await context.Store.DeleteContactAsync(id, contact.Id, Ct);
        var detail = (await context.Store.GetAsync(id, Ct))!;
        Assert.Empty(detail.Application.Contacts);
        Assert.Contains(detail.Application.Events, e => e.Kind == ApplicationEventKind.ContactAdded && e.Text == "Jordan Lee (Recruiter)");
    }

    [Fact]
    public async Task The_follow_up_rule_is_saved_and_validated()
    {
        await using var context = await PipelineTestContext.CreateAsync();

        Assert.Equal(new FollowUpPolicy(), await context.Settings.GetAsync(Ct));
        Assert.NotEmpty(await context.Settings.SaveAsync(new FollowUpPolicy(0), Ct));
        Assert.Empty(await context.Settings.SaveAsync(new FollowUpPolicy(10, 4, 1), Ct));

        Assert.Equal(new FollowUpPolicy(10, 4, 1), await context.Settings.GetAsync(Ct));
    }

    [Fact]
    public async Task Removing_an_application_keeps_the_offer()
    {
        await using var context = await PipelineTestContext.CreateAsync();
        var postingId = await context.AddPostingAsync();
        var id = await context.Store.StartAsync(postingId, ApplicationStage.Applied, Ct);
        await context.Store.AddNoteAsync(id, "note", Ct);

        await context.Store.DeleteAsync(id, Ct);

        Assert.Null(await context.Store.GetAsync(id, Ct));
        Assert.NotNull(await context.Jobs.List.GetItemAsync(postingId, Ct));
        await using var db = await context.Jobs.Db.CreateDbContextAsync(Ct);
        Assert.Empty(db.ApplicationEvents);
    }
}

public class CalendarExportTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Ics_round_trips_timed_and_all_day_entries_with_special_characters()
    {
        var stamp = new DateTime(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc);
        CalendarItem[] items =
        [
            new("jobpilot-interview-1", "Entrevista: Senior Engineer — Compañía, S.A.; Remote", "Línea 1, con coma; y punto y coma\nLínea 2 \\ barra",
                new DateTime(2026, 10, 5, 15, 30, 0, DateTimeKind.Utc), new DateTime(2026, 10, 5, 16, 15, 0, DateTimeKind.Utc), null, "https://jobs.example/1?a=1&b=2"),
            new("jobpilot-follow-up-2", "Follow-up: Staff Engineer — Contoso", "7 días sin novedades.", null, null, new DateOnly(2026, 10, 9), null),
        ];

        var ics = CalendarExport.Serialize(items, stamp);
        var calendar = Calendar.Load(ics)!;

        Assert.Equal(2, calendar.Events.Count);
        var interview = calendar.Events.Single(e => e.Uid == items[0].Uid);
        Assert.Equal(items[0].Summary, interview.Summary);
        Assert.Equal(items[0].Description, interview.Description);
        Assert.Equal(items[0].StartUtc, interview.DtStart!.AsUtc);
        Assert.Equal(items[0].EndUtc, interview.DtEnd!.AsUtc);
        Assert.Equal(new Uri(items[0].Url!), interview.Url);
        Assert.False(interview.IsAllDay);

        var reminder = calendar.Events.Single(e => e.Uid == items[1].Uid);
        Assert.True(reminder.IsAllDay);
        Assert.Equal(new DateOnly(2026, 10, 9), reminder.DtStart!.Date);
        Assert.Equal(items[1].Summary, reminder.Summary);

        // Serializing what was loaded gives the same file.
        Assert.Equal(ics, new Ical.Net.Serialization.CalendarSerializer().SerializeToString(calendar));
    }

    [Fact]
    public async Task The_pipeline_export_contains_upcoming_interviews_and_reminders()
    {
        await using var context = await PipelineTestContext.CreateAsync();
        var id = await context.Store.StartAsync(await context.AddPostingAsync(), ApplicationStage.Applied, Ct);
        await context.Store.ScheduleInterviewAsync(id, context.Now.AddDays(3), 60, "Technical", Ct);
        await context.Store.StartAsync(await context.AddPostingAsync("Other", "Contoso", "https://remotive.com/jobs/3"), ApplicationStage.Detected, Ct);

        var (fileName, content, count) = await context.Calendar.ExportAsync(null, Ct);
        var calendar = Calendar.Load(System.Text.Encoding.UTF8.GetString(content))!;

        Assert.Equal("jobpilot-pipeline.ics", fileName);
        Assert.Equal(2, count);
        Assert.Contains(calendar.Events, e => e.Summary == "Entrevista: Senior .NET Engineer — Northwind" && e.Description!.Contains("Technical", StringComparison.Ordinal));
        Assert.Contains(calendar.Events, e => e.Summary == "Follow-up: Senior .NET Engineer — Northwind" && e.IsAllDay);
    }
}
