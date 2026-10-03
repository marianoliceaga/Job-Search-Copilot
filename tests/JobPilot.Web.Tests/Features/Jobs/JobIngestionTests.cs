using JobPilot.Domain.Jobs;
using JobPilot.Web.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using static JobPilot.Web.Tests.TestSupport.JobsTestContext;

namespace JobPilot.Web.Tests.Features.Jobs;

public class JobIngestionTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Keeps_only_offers_matching_keywords_and_age()
    {
        await using var context = await CreateAsync();
        var preferences = new JobSearchPreferences { Keywords = [".net", "c#"], ExcludedKeywords = ["vb6"], MaxAgeDays = 30 };

        var result = await context.Ingestion.IngestAsync(
        [
            Job(),
            Job("React Developer", url: "https://remotive.com/jobs/2", description: "TypeScript"),
            Job("Legacy .NET", url: "https://remotive.com/jobs/3", description: "VB6 maintenance"),
            Job("Old .NET role", url: "https://remotive.com/jobs/4", published: new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc)),
        ], preferences, applyFilters: true, Ct);

        Assert.Equal((4, 1, 1), (result.Received, result.Matched, result.Added));
    }

    [Fact]
    public async Task The_same_url_updates_the_posting_instead_of_duplicating_it()
    {
        await using var context = await CreateAsync();
        var preferences = new JobSearchPreferences();

        await context.Ingestion.IngestAsync([Job()], preferences, applyFilters: false, Ct);
        context.Time.Advance(TimeSpan.FromHours(5));
        var second = await context.Ingestion.IngestAsync([Job(url: "https://www.remotive.com/jobs/1/?utm_source=x") with { SalaryText = "$100k" }], preferences, applyFilters: false, Ct);

        Assert.Equal((0, 1), (second.Added, second.Updated));
        await using var db = await context.Db.CreateDbContextAsync(Ct);
        var posting = await db.JobPostings.SingleAsync(Ct);
        Assert.Equal("$100k", posting.SalaryText);
        Assert.True(posting.LastSeenAtUtc > posting.FirstSeenAtUtc);
    }

    [Fact]
    public async Task The_same_offer_in_another_source_is_merged_as_a_sighting_and_fills_gaps()
    {
        await using var context = await CreateAsync();
        var preferences = new JobSearchPreferences();

        await context.Ingestion.IngestAsync([Job("Senior .NET Engineer (Remote)", "Northwind Inc.")], preferences, applyFilters: false, Ct);
        var result = await context.Ingestion.IngestAsync(
            [Job("Senior .NET Engineer", "Northwind", "https://himalayas.app/jobs/9", "himalayas") with { SalaryMin = 100_000, SalaryCurrency = "USD", SalaryPeriod = SalaryPeriod.Year }],
            preferences, applyFilters: false, Ct);

        Assert.Equal(1, result.Merged);
        await using var db = await context.Db.CreateDbContextAsync(Ct);
        var posting = await db.JobPostings.SingleAsync(Ct);
        Assert.Equal("remotive", posting.SourceKey);
        var sighting = Assert.Single(posting.Sightings);
        Assert.Equal(("himalayas", "https://himalayas.app/jobs/9"), (sighting.SourceKey, sighting.Url));
        Assert.Equal(100_000m, posting.SalaryMin);
    }

    [Fact]
    public async Task The_same_url_repeated_in_one_batch_counts_once()
    {
        await using var context = await CreateAsync();

        var result = await context.Ingestion.IngestAsync([Job(), Job(), Job(url: "https://remotive.com/jobs/1?utm_source=x")], new JobSearchPreferences(), applyFilters: false, Ct);

        Assert.Equal((3, 1, 1, 0, 0), (result.Received, result.Matched, result.Added, result.Updated, result.Merged));
    }

    [Fact]
    public async Task Duplicates_inside_one_batch_are_merged_too()
    {
        await using var context = await CreateAsync();

        var result = await context.Ingestion.IngestAsync(
            [Job(), Job(url: "https://remoteok.com/jobs/7", source: "remoteok")], new JobSearchPreferences(), applyFilters: false, Ct);

        Assert.Equal((1, 1), (result.Added, result.Merged));
    }
}
