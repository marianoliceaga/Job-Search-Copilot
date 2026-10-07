using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JobPilot.Mobile.Core.Infrastructure.Persistence;

public static class PersistenceExtensions
{
    public static IServiceCollection AddMobilePersistence(this IServiceCollection services, MobilePaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var connectionString = new SqliteConnectionStringBuilder { DataSource = paths.DatabasePath }.ToString();
        services.AddDbContextFactory<MobileDbContext>(options => options.UseSqlite(connectionString));
        return services;
    }

    /// <summary>Creates the data folder and applies pending migrations (on every app start, before the UI loads).</summary>
    public static async Task InitializeDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(services.GetRequiredService<MobilePaths>().DataDirectory);

        var factory = services.GetRequiredService<IDbContextFactory<MobileDbContext>>();
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await db.Database.MigrateAsync(cancellationToken);
    }
}
