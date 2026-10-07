using System.Text.Json;
using JobPilot.Domain.Cv;
using JobPilot.Mobile.Core.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JobPilot.Mobile.Core.Features.Profile;

public sealed record StoredMasterCv(MasterCv Cv, int Version, DateTime UpdatedAtUtc);

public sealed record MasterCvVersionInfo(int Version, DateTime CreatedAtUtc, string Source);

public sealed record MasterCvSaveResult(int? Version, IReadOnlyList<string> Problems)
{
    public bool IsSuccess => Problems.Count == 0;
}

/// <summary>Stores the master CV and an immutable snapshot of every saved version.</summary>
public sealed class MasterCvStore(IDbContextFactory<MobileDbContext> dbFactory, TimeProvider time)
{
    public async Task<StoredMasterCv?> GetAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var document = await db.MasterCvs.AsNoTracking().OrderBy(d => d.Id).FirstOrDefaultAsync(cancellationToken);
        return document is null ? null : new StoredMasterCv(Deserialize(document.Json), document.Version, document.UpdatedAtUtc);
    }

    /// <summary>Validates and saves the CV as a new version. Assigns ids to new elements.</summary>
    public async Task<MasterCvSaveResult> SaveAsync(MasterCv cv, string source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cv);
        cv.EnsureIds();
        var problems = cv.Validate();
        if (problems.Count > 0)
        {
            return new MasterCvSaveResult(null, problems);
        }

        var json = JsonSerializer.Serialize(cv, DomainJson.Options);
        var now = time.GetUtcNow().UtcDateTime;

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var document = await db.MasterCvs.OrderBy(d => d.Id).FirstOrDefaultAsync(cancellationToken);
        var version = (await db.MasterCvVersions.MaxAsync(v => (int?)v.Version, cancellationToken) ?? 0) + 1;
        if (document is null)
        {
            db.MasterCvs.Add(new MasterCvDocument { Json = json, Version = version, UpdatedAtUtc = now });
        }
        else
        {
            document.Json = json;
            document.Version = version;
            document.UpdatedAtUtc = now;
        }

        db.MasterCvVersions.Add(new MasterCvVersion { Version = version, CreatedAtUtc = now, Source = Truncate(source, 300), Json = json });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new MasterCvSaveResult(version, []);
    }

    public async Task<IReadOnlyList<MasterCvVersionInfo>> GetVersionsAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.MasterCvVersions.AsNoTracking()
            .OrderByDescending(v => v.Version)
            .Select(v => new MasterCvVersionInfo(v.Version, v.CreatedAtUtc, v.Source))
            .ToListAsync(cancellationToken);
    }

    public async Task<MasterCv?> GetVersionAsync(int version, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var snapshot = await db.MasterCvVersions.AsNoTracking().FirstOrDefaultAsync(v => v.Version == version, cancellationToken);
        return snapshot is null ? null : Deserialize(snapshot.Json);
    }

    /// <summary>Makes an old version current again by saving it as a new version (history is never rewritten).</summary>
    public async Task<MasterCvSaveResult> RestoreAsync(int version, CancellationToken cancellationToken)
    {
        var cv = await GetVersionAsync(version, cancellationToken)
            ?? throw new KeyNotFoundException($"Master CV version {version} does not exist.");
        return await SaveAsync(cv, $"restore: v{version}", cancellationToken);
    }

    private static MasterCv Deserialize(string json) =>
        JsonSerializer.Deserialize<MasterCv>(json, DomainJson.Options) ?? throw new InvalidDataException("Stored master CV is empty.");

    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length];
}
