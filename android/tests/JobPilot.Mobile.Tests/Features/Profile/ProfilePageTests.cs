using Bunit;
using JobPilot.Mobile.Core;
using JobPilot.Mobile.Core.Features.Profile;
using JobPilot.Mobile.Core.Features.Profile.Import;
using JobPilot.Mobile.Core.Infrastructure;
using JobPilot.Mobile.Core.Infrastructure.Files;
using JobPilot.Mobile.Core.Infrastructure.Llm;
using JobPilot.Mobile.Core.Infrastructure.Secrets;
using JobPilot.Mobile.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace JobPilot.Mobile.Tests.Features.Profile;

/// <summary>The import flow on the phone: Android's picker → local extraction → preview → Gemini → review → save.</summary>
public sealed class ProfilePageTests : BunitContext, IAsyncLifetime
{
    private readonly TempDirectory _directory = new();
    private readonly InMemorySecretStore _secrets = new();
    private readonly FakeGeminiHandler _gemini = new();
    private readonly FakeFilePicker _picker = new();

    public ProfilePageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
        Services.AddMudServices();
        Services.AddJobPilotMobile(
            MobileConfiguration.Load(new Dictionary<string, string?> { ["Gemini:BaseUrl"] = "https://gemini.test/v1beta/", ["Gemini:MaxRetryAttempts"] = "1" }),
            new MobilePaths(_directory.Path));
        Services.AddSingleton<ISecretStore>(_secrets);
        Services.AddSingleton<JobPilot.Mobile.Core.Infrastructure.Notifications.INotificationScheduler>(new FakeNotificationScheduler());
        Services.AddSingleton<JobPilot.Mobile.Core.Infrastructure.Google.IGoogleAuthorizer>(new FakeGoogleAuthorizer());
        Services.AddSingleton<IFilePickerService>(_picker);
        Services.AddHttpClient<GeminiLlmClient>().ConfigurePrimaryHttpMessageHandler(() => _gemini);
    }

    public async ValueTask InitializeAsync()
    {
        await _secrets.SetAsync(GeminiKeyStore.Key, "AIza-owner-key");
        await Services.GetRequiredService<AppInitializer>().EnsureInitializedAsync();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await base.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        _directory.Dispose();
    }

    private static PickedFile Fixture(string name) => new(name, File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Cv", name)));

    [Fact]
    public async Task A_picked_cv_is_extracted_locally_previewed_structured_and_saved_as_version_1()
    {
        _picker.Next = new PickResult(Fixture("fictional-cv.docx"));
        var cut = Render<MasterCvPanel>();

        cut.WaitForElement("[data-testid=pick-cv]").Click();

        var (_, extensions, maxBytes) = Assert.Single(_picker.Requests);
        Assert.Equal(CvTextExtractor.SupportedExtensions, extensions);
        Assert.Equal(CvTextExtractor.MaxFileBytes, maxBytes);
        cut.WaitForAssertion(() => Assert.Contains("Northwind Bank", cut.Find("[data-testid=payload-user]").TextContent, StringComparison.Ordinal));
        Assert.Empty(_gemini.Requests);

        _gemini.RespondJson(CvImportHandlerTests.FaithfulImportForTests());
        cut.Find("[data-testid=run-import]").Click();
        cut.WaitForElement("[data-testid=save-import]").Click();

        var stored = await Services.GetRequiredService<MasterCvStore>().GetAsync(Xunit.TestContext.Current.CancellationToken);
        Assert.Equal(1, stored!.Version);
        Assert.Equal("Northwind Bank", stored.Cv.Experiences[0].Company);
        Assert.Equal("import: fictional-cv.docx", Assert.Single(await Services.GetRequiredService<MasterCvStore>().GetVersionsAsync(Xunit.TestContext.Current.CancellationToken)).Source);
    }

    [Fact]
    public void Cancelling_the_picker_changes_nothing_and_picker_errors_are_shown()
    {
        var cut = Render<MasterCvPanel>();

        cut.WaitForElement("[data-testid=pick-cv]").Click();
        Assert.Empty(cut.FindAll("[data-testid=import-error]"));
        Assert.Empty(cut.FindAll("[data-testid=payload-user]"));

        _picker.Next = new PickResult(null, "El archivo supera los 10 MB.");
        cut.Find("[data-testid=pick-cv]").Click();
        cut.WaitForAssertion(() => Assert.Equal("El archivo supera los 10 MB.", cut.Find("[data-testid=import-error]").TextContent.Trim()));
    }

    [Fact]
    public void Unsupported_files_are_rejected_before_anything_is_sent()
    {
        _picker.Next = new PickResult(new PickedFile("photo.jpg", [1, 2, 3]));
        var cut = Render<MasterCvPanel>();

        cut.WaitForElement("[data-testid=pick-cv]").Click();

        cut.WaitForAssertion(() => Assert.Contains("Formato no soportado", cut.Find("[data-testid=import-error]").TextContent, StringComparison.Ordinal));
        Assert.Empty(_gemini.Requests);
    }

    [Fact]
    public void The_profile_page_has_the_profile_and_master_cv_tabs_and_starts_from_the_seed()
    {
        var cut = Render<ProfilePage>();

        Assert.Equal(["Perfil", "CV maestro"], cut.FindAll(".mud-tab").Select(t => t.TextContent.Trim()));
        cut.WaitForAssertion(() => Assert.Contains(new ProfileSeed().Load().Headline, cut.Markup, StringComparison.Ordinal));
    }
}
