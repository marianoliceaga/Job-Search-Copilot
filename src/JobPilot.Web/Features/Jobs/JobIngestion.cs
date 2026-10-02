using JobPilot.Domain.Jobs;
using JobPilot.Web.Features.Jobs.Sources;
using JobPilot.Web.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JobPilot.Web.Features.Jobs;

public sealed record IngestionResult(int Received, int Matched, int Added, int Updated, int Merged, IReadOnlyList<Guid> PostingIds);

/// <summary>
/// Stores offers read from sources: keyword and age filters, then deduplication by canonical URL (same posting) and
/// by company + title fingerprint (same offer in another source, recorded as a sighting).
/// </summary>
public sealed class JobIngestion(IDbContextFactory<JobPilotDbContext> dbFactory, TimeProvider time)
{
    public async Task<IngestionResult> IngestAsync(IReadOnlyList<SourcedJob> jobs, JobSearchPreferences preferences, bool applyFilters, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(preferences);
        var now = time.GetUtcNow().UtcDateTime;
        var matcher = new KeywordMatcher(preferences.Keywords, preferences.ExcludedKeywords);
        var oldest = now.AddDays(-preferences.MaxAgeDays);

        var accepted = applyFilters
            ? jobs.Where(j => (j.PublishedAtUtc is null || j.PublishedAtUtc >= oldest)
                              && matcher.Matches(string.Join("\n", j.Title, string.Join(", ", j.Tags), j.Description))).ToList()
            : jobs.ToList();

        // A source queried with several keywords returns the same posting more than once: keep one per URL.
        var keyed = accepted
            .Select(j => (Job: j, UrlKey: UrlNormalizer.Canonicalize(j.Url), Fingerprint: JobFingerprint.Compute(j.Company, j.Title)))
            .DistinctBy(k => k.UrlKey)
            .ToList();
        var urlKeys = keyed.Select(k => k.UrlKey).Distinct().ToList();
        var fingerprints = keyed.Select(k => k.Fingerprint).Distinct().ToList();

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var existing = await db.JobPostings
            .Where(p => urlKeys.Contains(p.UrlKey) || fingerprints.Contains(p.Fingerprint))
            .ToListAsync(cancellationToken);
        var byUrl = existing.ToDictionary(p => p.UrlKey);
        var byFingerprint = existing.GroupBy(p => p.Fingerprint).ToDictionary(g => g.Key, g => g.OrderBy(p => p.FirstSeenAtUtc).First());

        int added = 0, updated = 0, merged = 0;
        var ids = new List<Guid>();
        foreach (var (job, urlKey, fingerprint) in keyed)
        {
            if (byUrl.TryGetValue(urlKey, out var same))
            {
                Refresh(same, job, now);
                updated++;
                ids.Add(same.Id);
            }
            else if (byFingerprint.TryGetValue(fingerprint, out var duplicate))
            {
                if (!duplicate.Sightings.Any(s => UrlNormalizer.Canonicalize(s.Url) == urlKey))
                {
                    duplicate.Sightings = [.. duplicate.Sightings, new JobSighting(job.SourceKey, job.SourceName, job.Url, now)];
                }

                FillGaps(duplicate, job);
                duplicate.LastSeenAtUtc = now;
                byUrl[urlKey] = duplicate;
                merged++;
                ids.Add(duplicate.Id);
            }
            else
            {
                var posting = Create(job, urlKey, fingerprint, now);
                db.JobPostings.Add(posting);
                byUrl[urlKey] = posting;
                byFingerprint[fingerprint] = posting;
                added++;
                ids.Add(posting.Id);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return new IngestionResult(jobs.Count, keyed.Count, added, updated, merged, ids.Distinct().ToList());
    }

    private static JobPosting Create(SourcedJob job, string urlKey, string fingerprint, DateTime now)
    {
        var posting = new JobPosting
        {
            Id = Guid.NewGuid(),
            SourceKey = job.SourceKey,
            SourceName = job.SourceName,
            Title = job.Title,
            Company = job.Company,
            Url = job.Url,
            UrlKey = urlKey,
            Fingerprint = fingerprint,
            FirstSeenAtUtc = now,
        };
        Refresh(posting, job, now);
        return posting;
    }

    /// <summary>The source is authoritative for its own posting: take its latest values.</summary>
    private static void Refresh(JobPosting posting, SourcedJob job, DateTime now)
    {
        posting.SourceJobId = job.SourceJobId;
        posting.Title = Truncate(job.Title, 500);
        posting.Company = Truncate(job.Company, 300);
        posting.ApplyUrl = job.ApplyUrl;
        posting.Location = job.Location;
        posting.EmploymentType = job.EmploymentType;
        posting.Tags = job.Tags.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        posting.Description = job.Description;
        posting.PublishedAtUtc = job.PublishedAtUtc;
        posting.SalaryMin = job.SalaryMin;
        posting.SalaryMax = job.SalaryMax;
        posting.SalaryCurrency = job.SalaryCurrency;
        posting.SalaryPeriod = job.SalaryPeriod;
        posting.SalaryText = job.SalaryText;
        posting.LastSeenAtUtc = now;
    }

    /// <summary>Another source of the same offer may publish data the first one lacked.</summary>
    private static void FillGaps(JobPosting posting, SourcedJob job)
    {
        if (!posting.HasPublishedSalary && (job.SalaryMin is not null || job.SalaryMax is not null || job.SalaryText is not null))
        {
            posting.SalaryMin = job.SalaryMin;
            posting.SalaryMax = job.SalaryMax;
            posting.SalaryCurrency = job.SalaryCurrency;
            posting.SalaryPeriod = job.SalaryPeriod;
            posting.SalaryText = job.SalaryText;
        }

        posting.Location ??= job.Location;
        posting.ApplyUrl ??= job.ApplyUrl;
        if (posting.Description.Length < job.Description.Length / 2)
        {
            posting.Description = job.Description;
        }
    }

    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length];
}
