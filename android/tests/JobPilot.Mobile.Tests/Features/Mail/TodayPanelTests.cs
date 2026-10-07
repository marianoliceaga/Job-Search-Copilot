using System.Net;
using Bunit;
using JobPilot.Mobile.Core;
using JobPilot.Mobile.Core.Features.Dashboard;
using JobPilot.Mobile.Core.Features.Mail;
using JobPilot.Mobile.Core.Infrastructure;
using JobPilot.Mobile.Core.Infrastructure.Files;
using JobPilot.Mobile.Core.Infrastructure.Google;
using JobPilot.Mobile.Core.Infrastructure.Notifications;
using JobPilot.Mobile.Core.Infrastructure.Secrets;
using JobPilot.Mobile.Tests.Google;
using JobPilot.Mobile.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace JobPilot.Mobile.Tests.Features.Mail;

/// <summary>The "Hoy" brief on the phone's Panel: on demand, read-only, sized for a phone.</summary>
public sealed class TodayPanelTests : BunitContext, IAsyncLifetime
{
    private readonly TempDirectory _directory = new();
    private readonly FakeGoogleAuthorizer _authorizer = new();
    private readonly FakeGoogleHandler _google = new();

    public TodayPanelTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
        Services.AddMudServices();
        Services.AddJobPilotMobile(MobileConfiguration.Load(), new MobilePaths(_directory.Path));
        Services.AddSingleton<ISecretStore>(new InMemorySecretStore());
        Services.AddSingleton<INotificationScheduler>(new FakeNotificationScheduler());
        Services.AddSingleton<IGoogleAuthorizer>(_authorizer);
        Services.AddSingleton<IFilePickerService>(new FakeFilePicker());
        Services.AddSingleton<IShareService>(new FakeShareService());
        Services.ConfigureHttpClientDefaults(builder => builder.ConfigurePrimaryHttpMessageHandler(() => _google));
    }

    private static CancellationToken Ct => Xunit.TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await Services.GetRequiredService<AppInitializer>().EnsureInitializedAsync();

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await base.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        _directory.Dispose();
    }

    [Fact]
    public void Without_Gmail_permission_the_Panel_explains_how_to_connect_and_reads_nothing()
    {
        var cut = Render<DashboardPage>();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid=today]")));
        Assert.Contains("/configuracion?seccion=google", cut.Markup, StringComparison.Ordinal);
        Assert.True(UpdateButton(cut).HasAttribute("disabled"));
        Assert.Empty(_google.Requests);
    }

    [Fact]
    public async Task Updating_shows_waiting_cards_alert_roles_and_filter_examples_on_tap()
    {
        _authorizer.Returns(new GoogleAuthorization("access-1", GoogleScopes.All, false, null));
        Assert.True((await Services.GetRequiredService<GoogleAuthService>().ConnectAsync(GoogleAccess.Full, Ct)).IsSuccess);
        MailCheckServiceTests.ScriptInbox(_google, TimeProvider.System.GetUtcNow());
        var cut = Render<DashboardPage>();
        cut.WaitForAssertion(() => Assert.False(UpdateButton(cut).HasAttribute("disabled")));

        UpdateButton(cut).Click();

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=waiting-card]")), TimeSpan.FromSeconds(10));
        var role = Assert.Single(cut.FindAll("[data-testid=alert-role]"));
        Assert.Contains("Senior .NET Engineer", role.TextContent, StringComparison.Ordinal);
        Assert.Contains("/ofertas/", role.InnerHtml, StringComparison.Ordinal);
        Assert.All(_google.Requests, r => Assert.Equal(HttpMethod.Get, r.Method));

        // No hover on a phone: tapping a filter chip shows its examples.
        Assert.Empty(cut.FindAll("[data-testid=filtered-examples]"));
        cut.FindAll("[data-testid=filtered-chip]")[0].Click();
        Assert.Single(cut.FindAll("[data-testid=filtered-examples]"));

        cut.FindAll("[data-testid=waiting-card] button").First(b => b.TextContent.Trim() == "Listo").Click();
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid=waiting-card]")));
        Assert.Contains("Nada te espera.", cut.Markup, StringComparison.Ordinal);
    }

    private static AngleSharp.Dom.IElement UpdateButton(IRenderedComponent<DashboardPage> cut) =>
        cut.FindAll("[data-testid=today] button").First(b => b.TextContent.Contains("Actualizar correo", StringComparison.Ordinal));
}
