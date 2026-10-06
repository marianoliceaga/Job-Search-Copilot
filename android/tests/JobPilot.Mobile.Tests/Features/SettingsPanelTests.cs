using System.Text.RegularExpressions;
using Bunit;
using JobPilot.Mobile.Core;
using JobPilot.Mobile.Core.Features.Settings;
using JobPilot.Mobile.Core.Infrastructure;
using JobPilot.Mobile.Core.Infrastructure.Llm;
using JobPilot.Mobile.Core.Infrastructure.Secrets;
using JobPilot.Mobile.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace JobPilot.Mobile.Tests.Features;

public sealed partial class SettingsPanelTests : BunitContext, IAsyncLifetime
{
    private readonly TempDirectory _directory = new();
    private readonly InMemorySecretStore _secrets = new();
    private readonly FakeGeminiHandler _gemini = new();

    public SettingsPanelTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
        Services.AddMudServices();
        Services.AddJobPilotMobile(
            MobileConfiguration.Load(new Dictionary<string, string?> { ["Gemini:BaseUrl"] = "https://gemini.test/v1beta/", ["Gemini:MaxRetryAttempts"] = "1" }),
            new MobilePaths(_directory.Path));
        Services.AddSingleton<ISecretStore>(_secrets);
        Services.AddHttpClient<GeminiLlmClient>().ConfigurePrimaryHttpMessageHandler(() => _gemini);
    }

    public async ValueTask InitializeAsync() => await Services.GetRequiredService<AppInitializer>().EnsureInitializedAsync();

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await base.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        _directory.Dispose();
    }

    [Fact]
    public void Without_a_key_the_owner_pastes_it_and_it_goes_to_secure_storage()
    {
        var cut = Render<GeminiConnectionPanel>();
        Assert.Contains("No hay API key", cut.Find("[data-testid=api-key-status]").TextContent, StringComparison.Ordinal);
        Assert.True(cut.Find("[data-testid=run-test]").HasAttribute("disabled"));

        cut.Find("input[type=password]").Input(" AIza-owner-key ");
        cut.Find("[data-testid=save-key]").Click();

        cut.WaitForAssertion(() => Assert.Contains("está guardada", cut.Find("[data-testid=api-key-status]").TextContent, StringComparison.Ordinal));
        Assert.Equal("AIza-owner-key", _secrets.Values[GeminiKeyStore.Key]);
        Assert.DoesNotContain("AIza-owner-key", cut.Markup, StringComparison.Ordinal);
        Assert.False(cut.Find("[data-testid=run-test]").HasAttribute("disabled"));
    }

    [Fact]
    public async Task The_connection_test_shows_what_is_sent_and_reports_the_result()
    {
        await _secrets.SetAsync(GeminiKeyStore.Key, "AIza-owner-key");
        var cut = Render<GeminiConnectionPanel>();
        var userMessage = cut.WaitForElement("[data-testid=payload-user]").TextContent;
        var word = VerificationWord().Match(userMessage).Value;
        _gemini.RespondJson(new { status = "ok", echo = word });

        cut.Find("[data-testid=run-test]").Click();

        cut.WaitForAssertion(() => Assert.Contains("Conexión correcta", cut.Find("[data-testid=connection-result]").TextContent, StringComparison.Ordinal));
        var request = Assert.Single(_gemini.Requests);
        Assert.Equal("AIza-owner-key", request.ApiKeyHeader);
        Assert.Contains(word, request.Body, StringComparison.Ordinal);
        Assert.Single(await Services.GetRequiredService<DbLlmCallLog>().GetRecentAsync(10, Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public void A_rejected_key_is_reported_in_spanish()
    {
        _secrets.Values[GeminiKeyStore.Key] = "AIza-wrong";
        _gemini.Respond(System.Net.HttpStatusCode.BadRequest,
            """{"error":{"code":400,"message":"API key not valid.","status":"INVALID_ARGUMENT","details":[{"reason":"API_KEY_INVALID"}]}}""");
        var cut = Render<GeminiConnectionPanel>();

        cut.WaitForElement("[data-testid=run-test]:not([disabled])").Click();

        cut.WaitForAssertion(() => Assert.Contains("Gemini rechazó la API key", cut.Find("[data-testid=connection-result]").TextContent, StringComparison.Ordinal));
    }

    [Fact]
    public void The_settings_page_has_the_gemini_models_and_log_tabs()
    {
        var cut = Render<SettingsPage>();

        Assert.Equal(["Gemini", "Modelos", "Registro"], cut.FindAll(".mud-tab").Select(t => t.TextContent.Trim()));
    }

    [GeneratedRegex("jp[0-9a-f]{8}")]
    private static partial Regex VerificationWord();
}
