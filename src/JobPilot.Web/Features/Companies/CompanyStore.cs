using System.Net;
using JobPilot.Domain.Companies;
using JobPilot.Domain.Jobs;
using JobPilot.Web.Features.Transfer;
using JobPilot.Web.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace JobPilot.Web.Features.Companies;

public sealed class CompanyOptions
{
    public const string SectionName = "Companies";

    /// <summary>URLs read per research (Gemini's URL context tool accepts up to 20 per request).</summary>
    public int MaxUrlsPerResearch { get; set; } = 10;

    public int MaxPastedTextLength { get; set; } = 20000;
}

public sealed record CompanySummary(Guid Id, string Name, string? Website, int Facts, int VerifiedFacts, int Offers, DateTime? ResearchedAtUtc);

public sealed record CompanyOffer(Guid Id, string Title, DateTime FirstSeenAtUtc);

/// <summary>Company profiles. Sources are added by the owner; facts come from research and can be deleted.</summary>
public sealed class CompanyStore(IDbContextFactory<JobPilotDbContext> dbFactory, IOptions<CompanyOptions> options, TimeProvider time)
{
    /// <summary>Creates a profile, or returns the existing one for the same company (name compared without legal suffixes).</summary>
    public async Task<Guid> CreateAsync(string name, string? website, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("El nombre es obligatorio.", nameof(name));
        }

        if (!string.IsNullOrWhiteSpace(website) && PublicUrlProblem(website) is { } problem)
        {
            throw new ArgumentException(problem, nameof(website));
        }

        var profile = CompanyProfile.Create(name, website, Now);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var existing = await db.Companies.Where(c => c.NameKey == profile.NameKey).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(cancellationToken);
        if (existing is { } id)
        {
            return id;
        }

        if (profile.Website is { } site)
        {
            profile.Sources.Add(new CompanySource { Kind = CompanySourceKind.Url, Url = site, Label = "Sitio web", AddedAtUtc = Now });
        }

