using Bunit;
using JobPilot.Domain.Cv;
using JobPilot.Domain.Jobs;
using JobPilot.Mobile.Core;
using JobPilot.Mobile.Core.Features.Dashboard;
using JobPilot.Mobile.Core.Features.Jobs;
using JobPilot.Mobile.Core.Features.Profile;
using JobPilot.Mobile.Core.Infrastructure;
using JobPilot.Mobile.Core.Infrastructure.Secrets;
using JobPilot.Mobile.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace JobPilot.Mobile.Tests.Features.Dashboard;

/// <summary>A fresh install starts without anyone's data: the Panel lists what is missing.</summary>
public sealed class FirstStepsPanelTests : BunitContext, IAsyncLifetime
{
    private readonly TempDirectory _directory = new();
    private readonly InMemorySecretStore _secrets = new();

    public FirstStepsPanelTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
        Services.AddMudServices();
        Services.AddJobPilotMobile(MobileConfiguration.Load(new Dictionary<string, string?>()), new MobilePaths(_directory.Path));
        Services.AddSingleton<ISecretStore>(_secrets);
        Services.AddSingleton<JobPilot.Mobile.Core.Infrastructure.Notifications.INotificationScheduler>(new FakeNotificationScheduler());
        Services.AddSingleton<JobPilot.Mobile.Core.Infrastructure.Google.IGoogleAuthorizer>(new FakeGoogleAuthorizer());
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
    public void A_fresh_install_lists_the_key_the_profile_the_cv_and_the_import()
    {
        var cut = Render<FirstStepsPanel>();

        cut.WaitForElement("[data-testid=first-steps]");
        Assert.Equal("/configuracion?seccion=gemini", cut.Find("[data-testid=first-steps-key]").GetAttribute("href"));
        Assert.Equal("/perfil", cut.Find("[data-testid=first-steps-profile]").GetAttribute("href"));
        Assert.Equal("/perfil", cut.Find("[data-testid=first-steps-cv]").GetAttribute("href"));
        Assert.Equal("/ofertas?seccion=preferencias", cut.Find("[data-testid=first-steps-keywords]").GetAttribute("href"));
        Assert.Contains("/configuracion?seccion=datos", cut.Find("[data-testid=first-steps-import]").InnerHtml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Done_steps_are_checked_and_the_import_hint_goes_once_the_profile_is_filled_in()
    {
        await _secrets.SetAsync(GeminiKeyStore.Key, "AIza-test-key");
        var profiles = Services.GetRequiredService<ProfileStore>();
        var profile = await profiles.GetAsync(Ct);
        profile.Headline = "Backend Engineer";
        await profiles.SaveAsync(profile, Ct);

        var cut = Render<FirstStepsPanel>();

        cut.WaitForElement("[data-testid=first-steps]");
        Assert.Empty(cut.FindAll("[data-testid=first-steps-key]"));
        Assert.Empty(cut.FindAll("[data-testid=first-steps-profile]"));
        Assert.Single(cut.FindAll("[data-testid=first-steps-cv]"));
        Assert.Empty(cut.FindAll("[data-testid=first-steps-import]"));
    }

    [Fact]
    public async Task Nothing_is_shown_when_everything_is_in_place()
    {
        await _secrets.SetAsync(GeminiKeyStore.Key, "AIza-test-key");
        var profiles = Services.GetRequiredService<ProfileStore>();
        var profile = await profiles.GetAsync(Ct);
        profile.Headline = "Backend Engineer";
        await profiles.SaveAsync(profile, Ct);
        await Services.GetRequiredService<MasterCvStore>().SaveAsync(new MasterCv { Headline = "Backend Engineer" }, "manual", Ct);
        await Services.GetRequiredService<JobPreferencesStore>().SaveAsync(new JobSearchPreferences { Keywords = ["c#"] }, Ct);

        var cut = Render<FirstStepsPanel>();

        cut.WaitForState(() => cut.Instance.IsLoaded);
        Assert.Empty(cut.FindAll("[data-testid=first-steps]"));
    }
}
