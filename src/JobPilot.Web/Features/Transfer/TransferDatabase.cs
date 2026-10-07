using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace JobPilot.Web.Features.Transfer;

/// <summary>
/// Reads and writes the transfer tables as raw SQLite values (no entity mapping), so a row moves between the PC and the phone
/// exactly as EF Core stored it. Table and column names come from <see cref="TransferSchema"/>; values are always parameters.
/// </summary>
public static class TransferDatabase
{
    public static async Task<TransferData> ReadAsync(DbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        var data = new TransferData();
        var connection = db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            foreach (var table in TransferSchema.Tables)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = $"SELECT {Columns(table.Columns)} FROM {Quote(table.Name)} ORDER BY {Quote(table.Key)}";
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                var rows = new List<TransferRow>();
                while (await reader.ReadAsync(cancellationToken))
                {
                    var row = new TransferRow();
                    for (var i = 0; i < table.Columns.Count; i++)
                    {
                        row[table.Columns[i]] = TransferValues.Normalize(await reader.IsDBNullAsync(i, cancellationToken) ? null : reader.GetValue(i));
                    }

                    rows.Add(row);
                }

                data.Tables[table.Name] = rows;
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }

        return data;
    }

    /// <summary>Applies a plan in one transaction: deletions children first, then updates and inserts parents first.</summary>
    public static async Task ApplyAsync(DbContext db, TransferPlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(plan);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var connection = db.Database.GetDbConnection();
        var dbTransaction = transaction.GetDbTransaction();
        foreach (var changes in plan.Tables.Reverse())
        {
            foreach (var key in changes.Deletes)
            {
                await ExecuteAsync(connection, dbTransaction, $"DELETE FROM {Quote(changes.Table.Name)} WHERE {Quote(changes.Table.Key)} = @p0", [key], cancellationToken);
            }
        }

        foreach (var changes in plan.Tables)
        {
            var table = changes.Table;
            var others = table.Columns.Where(c => c != table.Key).ToList();
            foreach (var row in changes.Updates)
            {
                var assignments = string.Join(", ", others.Select((c, i) => $"{Quote(c)} = @p{i + 1}"));
                await ExecuteAsync(connection, dbTransaction, $"UPDATE {Quote(table.Name)} SET {assignments} WHERE {Quote(table.Key)} = @p0",
                    [row[table.Key], .. others.Select(c => row[c])], cancellationToken);
            }

            foreach (var row in changes.Inserts)
            {
                var parameters = string.Join(", ", table.Columns.Select((_, i) => $"@p{i}"));
                await ExecuteAsync(connection, dbTransaction, $"INSERT INTO {Quote(table.Name)} ({Columns(table.Columns)}) VALUES ({parameters})",
                    table.Columns.Select(c => row[c]).ToList(), cancellationToken);
            }
        }

        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>A consistent copy of the whole database (SQLite <c>VACUUM INTO</c>), taken before combining.</summary>
    public static async Task BackupAsync(DbContext db, string path, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
#pragma warning disable EF1002 // The path is built by the app (data folder + timestamp), quoted as an SQL string literal.
        await db.Database.ExecuteSqlRawAsync($"VACUUM INTO '{path.Replace("'", "''", StringComparison.Ordinal)}'", cancellationToken);
#pragma warning restore EF1002
    }

    private static async Task ExecuteAsync(DbConnection connection, DbTransaction transaction, string sql, IReadOnlyList<object?> values, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        for (var i = 0; i < values.Count; i++)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = $"@p{i}";
            parameter.Value = values[i] ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string Columns(IEnumerable<string> columns) => string.Join(", ", columns.Select(Quote));

    private static string Quote(string identifier) => $"\"{identifier}\"";
}
