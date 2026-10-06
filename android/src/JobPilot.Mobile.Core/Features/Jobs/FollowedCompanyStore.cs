using JobPilot.Domain.Jobs;
using JobPilot.Mobile.Core.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JobPilot.Mobile.Core.Features.Jobs;

public sealed record CompanySuggestion(AtsKind Ats, string BoardToken, string CompanyName, int Postings);

/// <summary>Companies whose public ATS boards are read on every search.</summary>
public sealed class FollowedCompanyStore(
    IDbContextFactory<MobileDbContext> dbFactory,
    SettingsDocumentStore documents,
    JobSeedFiles seeds,
    TimeProvider time)
{
    private const string SeededKey = "followed-companies-seeded";

    public async Task<IReadOnlyList<FollowedCompany>> GetAllAsync(CancellationToken cancellationToken)
    {
        await SeedOnceAsync(cancellationToken);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var companies = await db.FollowedCompanies.AsNoTracking().ToListAsync(cancellationToken);
        return companies.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Adds a company. Returns false when it is already followed.</summary>
    public async Task<bool> AddAsync(string name, AtsKind ats, string boardToken, string? notes, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(boardToken);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var token = boardToken.Trim();
        if (await db.FollowedCompanies.AnyAsync(c => c.Ats == ats && c.BoardToken == token, cancellationToken))
        {
            return false;
        }

        db.FollowedCompanies.Add(new FollowedCompany
        {
            Name = name.Trim(),
            Ats = ats,
            BoardToken = token,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            AddedAtUtc = time.GetUtcNow().UtcDateTime,
        });
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task RemoveAsync(int id, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await db.FollowedCompanies.Where(c => c.Id == id).ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>
    /// Public ATS boards referenced by stored offers (apply links or links in the description) that are not followed
    /// yet: a way to discover companies that hire for the owner's keywords.
    /// </summary>
    public async Task<IReadOnlyList<CompanySuggestion>> GetSuggestionsAsync(CancellationToken cancellationToken)
    {
        var followed = (await GetAllAsync(cancellationToken)).Select(c => (c.Ats, c.BoardToken.ToLowerInvariant())).ToHashSet();
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var postings = await db.JobPostings.AsNoTracking()
            .Where(p => !p.IsDiscarded)
            .Select(p => new { p.Company, p.Url, p.ApplyUrl, p.Description })
            .ToListAsync(cancellationToken);

        return postings
            .SelectMany(p => AtsLinks(p.Url, p.ApplyUrl, p.Description).Select(reference => (reference, p.Company)))
            .Where(x => !followed.Contains((x.reference.Ats, x.reference.BoardToken.ToLowerInvariant())))
            .GroupBy(x => (x.reference.Ats, Token: x.reference.BoardToken))
            .Select(g => new CompanySuggestion(g.Key.Ats, g.Key.Token, g.First().Company, g.Count()))
            .OrderByDescending(s => s.Postings)
            .ToList();
    }

    private static IEnumerable<AtsReference> AtsLinks(string url, string? applyUrl, string description)
    {
        var candidates = new[] { url, applyUrl }
            .Concat(System.Text.RegularExpressions.Regex.Matches(description, @"https?://[^\s)\]""'<>]+").Select(m => m.Value));
        return candidates.Select(AtsUrlParser.Parse).OfType<AtsReference>().DistinctBy(r => (r.Ats, r.BoardToken));
    }

    private async Task SeedOnceAsync(CancellationToken cancellationToken)
    {
        if (await documents.GetAsync<SeedFlag>(SeededKey, cancellationToken) is not null)
        {
            return;
        }

        foreach (var company in seeds.LoadFollowedCompanies())
        {
            await AddAsync(company.Name, company.Ats, company.BoardToken, company.Notes, cancellationToken);
        }

        await documents.SetAsync(SeededKey, new SeedFlag(time.GetUtcNow().UtcDateTime), cancellationToken);
    }

    private sealed record SeedFlag(DateTime SeededAtUtc);
}
