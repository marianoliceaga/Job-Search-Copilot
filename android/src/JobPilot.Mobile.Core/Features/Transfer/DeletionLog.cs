using JobPilot.Mobile.Core.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JobPilot.Mobile.Core.Features.Transfer;

/// <summary>
/// Records the owner's deletions (<see cref="DeletedRecord"/>) so that combining an export on the other device deletes the same
/// rows there. The identity is read with SQLite's <c>json_object</c> so its values keep the raw form the transfer file uses.
/// </summary>
public static class DeletionLog
{
    /// <summary>Runs <paramref name="delete"/> and, when it removed rows, records the deletion of the row with that key.</summary>
    public static async Task<int> DeleteAsync(DbContext db, string table, object key, Func<CancellationToken, Task<int>> delete, DateTime nowUtc, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(delete);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var identity = await IdentityAsync(db, table, key, cancellationToken);
        var deleted = await delete(cancellationToken);
        if (deleted > 0 && identity is not null)
        {
            db.Set<DeletedRecord>().Add(new DeletedRecord { TableName = table, Identity = identity, DeletedAtUtc = nowUtc });
            await db.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return deleted;
    }

    /// <summary>For deletions saved with <see cref="DbContext.SaveChangesAsync(CancellationToken)"/>: adds the record to the same save.</summary>
    public static async Task RecordAsync(DbContext db, string table, object key, DateTime nowUtc, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (await IdentityAsync(db, table, key, cancellationToken) is { } identity)
        {
            db.Set<DeletedRecord>().Add(new DeletedRecord { TableName = table, Identity = identity, DeletedAtUtc = nowUtc });
        }
    }

    private static async Task<string?> IdentityAsync(DbContext db, string table, object key, CancellationToken cancellationToken)
    {
        var definition = TransferSchema.Get(table);
        if (!definition.TracksDeletions)
        {
            throw new InvalidOperationException($"Deletions of {table} are not tracked.");
        }

        var pairs = string.Join(", ", definition.Identity.Select(c => $"'{c}', \"{c}\""));
#pragma warning disable EF1002 // Table and column names come from TransferSchema, not from input; the key is a parameter.
        return await db.Database
            .SqlQueryRaw<string>($"SELECT json_object({pairs}) AS \"Value\" FROM \"{definition.Name}\" WHERE \"{definition.Key}\" = {{0}}", key)
            .FirstOrDefaultAsync(cancellationToken);
#pragma warning restore EF1002
    }
}
