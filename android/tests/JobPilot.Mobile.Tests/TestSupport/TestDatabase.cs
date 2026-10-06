using JobPilot.Mobile.Core.Infrastructure;
using JobPilot.Mobile.Core.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JobPilot.Mobile.Tests.TestSupport;

/// <summary>Migrated phone database in a temporary folder.</summary>
public sealed class TestDatabase : IAsyncDisposable
{
    private readonly TempDirectory _directory;
    private readonly ServiceProvider _services;

    private TestDatabase(ServiceProvider services, TempDirectory directory)
    {
        _services = services;
        _directory = directory;
    }

    public IDbContextFactory<MobileDbContext> Factory => _services.GetRequiredService<IDbContextFactory<MobileDbContext>>();

    public static async Task<TestDatabase> CreateAsync()
    {
        var directory = new TempDirectory();
        var paths = new MobilePaths(directory.Path);
        var services = new ServiceCollection().AddSingleton(paths).AddMobilePersistence(paths).BuildServiceProvider();
        await services.InitializeDatabaseAsync();
        return new TestDatabase(services, directory);
    }

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        SqliteConnection.ClearAllPools();
        _directory.Dispose();
    }
}
