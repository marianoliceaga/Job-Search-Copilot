using JobPilot.Domain.Jobs;
using JobPilot.Mobile.Tests.TestSupport;
using static JobPilot.Mobile.Tests.TestSupport.JobsTestContext;

namespace JobPilot.Mobile.Tests.Features.Jobs;

public class FollowedCompanyStoreTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_list_starts_empty_and_keeps_what_the_owner_adds_and_removes()
    {
        await using var context = await CreateAsync();

        Assert.Empty(await context.Companies.GetAllAsync(Ct));
        await context.Companies.AddAsync("Contoso", AtsKind.Ashby, "contoso", null, Ct);
        var added = Assert.Single(await context.Companies.GetAllAsync(Ct));
        await context.Companies.RemoveAsync(added.Id, Ct);

        Assert.Equal(("Contoso", AtsKind.Ashby, "contoso"), (added.Name, added.Ats, added.BoardToken));
        Assert.Empty(await context.Companies.GetAllAsync(Ct));
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
    public void The_app_does_not_embed_the_web_search_seeds()
    {
        var resources = typeof(JobPilot.Mobile.Core.Features.Jobs.FollowedCompanyStore).Assembly.GetManifestResourceNames();

        Assert.DoesNotContain(resources, name => name.Contains("followed-companies.seed", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(resources, name => name.Contains("job-search.seed", StringComparison.OrdinalIgnoreCase));
    }
}