        db.Companies.Add(profile);
        await db.SaveChangesAsync(cancellationToken);
        return profile.Id;
    }

    public async Task<IReadOnlyList<CompanySummary>> ListAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var companies = await db.Companies.AsNoTracking().ToListAsync(cancellationToken);
        var keys = await db.JobPostings.AsNoTracking().Select(p => p.Fingerprint).ToListAsync(cancellationToken);
        var offers = keys.Select(k => k.Split('|')[0]).GroupBy(k => k).ToDictionary(g => g.Key, g => g.Count());
        return companies
            .Select(c => new CompanySummary(c.Id, c.Name, c.Website, c.Facts.Count, c.Facts.Count(f => f.IsVerified), offers.GetValueOrDefault(c.NameKey), c.ResearchedAtUtc))
            .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public async Task<CompanyProfile?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
    }

    /// <summary>The profile of an offer's company, if there is one.</summary>
    public async Task<CompanyProfile?> FindByCompanyAsync(string company, CancellationToken cancellationToken)
    {
        var key = JobFingerprint.CompanyKey(company);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.NameKey == key, cancellationToken);
    }

    public async Task<IReadOnlyList<CompanyOffer>> OffersAsync(CompanyProfile profile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var prefix = profile.NameKey + "|";
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.JobPostings.AsNoTracking()
            .Where(p => p.Fingerprint.StartsWith(prefix))
            .OrderByDescending(p => p.FirstSeenAtUtc)
            .Select(p => new CompanyOffer(p.Id, p.Title, p.FirstSeenAtUtc))
            .ToListAsync(cancellationToken);
    }

    /// <returns>A Spanish error message, or null when the source was added.</returns>
    public Task<string?> AddUrlAsync(Guid id, string url, string? label, CancellationToken cancellationToken)
    {
        if (PublicUrlProblem(url) is { } problem)
        {
            return Task.FromResult<string?>(problem);
        }

        return UpdateAsync(id, profile =>
        {
            if (profile.Sources.Any(s => s.Kind == CompanySourceKind.Url && CompanyFactVerifier.UrlKey(s.Url) == CompanyFactVerifier.UrlKey(url)))
            {
                return "Esa URL ya está en las fuentes.";
            }

            if (profile.Sources.Count(s => s.Kind == CompanySourceKind.Url) >= options.Value.MaxUrlsPerResearch)
            {
                return $"Se pueden usar hasta {options.Value.MaxUrlsPerResearch} URLs por empresa.";
            }

            profile.Sources.Add(new CompanySource { Kind = CompanySourceKind.Url, Url = url.Trim(), Label = Clean(label), AddedAtUtc = Now });
            return null;
        }, cancellationToken);
    }

    /// <param name="url">Where the text was copied from (optional, kept for attribution).</param>
    public Task<string?> AddTextAsync(Guid id, string text, string? url, string? label, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Task.FromResult<string?>("Pegá algún texto.");
        }

        if (text.Length > options.Value.MaxPastedTextLength)
        {
            return Task.FromResult<string?>($"El texto supera los {options.Value.MaxPastedTextLength} caracteres.");
        }

        if (!string.IsNullOrWhiteSpace(url) && PublicUrlProblem(url) is { } problem)
        {
            return Task.FromResult<string?>(problem);
        }

        return UpdateAsync(id, profile =>
        {
            profile.Sources.Add(new CompanySource
            {
                Kind = CompanySourceKind.Text, Text = text.Trim(), Url = Clean(url), Label = Clean(label), AddedAtUtc = Now,
            });
            return null;
        }, cancellationToken);
    }

    public Task RemoveSourceAsync(Guid id, Guid sourceId, CancellationToken cancellationToken) =>
        UpdateAsync(id, profile =>
        {
            profile.Sources.RemoveAll(s => s.Id == sourceId);
            return null;
        }, cancellationToken);

    public Task DeleteFactAsync(Guid id, Guid factId, CancellationToken cancellationToken) =>
        UpdateAsync(id, profile =>
        {
            profile.Facts.RemoveAll(f => f.Id == factId);
            return null;
        }, cancellationToken);

    public Task SaveNotesAsync(Guid id, string? notes, CancellationToken cancellationToken) =>
        UpdateAsync(id, profile =>
        {
            profile.Notes = Clean(notes);
            return null;
        }, cancellationToken);

    /// <summary>Replaces the facts with those of a new research.</summary>
    public Task SaveResearchAsync(Guid id, IReadOnlyList<CompanyFact> facts, IReadOnlyList<string> gaps, string promptId, string model, CancellationToken cancellationToken) =>
        UpdateAsync(id, profile =>
        {
            profile.Facts = facts.ToList();
            profile.Gaps = gaps.Where(g => !string.IsNullOrWhiteSpace(g)).Select(g => g.Trim()).ToList();
            profile.ResearchedAtUtc = Now;
            profile.PromptId = promptId;
            profile.Model = model;
            return null;
        }, cancellationToken);

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await DeletionLog.DeleteAsync(db, TransferSchema.Companies, id, ct => db.Companies.Where(c => c.Id == id).ExecuteDeleteAsync(ct), time.GetUtcNow().UtcDateTime, cancellationToken);
    }

    /// <summary>Only public http(s) pages: Gemini's URL context tool cannot read local or private addresses.</summary>
    public static string? PublicUrlProblem(string? url)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return "Ingresá una URL completa que empiece con https://.";
        }

        if (uri.IsLoopback || !uri.Host.Contains('.', StringComparison.Ordinal) || (IPAddress.TryParse(uri.Host, out var ip) && IsPrivate(ip)))
        {
            return "La URL tiene que ser una página pública de internet.";
        }

        return null;
    }

    private static bool IsPrivate(IPAddress ip)
    {
        var bytes = ip.MapToIPv4().GetAddressBytes();
        return IPAddress.IsLoopback(ip) || bytes[0] == 10 || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) || (bytes[0] == 192 && bytes[1] == 168)
            || (bytes[0] == 169 && bytes[1] == 254) || ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal;
    }

    private async Task<string?> UpdateAsync(Guid id, Func<CompanyProfile, string?> change, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var profile = await db.Companies.FirstAsync(c => c.Id == id, cancellationToken);
        var error = change(profile);
        if (error is null)
        {
            profile.UpdatedAtUtc = Now;
            await db.SaveChangesAsync(cancellationToken);
        }

        return error;
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private DateTime Now => time.GetUtcNow().UtcDateTime;
}
