using JobPilot.Domain.Profiles;
using JobPilot.Web.Infrastructure;
using JobPilot.Web.Infrastructure.Persistence;
using JobPilot.Web.Tests.TestSupport;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JobPilot.Web.Tests.Persistence;

public class MigrationTests
{
    [Fact]
    public async Task Migrations_create_the_database_and_match_the_model()
    {
        using var directory = new TempDirectory();
        var dataDirectory = Path.Combine(directory.Path, "nested", "data");
        var paths = new DataPaths(new JobPilotOptions { DataDirectory = dataDirectory });
        await using var services = new ServiceCollection().AddSingleton(paths).AddJobPilotPersistence(paths).BuildServiceProvider();
        var ct = TestContext.Current.CancellationToken;

        await services.InitializeDatabaseAsync(ct);

        Assert.True(File.Exists(paths.DatabasePath));
        var factory = services.GetRequiredService<IDbContextFactory<JobPilotDbContext>>();
        await using (var db = await factory.CreateDbContextAsync(ct))
        {
            Assert.False(db.Database.HasPendingModelChanges(), "The model has changes without a migration: run 'dotnet ef migrations add'.");
            Assert.Empty(await db.Database.GetPendingMigrationsAsync(ct));

            db.Profiles.Add(new Profile { Headline = "Senior .NET Engineer", Stack = ["C#", "Blazor"], UpdatedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync(ct);
        }

        await using (var db = await factory.CreateDbContextAsync(ct))
        {
            var profile = await db.Profiles.SingleAsync(ct);
            Assert.Equal(["C#", "Blazor"], profile.Stack);
            Assert.Equal(DateTimeKind.Utc, profile.UpdatedAtUtc.Kind);
        }

        SqliteConnection.ClearAllPools();
    }

    [Fact]
    public void Default_data_directory_is_outside_the_repository()
    {
        var paths = new DataPaths(new JobPilotOptions());

        Assert.EndsWith("JobPilot", paths.DataDirectory, StringComparison.Ordinal);
        Assert.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), paths.DataDirectory, StringComparison.Ordinal);
    }
}
