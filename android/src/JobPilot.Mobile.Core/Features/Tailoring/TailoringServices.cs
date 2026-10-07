using System.Text.Json;
using JobPilot.Domain.Cv;
using JobPilot.Domain.Tailoring;
using JobPilot.Mobile.Core.Features.Profile;
using JobPilot.Mobile.Core.Features.Transfer;
using JobPilot.Mobile.Core.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JobPilot.Mobile.Core.Features.Tailoring;

/// <summary>Technology names detected in generated texts (<c>Seed/technology-terms.txt</c>, data not code).</summary>
public sealed class TechnologyCatalog
{
    private readonly Lazy<IReadOnlyList<string>> _terms;

    /// <summary>The web app's <c>Seed/technology-terms.txt</c>, embedded at build time.</summary>
    public TechnologyCatalog()
        : this(ReadEmbedded)
    {
    }

    internal TechnologyCatalog(Func<IEnumerable<string>> lines) =>
        _terms = new(() => lines().Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')).ToList());

    public IReadOnlyList<string> Terms => _terms.Value;

    private static IEnumerable<string> ReadEmbedded()
    {
        using var stream = typeof(TechnologyCatalog).Assembly.GetManifestResourceStream("Seed/technology-terms.txt");
        if (stream is null)
        {
            return [];
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().ReplaceLineEndings("\n").Split('\n');
    }
}

/// <summary>Builds <see cref="TailoringContext"/> instances from the database.</summary>
public sealed class TailoringContextFactory(
    IDbContextFactory<MobileDbContext> dbFactory,
    MasterCvStore cvs,
    ProfileStore profiles,
    TechnologyCatalog catalog)
{
    /// <param name="cvVersion">Master CV version; null for the current one.</param>
    public async Task<(TailoringContext? Context, string? Error)> CreateAsync(Guid postingId, int? cvVersion, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var posting = await db.JobPostings.AsNoTracking().FirstOrDefaultAsync(p => p.Id == postingId, cancellationToken);
        if (posting is null)
        {
            return (null, "La oferta no existe.");
        }

        MasterCv? cv;
        int version;
        if (cvVersion is { } requested)
        {
            cv = await cvs.GetVersionAsync(requested, cancellationToken);
            version = requested;
        }
        else
        {
            var current = await cvs.GetAsync(cancellationToken);
            cv = current?.Cv;
            version = current?.Version ?? 0;
        }

        if (cv is null)
        {
            return (null, "Primero importá tu CV maestro (Perfil y CV maestro): todo lo que se genera sale de él.");
        }

        var profile = await profiles.GetAsync(cancellationToken);
        return (new TailoringContext(posting, cv, version, profile, catalog.Terms), null);
    }

    /// <summary>The current master CV without an offer (outreach, LinkedIn review).</summary>
    public async Task<(CvContext? Context, string? Error)> CreateForCvAsync(CancellationToken cancellationToken)
    {
        var current = await cvs.GetAsync(cancellationToken);
        if (current is null)
        {
            return (null, "Primero importá tu CV maestro (Perfil y CV maestro): todo lo que se genera sale de él.");
        }

        var profile = await profiles.GetAsync(cancellationToken);
        return (new CvContext(current.Cv, current.Version, profile, catalog.Terms), null);
    }
}

public sealed record TailoredDocumentSummary(long Id, TailoredKind Kind, TailoredStatus Status, int MasterCvVersion, DateTime CreatedAtUtc, DateTime? ApprovedAtUtc);

public sealed record StoredTailoredDocument(TailoredDocumentRecord Record, TailoredCv? Cv, CoverLetter? Letter);

/// <summary>Problems that block approval: flagged texts not confirmed by the owner and skills not in the CV.</summary>
public sealed record ApprovalCheck(IReadOnlyList<(TraceableText Text, IReadOnlyList<string> Problems)> Flags, IReadOnlyList<string> UnknownSkills)
{
    public IEnumerable<(TraceableText Text, IReadOnlyList<string> Problems)> Blocking => Flags.Where(f => !f.Text.OwnerConfirmed);

    public bool CanApprove => !Blocking.Any() && UnknownSkills.Count == 0;
}

/// <summary>Tailored CVs and letters: drafts are editable; approved documents are frozen until reopened.</summary>
public sealed class TailoredDocumentStore(IDbContextFactory<MobileDbContext> dbFactory, TimeProvider time)
{
    public async Task<long> AddDraftAsync(Guid postingId, TailoredKind kind, int cvVersion, object content, string promptId, string model, string? notes, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var record = new TailoredDocumentRecord
        {
            JobPostingId = postingId,
            Kind = kind,
            MasterCvVersion = cvVersion,
            Status = TailoredStatus.Draft,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            PromptId = promptId,
            Model = model,
            Notes = notes is { Length: > 4000 } ? notes[..4000] : notes,
            Json = JsonSerializer.Serialize(content, content.GetType(), DomainJson.Options),
        };
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.TailoredDocuments.Add(record);
        await db.SaveChangesAsync(cancellationToken);
        return record.Id;
    }

    public async Task<IReadOnlyList<TailoredDocumentSummary>> ListAsync(Guid postingId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.TailoredDocuments.AsNoTracking()
            .Where(d => d.JobPostingId == postingId)
            .Select(d => new TailoredDocumentSummary(d.Id, d.Kind, d.Status, d.MasterCvVersion, d.CreatedAtUtc, d.ApprovedAtUtc))
            .ToListAsync(cancellationToken);
        return rows.OrderByDescending(d => d.CreatedAtUtc).ToList();
    }

    public async Task<StoredTailoredDocument?> GetAsync(long id, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var record = await db.TailoredDocuments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (record is null)
        {
            return null;
        }

        return record.Kind == TailoredKind.Cv
            ? new StoredTailoredDocument(record, JsonSerializer.Deserialize<TailoredCv>(record.Json, DomainJson.Options), null)
            : new StoredTailoredDocument(record, null, JsonSerializer.Deserialize<CoverLetter>(record.Json, DomainJson.Options));
    }

    /// <summary>Saves edits of a draft. Approved documents must be reopened first.</summary>
    public async Task SaveAsync(long id, object content, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var record = await db.TailoredDocuments.FirstAsync(d => d.Id == id, cancellationToken);
        if (record.Status == TailoredStatus.Approved)
        {
            throw new InvalidOperationException("El documento está aprobado: volvelo a borrador para editarlo.");
        }

        record.Json = JsonSerializer.Serialize(content, content.GetType(), DomainJson.Options);
        record.UpdatedAtUtc = time.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SetStatusAsync(long id, TailoredStatus status, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var record = await db.TailoredDocuments.FirstAsync(d => d.Id == id, cancellationToken);
        record.Status = status;
        record.ApprovedAtUtc = status == TailoredStatus.Approved ? time.GetUtcNow().UtcDateTime : null;
        record.UpdatedAtUtc = time.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await DeletionLog.DeleteAsync(db, TransferSchema.TailoredDocuments, id, ct => db.TailoredDocuments.Where(d => d.Id == id).ExecuteDeleteAsync(ct), time.GetUtcNow().UtcDateTime, cancellationToken);
    }

    public static ApprovalCheck Check(TailoringContext context, TailoredCv cv)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new ApprovalCheck(context.Validator.Check(cv), context.Validator.UnknownSkills(cv.Skills));
    }

    public static ApprovalCheck Check(TailoringContext context, CoverLetter letter)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(letter);
        var postingText = context.Posting.SearchableText;
        return new ApprovalCheck(
            letter.Paragraphs.Select(p => (p, context.Validator.Check(p, allowedExtraText: postingText))).Where(r => r.Item2.Count > 0).ToList(),
            []);
    }
}
