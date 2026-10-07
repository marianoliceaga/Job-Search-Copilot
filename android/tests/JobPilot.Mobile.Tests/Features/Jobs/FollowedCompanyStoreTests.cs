using JobPilot.Domain.Jobs;
using JobPilot.Mobile.Tests.TestSupport;
using static JobPilot.Mobile.Tests.TestSupport.JobsTestContext;

namespace JobPilot.Mobile.Tests.Features.Jobs;

public class FollowedCompanyStoreTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Seeds_the_initial_companies_only_once()
    {
        await using var context = await CreateAsync();
        context.WriteSeed("followed-companies.seed.json", new[] { new { name = "Contoso", ats = "Ashby", boardToken = "contoso", notes = "seed" } });

        var first = await context.Companies.GetAllAsync(Ct);
        await context.Companies.RemoveAsync(first[0].Id, Ct);
        var afterRemoval = await context.Companies.GetAllAsync(Ct);

        Assert.Equal(("Contoso", AtsKind.Ashby, "contoso"), (first[0].Name, first[0].Ats, first[0].BoardToken));
        Assert.Empty(afterRemoval);
    }

    [Fact]
    public async Task Does_not_follow_the_same_board_twice()
    {
        await using var context = await CreateAsync();

        Assert.True(await context.Companies.AddAsync("Contoso", AtsKind.Lever, "Contoso", null, Ct));
        Assert.False(await context.Companies.AddAsync("Contoso again", AtsKind.Lever, "Contoso", null, Ct));
        Assert.True(await context.Companies.AddAsync("Contoso", AtsKind.Ashby, "Contoso", null, Ct));
    }

    [Fact]
    public async Task Suggests_ats_boards_found_in_offers_that_are_not_followed()
    {
        await using var context = await CreateAsync();
        await context.Companies.AddAsync("Followed", AtsKind.Greenhouse, "followed", null, Ct);
        await context.Ingestion.IngestAsync(
        [
            Job(company: "Litware", description: "Apply at https://jobs.lever.co/litware/11111111-2222-3333-4444-555555555555 today"),
            Job("Backend .NET", "Litware", "https://remotive.com/jobs/2", description: "Apply: https://jobs.lever.co/litware/22222222-2222-3333-4444-555555555555"),
            Job("Other", "Followed Co", "https://remotive.com/jobs/3") with { ApplyUrl = "https://job-boards.greenhouse.io/followed/jobs/1" },
        ], new JobSearchPreferences(), applyFilters: false, Ct);

        var suggestion = Assert.Single(await context.Companies.GetSuggestionsAsync(Ct));

        Assert.Equal((AtsKind.Lever, "litware", "Litware", 2), (suggestion.Ats, suggestion.BoardToken, suggestion.CompanyName, suggestion.Postings));
    }

    [Fact]
    public async Task The_repository_seed_contains_only_verified_public_boards()
    {
        var seeds = new JobPilot.Mobile.Core.Features.Jobs.JobSeedFiles().LoadFollowedCompanies();

        Assert.NotEmpty(seeds);
        Assert.All(seeds, c => Assert.StartsWith("Verified ", c.Notes, StringComparison.Ordinal));
        Assert.Equal(seeds.Count, seeds.Select(c => (c.Ats, c.BoardToken)).Distinct().Count());
        await Task.CompletedTask;
    }
}
