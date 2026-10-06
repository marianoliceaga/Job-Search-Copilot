using System.Text.Json;
using JobPilot.Domain.Cv;
using JobPilot.Domain.Jobs;
using JobPilot.Mobile.Core.Features.Jobs.Analysis;
using JobPilot.Mobile.Core.Features.Profile;
using JobPilot.Mobile.Core.Infrastructure.Llm;
using JobPilot.Mobile.Core.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JobPilot.Mobile.Core.Features.Jobs;

public sealed record JobListFilter
{
    public string? Text { get; init; }

    public bool HideUnlikelyEligible { get; init; }

    public bool OnlyRemote { get; init; }

    public int? MaxAgeDays { get; init; }

    public string? SourceKey { get; init; }

    /// <summary>Null: all; true: analyzed only; false: not analyzed only.</summary>
    public bool? Analyzed { get; init; }

    public bool OnlyDiscarded { get; init; }
}

public sealed record JobListItem(
    JobPosting Posting,
    EligibilityAssessment Eligibility,
    bool EligibilityFromAnalysis,
    CompensationNormalization Compensation,
    RankResult Rank,
    VerifiedJobAnalysis? Analysis,
    DateTime? AnalyzedAtUtc);

/// <summary>Offers with their eligibility, normalized compensation and rank, filtered and sorted for the list.</summary>
public sealed class JobListService(IDbContextFactory<MobileDbContext> dbFactory, JobPreferencesStore preferencesStore, ProfileStore profiles)
{
    private static readonly HashSet<string> RemoteBoards = new(StringComparer.OrdinalIgnoreCase)
    {
        Sources.RemotiveSource.SourceKey, Sources.RemoteOkSource.SourceKey, Sources.HimalayasSource.SourceKey,
        Sources.JobicySource.SourceKey, Sources.WeWorkRemotelySource.SourceKey,
    };

    public async Task<IReadOnlyList<JobListItem>> GetAsync(JobListFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var query = db.JobPostings.AsNoTracking().Where(p => p.IsDiscarded == filter.OnlyDiscarded);
        if (filter.SourceKey is not null)
        {
            query = query.Where(p => p.SourceKey == filter.SourceKey);
        }

        var postings = await query.ToListAsync(cancellationToken);
        var analyses = await LatestAnalysesAsync(db, postings.Select(p => p.Id).ToList(), cancellationToken);
        var context = await ContextAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var text = TextNormalizer.Normalize(filter.Text);

        return postings
            .Select(p => Build(p, analyses.GetValueOrDefault(p.Id), context))
            .Where(i => text.Length == 0 || TextNormalizer.Normalize(i.Posting.SearchableText).Contains(text, StringComparison.Ordinal))
            .Where(i => !filter.HideUnlikelyEligible || i.Eligibility.Status != EligibilityStatus.Unlikely)
            .Where(i => !filter.OnlyRemote || IsRemote(i.Posting))
            .Where(i => filter.MaxAgeDays is null || (i.Posting.PublishedAtUtc ?? i.Posting.FirstSeenAtUtc) >= now.AddDays(-filter.MaxAgeDays.Value))
            .Where(i => filter.Analyzed is null || (i.Analysis is not null) == filter.Analyzed)
            .OrderByDescending(i => i.Rank.Score)
            .ThenByDescending(i => i.Posting.PublishedAtUtc ?? i.Posting.FirstSeenAtUtc)
            .ToList();
    }

