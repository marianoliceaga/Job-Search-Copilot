using JobPilot.Domain.Llm;
using JobPilot.Mobile.Core;
using JobPilot.Mobile.Core.Infrastructure;
using JobPilot.Mobile.Core.Infrastructure.Llm;
using JobPilot.Mobile.Core.Infrastructure.Persistence;
using JobPilot.Mobile.Core.Infrastructure.Secrets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace JobPilot.Mobile.Tests.TestSupport;

/// <summary>
/// The app's real service registrations (embedded web defaults, resilience pipeline, call log on a temporary SQLite
/// database) with the HTTP transport replaced by <see cref="FakeGeminiHandler"/> and secure storage by
/// <see cref="InMemorySecretStore"/>.
/// </summary>
public sealed class LlmTestHost : IAsyncDisposable
{
    public const string ApiKey = "test-api-key";
    public const string DefaultModel = "gemini-test-flash";

    private readonly TempDirectory _directory;
    private readonly ServiceProvider _services;

    private LlmTestHost(TempDirectory directory, ServiceProvider services, FakeGeminiHandler handler, InMemorySecretStore secrets)
    {
        _directory = directory;
        _services = services;
        Handler = handler;
        Secrets = secrets;
    }

    public FakeGeminiHandler Handler { get; }

    public InMemorySecretStore Secrets { get; }

    public IServiceProvider Services => _services;

    public ILlmClient Client => _services.GetRequiredService<ILlmClient>();

    /// <param name="apiKey">Key in secure storage; null for none.</param>
    public static async Task<LlmTestHost> CreateAsync(Dictionary<string, string?>? overrides = null, string? apiKey = ApiKey)
    {
        var settings = new Dictionary<string, string?>
        {
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

        var handler = new FakeGeminiHandler();
        var secrets = new InMemorySecretStore();
        if (apiKey is not null)
        {
            await secrets.SetAsync(GeminiKeyStore.Key, apiKey);
        }

        var directory = new TempDirectory();
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        services.AddJobPilotMobile(MobileConfiguration.Load(settings), new MobilePaths(directory.Path));
        services.AddSingleton<ISecretStore>(secrets);
        services.AddSingleton<JobPilot.Mobile.Core.Infrastructure.Notifications.INotificationScheduler>(new FakeNotificationScheduler());
        services.AddSingleton<JobPilot.Mobile.Core.Infrastructure.Google.IGoogleAuthorizer>(new FakeGoogleAuthorizer());
        services.AddHttpClient<GeminiLlmClient>().ConfigurePrimaryHttpMessageHandler(() => handler);

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        await provider.GetRequiredService<AppInitializer>().EnsureInitializedAsync();

        return new LlmTestHost(directory, provider, handler, secrets);
    }

    public async Task<IReadOnlyList<LlmCallRecord>> CallRecordsAsync()
    {
        var factory = _services.GetRequiredService<IDbContextFactory<MobileDbContext>>();
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
