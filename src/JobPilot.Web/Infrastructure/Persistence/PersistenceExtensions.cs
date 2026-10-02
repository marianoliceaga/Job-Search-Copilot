using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace JobPilot.Web.Infrastructure.Persistence;

public static class PersistenceExtensions
{
    public static IServiceCollection AddJobPilotPersistence(this IServiceCollection services, DataPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var connectionString = new SqliteConnectionStringBuilder { DataSource = paths.DatabasePath }.ToString();
        services.AddDbContextFactory<JobPilotDbContext>(options => options.UseSqlite(connectionString));
        return services;
    }

    /// <summary>Creates the data folder and applies pending migrations (local single-user app).</summary>
    public static async Task InitializeDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(services.GetRequiredService<DataPaths>().DataDirectory);

        var factory = services.GetRequiredService<IDbContextFactory<JobPilotDbContext>>();
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await db.Database.MigrateAsync(cancellationToken);
    }
}
