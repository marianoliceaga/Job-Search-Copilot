using JobPilot.Domain.Llm;
using JobPilot.Mobile.Core;
using JobPilot.Mobile.Core.Infrastructure;
using JobPilot.Mobile.Core.Infrastructure.Persistence;
using JobPilot.Mobile.Tests.TestSupport;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JobPilot.Mobile.Tests.Persistence;

public class MigrationTests
{
    [Fact]
    public async Task The_app_initializer_creates_the_database_and_matches_the_model()
    {
        using var directory = new TempDirectory();
        var paths = new MobilePaths(Path.Combine(directory.Path, "files"));
        await using var services = new ServiceCollection().AddLogging()
            .AddJobPilotMobile(MobileConfiguration.Load(), paths)
            .AddSingleton<JobPilot.Mobile.Core.Infrastructure.Notifications.INotificationScheduler>(new FakeNotificationScheduler())
            .AddSingleton<JobPilot.Mobile.Core.Infrastructure.Google.IGoogleAuthorizer>(new FakeGoogleAuthorizer())
            .BuildServiceProvider();
        var ct = TestContext.Current.CancellationToken;

        var initializer = services.GetRequiredService<AppInitializer>();
        await initializer.EnsureInitializedAsync();
        await initializer.EnsureInitializedAsync();

        Assert.True(File.Exists(paths.DatabasePath));
        var factory = services.GetRequiredService<IDbContextFactory<MobileDbContext>>();
        await using (var db = await factory.CreateDbContextAsync(ct))
        {
            Assert.False(db.Database.HasPendingModelChanges(), "The model has changes without a migration: run 'dotnet ef migrations add'.");
            Assert.Empty(await db.Database.GetPendingMigrationsAsync(ct));

            db.LlmCalls.Add(new LlmCallRecord { UseCase = "connection-test", Model = "m", PromptId = "connection-test.v1", StartedAtUtc = new DateTime(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc), EstimatedCostUsd = 0.000123m });
            await db.SaveChangesAsync(ct);
        }

        await using (var db = await factory.CreateDbContextAsync(ct))
        {
            var call = await db.LlmCalls.SingleAsync(ct);
            Assert.Equal(DateTimeKind.Utc, call.StartedAtUtc.Kind);
            Assert.Equal(0.000123m, call.EstimatedCostUsd);
        }

        SqliteConnection.ClearAllPools();
    }

    [Fact]
    public async Task Settings_documents_round_trip_as_json()
    {
        await using var host = await LlmTestHost.CreateAsync();
        var store = host.Services.GetRequiredService<SettingsDocumentStore>();
        var ct = TestContext.Current.CancellationToken;

        await store.SetAsync("demo", new Demo("a", [1, 2]), ct);
        await store.SetAsync("demo", new Demo("b", [3]), ct);

        Assert.Equal(new Demo("b", [3]).Name, (await store.GetAsync<Demo>("demo", ct))!.Name);
        Assert.Null(await store.GetAsync<Demo>("missing", ct));
    }

    private sealed record Demo(string Name, IReadOnlyList<int> Values);
}
