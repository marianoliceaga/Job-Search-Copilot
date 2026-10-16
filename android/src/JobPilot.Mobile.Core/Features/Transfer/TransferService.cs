using System.Globalization;
using JobPilot.Mobile.Core.Features.Pipeline;
using JobPilot.Mobile.Core.Infrastructure;
using JobPilot.Mobile.Core.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JobPilot.Mobile.Core.Features.Transfer;

/// <summary>One line of the import summary (a table with changes).</summary>
public sealed record TransferSummaryLine(string Label, int Added, int Updated, int Deleted, int Skipped);

/// <summary>What a file would change, shown before the owner confirms.</summary>
public sealed record TransferPreview(TransferFile File, IReadOnlyList<TransferSummaryLine> Lines)
{
    public bool HasChanges => Lines.Any(l => l.Added + l.Updated + l.Deleted > 0);
}

public sealed record TransferImportResult(IReadOnlyList<TransferSummaryLine> Lines, string BackupPath);

/// <summary>
/// Export and "Combinar" import of every JobSearch Copilot table, the same file format as the PC (<see cref="TransferFile"/>). Nothing is
/// sent anywhere: the owner shares the file (Android's share sheet) and picks it on the other device. Before combining, the
/// database is copied to the app's data folder. Port of the web service; after combining, the phone's reminders are rescheduled.
/// </summary>
public sealed class TransferService(IDbContextFactory<MobileDbContext> dbFactory, MobilePaths paths, TimeProvider time, ReminderNotificationSync notifications)
{
    public const string App = "android";
    public const string ContentType = "application/json";
    public const long MaxFileBytes = 100L * 1024 * 1024;
    private const int BackupsKept = 5;

    private static readonly SemaphoreSlim Gate = new(1, 1);

    public string BackupDirectory => Path.Combine(paths.DataDirectory, "backups");

    public async Task<(string FileName, byte[] Content)> ExportAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var now = time.GetUtcNow().UtcDateTime;
        var file = new TransferFile { App = App, ExportedAtUtc = now, Data = await TransferDatabase.ReadAsync(db, cancellationToken) };
        using var stream = new MemoryStream();
        file.Write(stream);
        var local = TimeZoneInfo.ConvertTimeFromUtc(now, time.LocalTimeZone);
        return ($"jobsearch-copilot-celular-{local.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture)}.json", stream.ToArray());
    }

    /// <summary>Reads the file and computes what combining it would change. Errors are Spanish, for the UI.</summary>
    public async Task<(TransferPreview? Preview, string? Error)> PreviewAsync(Stream content, CancellationToken cancellationToken)
    {
        var (file, error) = TransferFile.Read(content);
        if (file is null)
        {
            return (null, error);
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var plan = TransferMerger.Merge(await TransferDatabase.ReadAsync(db, cancellationToken), file.Data);
        return (new TransferPreview(file, Summarize(plan)), null);
    }

    /// <summary>Copies the database, then combines the file (the plan is computed again on the current data).</summary>
    public async Task<TransferImportResult> ImportAsync(TransferFile file, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        await Gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var plan = TransferMerger.Merge(await TransferDatabase.ReadAsync(db, cancellationToken), file.Data);
            var local = TimeZoneInfo.ConvertTimeFromUtc(time.GetUtcNow().UtcDateTime, time.LocalTimeZone);
            var backup = Path.Combine(BackupDirectory, $"jobsearch-copilot-antes-de-combinar-{local.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.db");
            await TransferDatabase.BackupAsync(db, backup, cancellationToken);
            PruneBackups();
            await TransferDatabase.ApplyAsync(db, plan, cancellationToken);
            await notifications.SyncAsync(cancellationToken);
            return new TransferImportResult(Summarize(plan), backup);
        }
        finally
        {
            Gate.Release();
        }
    }

    public static IReadOnlyList<TransferSummaryLine> Summarize(TransferPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan.Tables
            .Where(t => t.HasChanges || t.Skipped > 0)
            .Select(t => new TransferSummaryLine(t.Table.Label, t.Inserts.Count, t.Updates.Count, t.Deletes.Count, t.Skipped))
            .ToList();
    }

    private void PruneBackups()
    {
        var old = new DirectoryInfo(BackupDirectory).GetFiles("*-antes-de-combinar-*.db")
            .OrderByDescending(f => f.Name, StringComparer.Ordinal)
            .Skip(BackupsKept);
        foreach (var file in old)
        {
            file.Delete();
        }
    }
}

public static class TransferServiceCollectionExtensions
{
    /// <summary>Phase A10: export and "Combinar" import between the PC and the phone (port of the web feature).</summary>
    public static IServiceCollection AddTransfer(this IServiceCollection services)
    {
        services.AddTransient<TransferService>();
        return services;
    }
}
