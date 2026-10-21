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

/// <summary>The Gemini API key: pasted in Configuración (stored encrypted, wins) or in secrets.json; never from the environment.</summary>
public sealed class GeminiKeyStoreTests : BunitContext
{
    private const string PastedKey = "pasted-test-key";

    private readonly TempDirectory _data = new();

    private void AddServices(string? secretsFileKey = null)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddLogging();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Llm:DefaultModel"] = "gemini-test-flash",
        }).Build();
        var paths = new DataPaths(new JobPilotOptions { DataDirectory = _data.Path });
        Services.AddSingleton(paths);
        Services.AddSingleton(TimeProvider.System);
        Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        Services.AddJobPilotPersistence(paths);
        Services.AddSingleton<IGeminiSecretsFile>(new FixedGeminiSecretsFile(secretsFileKey));
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
    public void The_key_saved_in_the_app_wins_over_secrets_json_and_deleting_it_falls_back()
    {
        AddServices(secretsFileKey: "secrets-file-key");
        var keys = Services.GetRequiredService<GeminiKeyStore>();
        Assert.Equal((GeminiKeySource.SecretsFile, "secrets-file-key"), (keys.Source, keys.ApiKey));

        keys.Save(PastedKey);
        Assert.Equal((GeminiKeySource.App, PastedKey), (keys.Source, keys.ApiKey));

        keys.Delete();
        Assert.Equal((GeminiKeySource.SecretsFile, "secrets-file-key"), (keys.Source, keys.ApiKey));
    }

    [Fact]
    public async Task Environment_variables_and_other_configuration_never_supply_the_key()
    {
        var previous = (Environment.GetEnvironmentVariable("GEMINI_API_KEY"), Environment.GetEnvironmentVariable("Gemini__ApiKey"));
        Environment.SetEnvironmentVariable("GEMINI_API_KEY", "from-environment");
        Environment.SetEnvironmentVariable("Gemini__ApiKey", "from-environment");
        try
        {
            await using var host = await LlmTestHost.CreateAsync(new() { ["Gemini:ApiKey"] = string.Empty });
            var keys = host.Services.GetRequiredService<GeminiKeyStore>();

            Assert.Equal((GeminiKeySource.None, (string?)null), (keys.Source, keys.ApiKey));
        }
        finally
        {
            Environment.SetEnvironmentVariable("GEMINI_API_KEY", previous.Item1);
            Environment.SetEnvironmentVariable("Gemini__ApiKey", previous.Item2);
        }
    }

    [Fact]
    public void The_real_secrets_file_reads_only_its_own_json()
    {
        using var secrets = new UserSecretsGeminiFile();

        Assert.EndsWith(Path.Combine("jobpilot-web-local", "secrets.json"), secrets.FilePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_gemini_client_sends_the_pasted_key()
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
        Assert.Contains("clave guardada desde la app", cut.Find("[data-testid=api-key-status]").TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain(PastedKey, cut.Markup, StringComparison.Ordinal);
        Assert.NotNull(cut.Find("[data-testid=remove-key]"));
    }

    [Fact]
    public async Task With_a_key_in_secrets_json_the_panel_says_so_and_still_lets_the_owner_replace_it()
    {
        AddServices(secretsFileKey: "secrets-file-key");
        await Services.InitializeDatabaseAsync(Xunit.TestContext.Current.CancellationToken);
        var cut = Render<GeminiConnectionPanel>();

        Assert.Contains("secrets.json", cut.Find("[data-testid=api-key-status]").TextContent, StringComparison.Ordinal);
        Assert.Contains("jobpilot-web-local", cut.Find("[data-testid=api-key-secrets-path]").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[data-testid=remove-key]"));

        cut.Find("input[data-testid=api-key-input]").Input(PastedKey);
        cut.Find("[data-testid=save-key]").Click();

        var status = cut.Find("[data-testid=api-key-status]").TextContent;
        Assert.Contains("clave guardada desde la app", status, StringComparison.Ordinal);
        Assert.Contains("También hay una clave en secrets.json", status, StringComparison.Ordinal);
        Assert.Equal(PastedKey, Services.GetRequiredService<GeminiKeyStore>().ApiKey);
        Assert.NotNull(cut.Find("input[data-testid=api-key-input]"));
    }
}
