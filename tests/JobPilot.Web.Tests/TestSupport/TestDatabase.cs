using JobPilot.Web.Infrastructure;
using JobPilot.Web.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JobPilot.Web.Tests.TestSupport;

/// <summary>Migrated SQLite database in a temporary folder.</summary>
public sealed class TestDatabase : IAsyncDisposable
{
    private readonly TempDirectory _directory;
    private readonly ServiceProvider _services;

    public IDbContextFactory<JobPilotDbContext> Factory => _services.GetRequiredService<IDbContextFactory<JobPilotDbContext>>();

    public string DirectoryPath => _directory.Path;

    public static async Task<TestDatabase> CreateAsync()
    {
        var directory = new TempDirectory();
        var paths = new DataPaths(new JobPilotOptions { DataDirectory = directory.Path });
        var services = new ServiceCollection().AddSingleton(paths).AddJobPilotPersistence(paths).BuildServiceProvider();
        await services.InitializeDatabaseAsync();
        return new TestDatabase(services, directory);
    }

    private TestDatabase(ServiceProvider services, TempDirectory directory)
    {
        _services = services;
        _directory = directory;
    }

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();

        SqliteConnection.ClearAllPools();
        _directory.Dispose();
    }
}
