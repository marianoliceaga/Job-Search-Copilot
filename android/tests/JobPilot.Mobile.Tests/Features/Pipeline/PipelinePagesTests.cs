using Bunit;
using JobPilot.Domain.Pipeline;
using JobPilot.Mobile.Core;
using JobPilot.Mobile.Core.Features.Dashboard;
using JobPilot.Mobile.Core.Features.Pipeline;
using JobPilot.Mobile.Core.Features.Profile;
using JobPilot.Mobile.Core.Features.Tailoring;
using JobPilot.Mobile.Core.Infrastructure;
using JobPilot.Mobile.Core.Infrastructure.Files;
using JobPilot.Mobile.Core.Infrastructure.Llm;
using JobPilot.Mobile.Core.Infrastructure.Notifications;
using JobPilot.Mobile.Core.Infrastructure.Persistence;
using JobPilot.Mobile.Core.Infrastructure.Secrets;
using JobPilot.Mobile.Tests.Features.Tailoring;
using JobPilot.Mobile.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

namespace JobPilot.Mobile.Tests.Features.Pipeline;

/// <summary>The pipeline on the phone: lists per stage, the application page, the Panel and the notification settings.</summary>
public sealed class PipelinePagesTests : BunitContext, IAsyncLifetime
{
    private readonly TempDirectory _directory = new();
    private readonly FakeShareService _share = new();
    private readonly FakeNotificationScheduler _notifications = new();
    private readonly FakeLlmClient _llm = new();
    private Guid _postingId;
    private long _documentId;

