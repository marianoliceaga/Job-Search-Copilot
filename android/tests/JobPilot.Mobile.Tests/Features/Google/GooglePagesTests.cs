using System.Net;
using Bunit;
using JobPilot.Domain.Pipeline;
using JobPilot.Mobile.Core;
using JobPilot.Mobile.Core.Features.Google;
using JobPilot.Mobile.Core.Features.Pipeline;
using JobPilot.Mobile.Core.Features.Profile;
using JobPilot.Mobile.Core.Infrastructure;
using JobPilot.Mobile.Core.Infrastructure.Files;
using JobPilot.Mobile.Core.Infrastructure.Google;
using JobPilot.Mobile.Core.Infrastructure.Notifications;
using JobPilot.Mobile.Core.Infrastructure.Persistence;
using JobPilot.Mobile.Core.Infrastructure.Secrets;
using JobPilot.Mobile.Tests.Features.Tailoring;
using JobPilot.Mobile.Tests.Google;
using JobPilot.Mobile.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace JobPilot.Mobile.Tests.Features.Google;

/// <summary>Google on the phone's screens: connecting through Android's authorization API and the actions it enables.</summary>
public sealed class GooglePagesTests : BunitContext, IAsyncLifetime
{
    private readonly TempDirectory _directory = new();
    private readonly FakeGoogleAuthorizer _authorizer = new();
    private readonly FakeGoogleHandler _google = new();
    private readonly FakeShareService _share = new();
    private readonly FakeAppSigningInfo _signing = new();
    private Guid _postingId;

    public GooglePagesTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
        Services.AddMudServices();
        Services.AddJobPilotMobile(MobileConfiguration.Load(), new MobilePaths(_directory.Path));
        Services.AddSingleton<ISecretStore>(new InMemorySecretStore());
        Services.AddSingleton<INotificationScheduler>(new FakeNotificationScheduler());
        Services.AddSingleton<IGoogleAuthorizer>(_authorizer);
        Services.AddSingleton<IFilePickerService>(new FakeFilePicker());
        Services.AddSingleton<IShareService>(_share);
        Services.AddSingleton<IAppSigningInfo>(_signing);
        Services.ConfigureHttpClientDefaults(builder => builder.ConfigurePrimaryHttpMessageHandler(() => _google));
    }

    private static CancellationToken Ct => Xunit.TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await Services.GetRequiredService<AppInitializer>().EnsureInitializedAsync();
        var (master, _, posting) = ExportTests.Sample();
        await Services.GetRequiredService<MasterCvStore>().SaveAsync(master, "test", Ct);
        await using var db = await Services.GetRequiredService<IDbContextFactory<MobileDbContext>>().CreateDbContextAsync(Ct);
        db.JobPostings.Add(posting);
        await db.SaveChangesAsync(Ct);
        _postingId = posting.Id;
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await base.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        _directory.Dispose();
    }

    [Fact]
    public void Without_play_services_the_panel_says_so()
    {
        _authorizer.IsAvailable = false;

        var cut = Render<GooglePanel>();

        Assert.NotNull(cut.Find("[data-testid=google-unavailable]"));
        Assert.Empty(cut.FindAll("[data-testid=google-connect]"));
    }

    [Fact]
    public void Connecting_shows_the_granted_permissions_and_the_account()
    {
        _google.On("GET", "/gmail/v1/users/me/profile", HttpStatusCode.OK, new { emailAddress = "alex@example.com" });
        var cut = Render<GooglePanel>();

        cut.Find("[data-testid=google-skip-to-connect]").Click();
        cut.Find("[data-testid=google-connect]").Click();

        var status = cut.WaitForElement("[data-testid=google-status]");
        Assert.Contains("como alex@example.com", status.TextContent, StringComparison.Ordinal);
        Assert.True(Assert.Single(_authorizer.Requests).Interactive);
    }

    [Fact]
    public async Task With_google_connected_interviews_can_go_to_the_jobpilot_calendar_and_follow_ups_to_gmail_drafts()
    {
        Assert.True((await Services.GetRequiredService<GoogleAuthService>().ConnectAsync(GoogleAccess.Full, Ct)).IsSuccess);
        var store = Services.GetRequiredService<ApplicationStore>();
        var id = await store.StartAsync(_postingId, ApplicationStage.Interview, Ct);
        await store.ScheduleInterviewAsync(id, DateTime.UtcNow.AddDays(2), 60, "Video call", Ct);

        var cut = Render<ApplicationPage>(p => p.Add(c => c.Id, id));

        cut.WaitForElements(".mud-chipset .mud-chip").Single(c => c.TextContent.Trim() == "Entrevistas").Click();
        cut.WaitForAssertion(() => Assert.Contains("Agregar a Google Calendar", cut.Markup, StringComparison.Ordinal));
        cut.FindAll(".mud-chipset .mud-chip").Single(c => c.TextContent.Trim() == "Correo").Click();
        cut.WaitForAssertion(() => Assert.Contains("Buscar en Gmail", cut.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public void The_wizard_shows_this_installs_package_and_sha1_for_the_android_client()
    {
        _signing.Certificate = "abc"u8.ToArray();
        var cut = Render<GooglePanel>();

        Assert.Contains("jp-step-current", cut.Find("[data-testid=google-step-1]").ClassName, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[data-testid=google-connect]"));
        for (var i = 0; i < 3; i++)
        {
            cut.Find("[data-testid=google-step-next]").Click();
        }

        Assert.Equal("com.vidi.jobpilot", cut.Find("[data-testid=google-package]").TextContent);
        // SHA-1 of "abc", formatted as Google Cloud and keytool show it.
        Assert.Equal("A9:99:3E:36:47:06:81:6A:BA:3E:25:71:78:50:C2:6C:9C:D0:D8:9D", cut.Find("[data-testid=google-sha1]").TextContent);
        cut.Find("[data-testid=google-sha1-copy]").Click();
        Assert.Equal("A9:99:3E:36:47:06:81:6A:BA:3E:25:71:78:50:C2:6C:9C:D0:D8:9D", Assert.Single(_share.Copied));

        cut.Find("[data-testid=google-step-next]").Click();
        Assert.NotNull(cut.Find("[data-testid=google-connect]"));
    }

    [Fact]
    public void When_the_signature_cannot_be_read_the_wizard_says_where_to_find_it()
    {
        var cut = Render<GooglePanel>();

        cut.Find("[data-testid=google-step-4] button").Click();

        Assert.Contains("keytool", cut.Find("[data-testid=google-sha1-missing]").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_a_host_implementation_the_app_still_knows_its_package_name()
    {
        using var provider = new ServiceCollection().AddJobPilotMobile(MobileConfiguration.Load(), new MobilePaths(_directory.Path)).BuildServiceProvider();

        var signing = provider.GetRequiredService<IAppSigningInfo>();

        Assert.Equal("com.vidi.jobpilot", signing.PackageName);
        Assert.Null(signing.SigningCertificate());
    }
}

internal sealed class FakeAppSigningInfo : IAppSigningInfo
{
    public byte[]? Certificate { get; set; }

    public string PackageName => SigningFingerprint.PackageName;

    public byte[]? SigningCertificate() => Certificate;
}
