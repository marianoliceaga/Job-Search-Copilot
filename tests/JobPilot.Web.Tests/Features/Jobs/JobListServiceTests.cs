using JobPilot.Domain.Jobs;
using JobPilot.Web.Features.Jobs;
using JobPilot.Web.Tests.TestSupport;
using static JobPilot.Web.Tests.TestSupport.JobsTestContext;

namespace JobPilot.Web.Tests.Features.Jobs;

public class JobListServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Ranks_by_score_with_compensation_weighted_most_and_filters()
    {
        await using var context = await CreateAsync();
        var preferences = await context.Preferences.GetAsync(Ct);
        preferences.UsdPerUnit["EUR"] = 1.1m;
        await context.Preferences.SaveAsync(preferences, Ct);
        await context.Ingestion.IngestAsync(
        [
            Job("Well paid .NET", "A", "https://remotive.com/1") with { Location = "Worldwide", SalaryMin = 150_000, SalaryMax = 170_000, SalaryCurrency = "USD", SalaryPeriod = SalaryPeriod.Year },
            Job("Unpaid info .NET", "B", "https://remotive.com/2") with { Location = "LATAM" },
            Job("US only .NET", "C", "https://remotive.com/3") with { Location = "USA Only", SalaryMin = 160_000, SalaryCurrency = "USD", SalaryPeriod = SalaryPeriod.Year },
            Job("Euro .NET", "D", "https://remotive.com/4") with { Location = "Worldwide", SalaryMin = 5_000, SalaryCurrency = "EUR", SalaryPeriod = SalaryPeriod.Month },
            Job("Real .NET", "E", "https://remotive.com/5") with { Location = "Worldwide", SalaryMin = 900_000, SalaryCurrency = "BRL", SalaryPeriod = SalaryPeriod.Year },
        ], preferences, applyFilters: false, Ct);

        var all = await context.List.GetAsync(new JobListFilter(), Ct);
        var eligible = await context.List.GetAsync(new JobListFilter { HideUnlikelyEligible = true }, Ct);
        var searched = await context.List.GetAsync(new JobListFilter { Text = "euro" }, Ct);

        Assert.Equal("Well paid .NET", all[0].Posting.Title);
        Assert.All(all, i => Assert.True(i.Rank.IsPreliminary));
        Assert.DoesNotContain(eligible, i => i.Posting.Title == "US only .NET");
        Assert.Equal(66_000m, Assert.Single(searched).Compensation.Range!.Min);
        Assert.Contains("BRL", all.Single(i => i.Posting.Company == "E").Compensation.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Discarded_offers_are_listed_separately()
    {
        await using var context = await CreateAsync();
        var id = (await context.Ingestion.IngestAsync([Job()], new JobSearchPreferences(), false, Ct)).PostingIds[0];

        await context.List.SetDiscardedAsync(id, true, Ct);

        Assert.Empty(await context.List.GetAsync(new JobListFilter(), Ct));
        Assert.Single(await context.List.GetAsync(new JobListFilter { OnlyDiscarded = true }, Ct));
    }

    [Fact]
    public void Remote_detection_uses_the_source_and_location()
    {
        Assert.True(JobListService.IsRemote(Posting("remotive", "USA")));
        Assert.True(JobListService.IsRemote(Posting("greenhouse", "Remote - LATAM")));
        Assert.False(JobListService.IsRemote(Posting("greenhouse", "New York, NY")));
    }

    private static JobPosting Posting(string source, string location) => new()
    {
        SourceKey = source, SourceName = source, Title = "T", Company = "C", Url = "u", UrlKey = "u", Fingerprint = "f", Location = location,
    };
}
