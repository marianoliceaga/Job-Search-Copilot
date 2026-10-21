using JobPilot.Domain.Llm;
using JobPilot.Web.Infrastructure;
using JobPilot.Web.Infrastructure.Llm;
using JobPilot.Web.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace JobPilot.Web.Tests.TestSupport;

/// <summary>
/// Real LLM service registrations (options, resilience pipeline, call log on a temporary SQLite database)
/// with the HTTP transport replaced by <see cref="FakeGeminiHandler"/>.
/// </summary>
public sealed class LlmTestHost : IAsyncDisposable
{
    public const string ApiKey = "test-api-key";
    public const string DefaultModel = "gemini-test-flash";

    private readonly TempDirectory _directory;
    private readonly ServiceProvider _services;

    private LlmTestHost(TempDirectory directory, ServiceProvider services, FakeGeminiHandler handler)
    {
        _directory = directory;
        _services = services;
        Handler = handler;
    }

    public FakeGeminiHandler Handler { get; }

    public IServiceProvider Services => _services;

    public ILlmClient Client => _services.GetRequiredService<ILlmClient>();

    public static async Task<LlmTestHost> CreateAsync(Dictionary<string, string?>? overrides = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Gemini:ApiKey"] = ApiKey,
            ["Gemini:BaseUrl"] = "https://gemini.test/v1beta/",
            ["Gemini:MaxRetryAttempts"] = "2",
            ["Gemini:RetryBaseDelaySeconds"] = "0",
            ["Gemini:AttemptTimeoutSeconds"] = "0.5",
            ["Gemini:TotalTimeoutSeconds"] = "5",
            ["Llm:DefaultModel"] = DefaultModel,
            ["Llm:MaxInvalidOutputRetries"] = "1",
            ["Llm:Pricing:gemini-test-flash:InputPerMillionUsd"] = "1",
            ["Llm:Pricing:gemini-test-flash:OutputPerMillionUsd"] = "10",
        };
        foreach (var (key, value) in overrides ?? [])
        {
            settings[key] = value;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var handler = new FakeGeminiHandler();
        var directory = new TempDirectory();

        var services = new ServiceCollection();
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton(TimeProvider.System);
        var paths = new DataPaths(new JobPilotOptions { DataDirectory = directory.Path });
        services.AddSingleton(paths);
        services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        services.AddJobPilotPersistence(paths);
        services.AddSingleton<IGeminiSecretsFile>(new FixedGeminiSecretsFile(settings["Gemini:ApiKey"]));
        services.AddLlm(configuration, Path.Combine(AppContext.BaseDirectory, "Prompts"));
        services.AddHttpClient<GeminiLlmClient>().ConfigurePrimaryHttpMessageHandler(() => handler);

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        await provider.InitializeDatabaseAsync();

        return new LlmTestHost(directory, provider, handler);
    }

    public async Task<IReadOnlyList<LlmCallRecord>> CallRecordsAsync()
    {
        var factory = _services.GetRequiredService<IDbContextFactory<JobPilotDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        return await db.LlmCalls.AsNoTracking().OrderBy(c => c.Id).ToListAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        _directory.Dispose();
    }
}
