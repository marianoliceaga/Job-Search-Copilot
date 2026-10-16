using Bunit;
using JobPilot.Domain.Llm;
using JobPilot.Web.Features.Settings;
using JobPilot.Web.Infrastructure;
using JobPilot.Web.Infrastructure.Llm;
using JobPilot.Web.Infrastructure.Persistence;
using JobPilot.Web.Tests.TestSupport;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace JobPilot.Web.Tests.Llm;

/// <summary>The Gemini API key pasted in Configuración: stored encrypted, used when configuration has none.</summary>
public sealed class GeminiKeyStoreTests : BunitContext
{
    private const string PastedKey = "pasted-test-key";

    private readonly TempDirectory _data = new();

    private void AddServices(string? configuredKey = null)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddLogging();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Gemini:ApiKey"] = configuredKey ?? string.Empty,
            ["Llm:DefaultModel"] = "gemini-test-flash",
        }).Build();
        var paths = new DataPaths(new JobPilotOptions { DataDirectory = _data.Path });
        Services.AddSingleton(paths);
        Services.AddSingleton(TimeProvider.System);
        Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        Services.AddJobPilotPersistence(paths);
        Services.AddLlm(configuration, Path.Combine(AppContext.BaseDirectory, "Prompts"));
        Services.AddTransient<ConnectionTestHandler>();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        SqliteConnection.ClearAllPools();
        _data.Dispose();
    }

    [Theory]
    [InlineData("", "Pegá la API key")]
    [InlineData("two words", "no puede tener espacios")]
    public void Incomplete_keys_are_rejected(string value, string expected)
    {
        AddServices();
        var keys = Services.GetRequiredService<GeminiKeyStore>();

        Assert.Contains(expected, keys.Save(value), StringComparison.Ordinal);
        Assert.Equal(GeminiKeySource.None, keys.Source);
        Assert.False(File.Exists(keys.FilePath));
    }

    [Fact]
    public void The_key_is_stored_encrypted_and_can_be_deleted()
    {
        AddServices();
        var keys = Services.GetRequiredService<GeminiKeyStore>();

        Assert.Null(keys.Save($"  {PastedKey} "));

        Assert.Equal((GeminiKeySource.App, PastedKey), (keys.Source, keys.ApiKey));
        Assert.DoesNotContain(PastedKey, File.ReadAllText(keys.FilePath), StringComparison.Ordinal);
        keys.Delete();
        Assert.Equal((GeminiKeySource.None, (string?)null), (keys.Source, keys.ApiKey));
    }

    [Fact]
    public void Configuration_wins_over_the_pasted_key()
    {
        AddServices(configuredKey: "configured-key");
        var keys = Services.GetRequiredService<GeminiKeyStore>();

        keys.Save(PastedKey);

        Assert.Equal((GeminiKeySource.Configuration, "configured-key"), (keys.Source, keys.ApiKey));
    }

    [Fact]
    public async Task The_gemini_client_sends_the_pasted_key_when_configuration_has_none()
    {
        await using var host = await LlmTestHost.CreateAsync(new() { ["Gemini:ApiKey"] = string.Empty });
        var keys = host.Services.GetRequiredService<GeminiKeyStore>();
        var request = new LlmRequest<ConnectionTestResponse>(new ConnectionTestHandler(host.Services.GetRequiredService<PromptLibrary>(), host.Client).Prepare().Prompt);

        var missing = await host.Client.GenerateAsync(request, Xunit.TestContext.Current.CancellationToken);
        keys.Save(PastedKey);
        host.Handler.RespondJson(new { status = "ok", echo = "word" });
        await host.Client.GenerateAsync(request, Xunit.TestContext.Current.CancellationToken);

        Assert.Equal(LlmErrorKind.Configuration, missing.Error!.Kind);
        Assert.Contains("Configuración", missing.Error.Message, StringComparison.Ordinal);
        Assert.Equal(PastedKey, Assert.Single(host.Handler.Requests).ApiKeyHeader);
    }

    [Fact]
    public async Task The_panel_saves_the_pasted_key_and_never_shows_it()
    {
        AddServices();
        await Services.InitializeDatabaseAsync(Xunit.TestContext.Current.CancellationToken);
        var cut = Render<GeminiConnectionPanel>();
        Assert.Contains("No hay API key", cut.Find("[data-testid=api-key-status]").TextContent, StringComparison.Ordinal);

        cut.Find("input[data-testid=api-key-input]").Input(PastedKey);
        cut.Find("[data-testid=save-key]").Click();

        Assert.Equal(PastedKey, Services.GetRequiredService<GeminiKeyStore>().ApiKey);
        Assert.Contains("guardada en esta PC", cut.Find("[data-testid=api-key-status]").TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain(PastedKey, cut.Markup, StringComparison.Ordinal);
        Assert.NotNull(cut.Find("[data-testid=remove-key]"));
    }

    [Fact]
    public async Task With_a_configured_key_the_panel_has_no_key_field()
    {
        AddServices(configuredKey: "configured-key");
        await Services.InitializeDatabaseAsync(Xunit.TestContext.Current.CancellationToken);
        var cut = Render<GeminiConnectionPanel>();

        Assert.Contains("user-secrets", cut.Find("[data-testid=api-key-status]").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[data-testid=api-key-input]"));
    }
}