    public PipelinePagesTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
        Services.AddMudServices();
        Services.AddJobPilotMobile(MobileConfiguration.Load(), new MobilePaths(_directory.Path));
        Services.AddSingleton<ISecretStore>(new InMemorySecretStore());
        Services.AddSingleton<INotificationScheduler>(_notifications);
        Services.AddSingleton<JobPilot.Mobile.Core.Infrastructure.Google.IGoogleAuthorizer>(new FakeGoogleAuthorizer());
        Services.AddSingleton<IFilePickerService>(new FakeFilePicker());
        Services.AddSingleton<IShareService>(_share);
        Services.AddSingleton<ILlmClient>(_llm);
    }

    private static CancellationToken Ct => Xunit.TestContext.Current.CancellationToken;

    private ApplicationStore Store => Services.GetRequiredService<ApplicationStore>();

    public async ValueTask InitializeAsync()
    {
        await Services.GetRequiredService<AppInitializer>().EnsureInitializedAsync();
        var (master, tailored, posting) = ExportTests.Sample();
        var version = (await Services.GetRequiredService<MasterCvStore>().SaveAsync(master, "test", Ct)).Version!.Value;
        await using (var db = await Services.GetRequiredService<IDbContextFactory<MobileDbContext>>().CreateDbContextAsync(Ct))
        {
            db.JobPostings.Add(posting);
            await db.SaveChangesAsync(Ct);
        }

        _postingId = posting.Id;
        var documents = Services.GetRequiredService<TailoredDocumentStore>();
        _documentId = await documents.AddDraftAsync(posting.Id, TailoredKind.Cv, version, tailored, "cv-tailoring.v1", "gemini-test", null, Ct);
        await documents.SetStatusAsync(_documentId, TailoredStatus.Approved, Ct);
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await base.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        _directory.Dispose();
    }

    [Fact]
    public async Task The_pipeline_lists_applications_per_stage_and_moves_them_with_the_card_menu()
    {
        var id = await Store.StartAsync(_postingId, ApplicationStage.Applied, Ct);
        var popovers = Render<MudPopoverProvider>();
        var cut = Render<PipelinePage>();

        cut.WaitForAssertion(() => Assert.Contains("Postulada (1)", cut.FindAll(".mud-chipset .mud-chip").Select(t => t.TextContent.Trim())));
        // Every stage stays visible (chips wrap instead of a scrolling tab bar).
        Assert.Equal(7, cut.FindAll(".mud-chipset .mud-chip").Count);
        Assert.Empty(cut.FindAll(".mud-tab"));
        // Opens on the first stage with applications.
        Assert.Single(cut.Find("[data-testid=stage-Applied]").QuerySelectorAll("[data-testid=pipeline-card]"));
        Assert.NotNull(cut.Find("[data-testid=notifications-off]"));

        cut.Find("[data-testid=pipeline-card] button").Click();
        popovers.WaitForElements(".mud-menu-item").Single(i => i.TextContent.Contains("Mover a Entrevista", StringComparison.Ordinal)).Click();

        cut.WaitForAssertion(() => Assert.Contains("pasó a Entrevista", cut.Markup, StringComparison.Ordinal));
        var detail = await Store.GetAsync(id, Ct);
        Assert.Equal(ApplicationStage.Interview, detail!.Application.Stage);
        Assert.Contains(detail.Application.Events, e => e.Kind == ApplicationEventKind.StageChanged && e.ToStage == ApplicationStage.Interview);
    }

    [Fact]
    public async Task The_calendar_is_shared_as_an_ics_file()
    {
        await Store.StartAsync(_postingId, ApplicationStage.Applied, Ct);
        var cut = Render<PipelinePage>();

        cut.WaitForElement("[data-testid=share-ics]").Click();

        cut.WaitForAssertion(() => Assert.Single(_share.Shared));
        var (_, fileName, contentType, content) = _share.Shared[0];
        Assert.Equal(("jobpilot-pipeline.ics", "text/calendar"), (fileName, contentType));
        Assert.Contains("Follow-up: Senior .NET Engineer — Contoso Cloud", System.Text.Encoding.UTF8.GetString(content), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_sent_document_is_recorded_and_shared_as_it_was_sent()
    {
        var id = await Store.StartAsync(_postingId, ApplicationStage.Applied, Ct);
        var cut = Render<ApplicationPage>(p => p.Add(c => c.Id, id));

        cut.WaitForElements(".mud-chipset .mud-chip").Single(t => t.TextContent.Trim() == "Documentos").Click();
        cut.WaitForElements("button").Single(b => b.TextContent.Trim() == "Registrar como enviado").Click();
        cut.WaitForAssertion(() => Assert.Contains("Documento registrado como enviado.", cut.Markup, StringComparison.Ordinal));
        cut.FindAll("button").First(b => b.TextContent.Trim() == "PDF").Click();

        cut.WaitForAssertion(() => Assert.Single(_share.Shared));
        Assert.Equal(("Alex Rivera - CV - Contoso Cloud.pdf", "application/pdf"), (_share.Shared[0].FileName, _share.Shared[0].ContentType));
    }

    [Fact]
    public async Task A_follow_up_draft_is_copied_or_shared_and_the_owner_marks_it_as_sent()
    {
        var id = await Store.StartAsync(_postingId, ApplicationStage.Applied, Ct);
        _llm.Returns(new FollowUpDraftResponse(
            "Senior .NET Engineer application",
            [new FollowUpParagraphResponse("I applied for the Senior .NET Engineer role and wanted to check whether there are any updates.", [])],
            "Pregunta por el estado."));
        var cut = Render<ApplicationPage>(p => p.Add(c => c.Id, id));

        cut.WaitForElements(".mud-chipset .mud-chip").Single(t => t.TextContent.Trim() == "Follow-up").Click();
        cut.WaitForElements("button").Single(b => b.TextContent.Trim() == "Preparar borrador").Click();
        cut.WaitForElements("button").Single(b => b.TextContent.Trim() == "Generar borrador con Gemini").Click();
        cut.WaitForElement("[data-testid=share-follow-up]").Click();
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Copiar mensaje").Click();

        var (_, subject, text) = Assert.Single(_share.SharedTexts);
        Assert.Equal("Senior .NET Engineer application", subject);
        Assert.StartsWith("Hello,", text, StringComparison.Ordinal);
        Assert.EndsWith("Best regards,\nAlex Rivera", text, StringComparison.Ordinal);
        Assert.Equal(text, Assert.Single(_share.Copied));

        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Ya lo envié").Click();
        cut.WaitForAssertion(async () => Assert.Contains((await Store.GetAsync(id, Ct))!.Application.Events, e => e.Kind == ApplicationEventKind.FollowUpSent));
    }

    [Fact]
    public async Task Turning_notifications_on_asks_android_and_schedules_them()
    {
        await Store.StartAsync(_postingId, ApplicationStage.Applied, Ct);
        var cut = Render<NotificationsPanel>();

        cut.WaitForElement("[data-testid=upcoming-notifications]");
        Assert.Contains("Los avisos están apagados", cut.Markup, StringComparison.Ordinal);
        cut.Find("[data-testid=notifications-switch] input, input[data-testid=notifications-switch]").Change(true);

        cut.WaitForAssertion(() => Assert.Contains("Avisos guardados y programados.", cut.Markup, StringComparison.Ordinal));
        Assert.Equal(1, _notifications.PermissionRequests);
        Assert.StartsWith("Follow-up: Senior .NET Engineer", Assert.Single(_notifications.Scheduled!).Title, StringComparison.Ordinal);
        Assert.True((await Services.GetRequiredService<ReminderNotificationSettingsStore>().GetAsync(Ct)).Enabled);
    }

    [Fact]
    public async Task Notifications_stay_off_when_android_denies_them()
    {
        _notifications.GrantOnRequest = false;
        var cut = Render<NotificationsPanel>();

        cut.WaitForElement("[data-testid=notifications-switch] input, input[data-testid=notifications-switch]").Change(true);

        cut.WaitForAssertion(() => Assert.Contains("Android no permitió las notificaciones", cut.Markup, StringComparison.Ordinal));
        Assert.False((await Services.GetRequiredService<ReminderNotificationSettingsStore>().GetAsync(Ct)).Enabled);
        Assert.Null(_notifications.Scheduled);
    }

    [Fact]
    public async Task Without_exact_alarms_the_panel_offers_the_system_setting()
    {
        await Services.GetRequiredService<ReminderNotificationSettingsStore>().SaveAsync(new ReminderNotificationSettings(Enabled: true), Ct);
        _notifications.Access = new NotificationAccess(CanNotify: true, CanUseExactAlarms: false, ExactAlarmsNeedPermission: true);
        var cut = Render<NotificationsPanel>();

        cut.WaitForElement("[data-testid=exact-alarms-settings]").Click();

        Assert.Equal(1, _notifications.ExactAlarmSettingsOpened);
        Assert.NotNull(cut.Find("[data-testid=exact-alarms-off]"));
    }

    [Fact]
    public async Task The_panel_shows_metrics_due_reminders_and_upcoming_interviews()
    {
        var id = await Store.StartAsync(_postingId, ApplicationStage.Interview, Ct);
        await Store.ScheduleInterviewAsync(id, DateTime.UtcNow.AddDays(2), 60, null, Ct);
        var cut = Render<DashboardPage>();

        cut.WaitForElement("[data-testid=metric-applications]");
        Assert.Contains("Senior .NET Engineer — Contoso Cloud", cut.Find("[data-testid=upcoming-interviews]").TextContent, StringComparison.Ordinal);
        Assert.Contains("Entrevista", cut.Find("[data-testid=stages]").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("table"));
    }
}