    /// <summary>
    /// Stores the description the owner pasted for an offer that arrived without one (e.g. a LinkedIn alert). The text is
    /// kept as pasted, never rewritten; an existing description is not replaced.
    /// </summary>
    public async Task<string?> SetPastedDescriptionAsync(Guid id, string text, CancellationToken cancellationToken)
    {
        var description = (text ?? string.Empty).Trim();
        if (description.Length == 0)
        {
            return "Pegá el texto de la oferta.";
        }

        if (description.Length > ManualJobImport.MaxTextLength)
        {
            return $"El texto es demasiado largo (máximo {ManualJobImport.MaxTextLength:N0} caracteres).";
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var posting = await db.JobPostings.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (posting is null)
        {
            return "La oferta no existe.";
        }

        if (!string.IsNullOrWhiteSpace(posting.Description))
        {
            return "La oferta ya tiene descripción.";
        }

        posting.Description = description;
        await db.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<JobListItem?> GetItemAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var posting = await db.JobPostings.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (posting is null)
        {
            return null;
        }

        var analyses = await LatestAnalysesAsync(db, [id], cancellationToken);
        return Build(posting, analyses.GetValueOrDefault(id), await ContextAsync(cancellationToken));
    }

    public async Task SetDiscardedAsync(Guid id, bool discarded, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await db.JobPostings.Where(p => p.Id == id).ExecuteUpdateAsync(s => s.SetProperty(p => p.IsDiscarded, discarded), cancellationToken);
    }

    public static bool IsRemote(JobPosting posting)
    {
        ArgumentNullException.ThrowIfNull(posting);
        if (RemoteBoards.Contains(posting.SourceKey))
        {
            return true;
        }

        var text = TextNormalizer.Normalize($"{posting.Location} {posting.Title}");
        return text.Contains("remote", StringComparison.Ordinal) || text.Contains("remoto", StringComparison.Ordinal)
            || text.Contains("anywhere", StringComparison.Ordinal) || text.Contains("worldwide", StringComparison.Ordinal);
    }

    private static JobListItem Build(JobPosting posting, (VerifiedJobAnalysis Analysis, DateTime At)? analysis, ListContext context)
    {
        var verified = analysis?.Analysis;
        var eligibility = verified is null
            ? EligibilityHeuristic.Assess(posting.Location, context.Country)
            : new EligibilityAssessment(verified.Analysis.Eligibility.Verdict switch
            {
                EligibilityVerdict.Eligible => EligibilityStatus.Likely,
                EligibilityVerdict.NotEligible => EligibilityStatus.Unlikely,
                _ => EligibilityStatus.Unknown,
            }, verified.Analysis.Eligibility.Reasoning);

        var compensation = posting.SalaryMin is not null || posting.SalaryMax is not null
            ? CompensationNormalizer.ToAnnualUsd(posting.SalaryMin, posting.SalaryMax, posting.SalaryCurrency, posting.SalaryPeriod, context.Preferences.UsdPerUnit)
            : verified is { CompensationOrigin: CompensationOrigin.PostingQuote } v
                ? CompensationNormalizer.ToAnnualUsd(v.Analysis.Compensation.Min, v.Analysis.Compensation.Max, v.Analysis.Compensation.Currency,
                    Sources.SourceParsing.Period(v.Analysis.Compensation.Period), context.Preferences.UsdPerUnit)
                : new CompensationNormalization(null, posting.SalaryText is null ? "Compensación no informada." : "La fuente publica la compensación solo como texto; analizala para normalizarla.");

        var rank = JobRanker.Rank(
            new RankInputs(verified?.Analysis.FitScore, compensation.Range?.Midpoint, eligibility.Status, verified?.Analysis.RedFlags.Count),
            context.Preferences.Weights,
            context.Preferences.Compensation);

        return new JobListItem(posting, eligibility, verified is not null, compensation, rank, verified, analysis?.At);
    }

    private static async Task<Dictionary<Guid, (VerifiedJobAnalysis Analysis, DateTime At)>> LatestAnalysesAsync(
        MobileDbContext db, List<Guid> ids, CancellationToken cancellationToken)
    {
        var records = await db.JobAnalyses.AsNoTracking()
            .Where(a => ids.Contains(a.JobPostingId))
            .Select(a => new { a.JobPostingId, a.CreatedAtUtc, a.Id, a.Json })
            .ToListAsync(cancellationToken);

        return records
            .GroupBy(a => a.JobPostingId)
            .Select(g => g.OrderByDescending(a => a.CreatedAtUtc).ThenByDescending(a => a.Id).First())
            .ToDictionary(a => a.JobPostingId, a => (JsonSerializer.Deserialize<VerifiedJobAnalysis>(a.Json, LlmJson.Options)!, a.CreatedAtUtc));
    }

    private async Task<ListContext> ContextAsync(CancellationToken cancellationToken) =>
        new(await preferencesStore.GetAsync(cancellationToken), (await profiles.GetAsync(cancellationToken)).WorkFromCountry ?? string.Empty);

    private sealed record ListContext(JobSearchPreferences Preferences, string Country);
}
