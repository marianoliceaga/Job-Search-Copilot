using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace JobPilot.Web.Infrastructure.Persistence;

/// <summary>Reads and writes JSON documents by key.</summary>
public sealed class SettingsDocumentStore(IDbContextFactory<JobPilotDbContext> dbFactory, TimeProvider time)
{
    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken)
        where T : class
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var document = await db.SettingsDocuments.AsNoTracking().FirstOrDefaultAsync(d => d.Key == key, cancellationToken);
        return document is null ? null : JsonSerializer.Deserialize<T>(document.Json, DomainJson.Options);
    }

    public async Task SetAsync<T>(string key, T value, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var json = JsonSerializer.Serialize(value, DomainJson.Options);
        var document = await db.SettingsDocuments.FirstOrDefaultAsync(d => d.Key == key, cancellationToken);
        if (document is null)
        {
            db.SettingsDocuments.Add(new SettingsDocument { Key = key, Json = json, UpdatedAtUtc = time.GetUtcNow().UtcDateTime });
        }
        else
        {
            document.Json = json;
            document.UpdatedAtUtc = time.GetUtcNow().UtcDateTime;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
