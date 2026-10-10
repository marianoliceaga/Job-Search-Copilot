using JobPilot.Mobile.Core.Features.Jobs;
using JobPilot.Mobile.Core.Features.Jobs.Sources;
using JobPilot.Mobile.Core.Infrastructure.Persistence;
using JobPilot.Mobile.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using static JobPilot.Mobile.Tests.TestSupport.JobsTestContext;

namespace JobPilot.Mobile.Tests.Features.Jobs;

public class JobSearchServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Runs_enabled_sources_reports_progress_and_stores_matching_offers()
    {
        await using var context = await CreateAsync();
        var a = FakeJobSource.Returning("fake-a", Job());
        var b = FakeJobSource.Returning("fake-b", Job("Java Developer", url: "https://b.test/2", description: "Spring"));
        var progress = new List<SearchProgress>();

        var reports = await context.Search([a, b]).RunAsync(new SynchronousProgress<SearchProgress>(progress.Add), Ct);

        Assert.All(reports, r => Assert.Equal(SourceRunStatus.Succeeded, r.Status));
        Assert.Equal(1, reports[0].Ingestion!.Added);
        Assert.Equal(0, reports[1].Ingestion!.Matched);
        Assert.Equal([0, 1, 2], progress.Select(p => p.Completed));
        Assert.Equal("fake-a", progress[0].Current);
        await using var db = await context.Db.CreateDbContextAsync(Ct);
        Assert.Equal(2, await db.SourceRuns.CountAsync(Ct));
    }

    [Fact]
    public async Task Without_keywords_no_source_is_queried()
    {
        await using var context = await CreateAsync(keywords: false);
        var source = FakeJobSource.Returning("fake-a", Job());

        var report = Assert.Single(await context.Search([source]).RunAsync(null, Ct));

        Assert.Equal(SourceRunStatus.Skipped, report.Status);
        Assert.Equal(JobSearchService.NoKeywordsMessage, report.Message);
        Assert.Equal(0, source.Calls);
        await using var db = await context.Db.CreateDbContextAsync(Ct);
        Assert.Equal(0, await db.SourceRuns.CountAsync(Ct));
        Assert.Equal(0, await db.JobPostings.CountAsync(Ct));
    }

    [Fact]
    public async Task Respects_the_minimum_interval_between_requests_to_a_source()
    {
        await using var context = await CreateAsync();
        var source = FakeJobSource.Returning("fake-a", Job());
        var search = context.Search([source], minIntervalMinutes: 360);

        await search.RunAsync(null, Ct);
        context.Time.Advance(TimeSpan.FromHours(1));
        var second = await search.RunAsync(null, Ct);
        context.Time.Advance(TimeSpan.FromHours(6));
        var third = await search.RunAsync(null, Ct);

        Assert.Equal(SourceRunStatus.Skipped, second.Single().Status);
        Assert.Contains("intervalo mínimo", second.Single().Message, StringComparison.Ordinal);
        Assert.Equal(SourceRunStatus.Succeeded, third.Single().Status);
        Assert.Equal(2, source.Calls);
    }

    [Fact]
    public async Task A_failing_source_does_not_stop_the_others_and_counts_as_a_request()
    {
        await using var context = await CreateAsync();
        var failing = new FakeJobSource("fake-a", (_, _) => throw new HttpRequestException("503 Service Unavailable"));
        var working = FakeJobSource.Returning("fake-b", Job());
        var search = context.Search([failing, working]);

        var reports = await search.RunAsync(null, Ct);
        var status = await search.GetStatusAsync(Ct);

        Assert.Equal(SourceRunStatus.Failed, reports[0].Status);
        Assert.Contains("503", reports[0].Message, StringComparison.Ordinal);
        Assert.Equal(SourceRunStatus.Succeeded, reports[1].Status);
        Assert.NotNull(status.Single(s => s.SourceKey == "fake-a").NextAllowedUtc);
    }

    [Fact]
    public async Task Disabled_sources_are_not_read()
    {
        await using var context = await CreateAsync();
        var preferences = await context.Preferences.GetAsync(Ct);
        preferences.DisabledSources = ["fake-a"];
        await context.Preferences.SaveAsync(preferences, Ct);
        var disabled = FakeJobSource.Returning("fake-a", Job());

        var reports = await context.Search([disabled]).RunAsync(null, Ct);

        Assert.Empty(reports);
        Assert.Equal(0, disabled.Calls);
    }

    [Fact]
    public async Task Cancellation_stops_the_search_and_is_recorded()
    {
        await using var context = await CreateAsync();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var hanging = new FakeJobSource("fake-a", async (_, token) =>
        {
            await cts.CancelAsync();
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException("Unreachable.");
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => context.Search([hanging]).RunAsync(null, cts.Token));

        await using var db = await context.Db.CreateDbContextAsync(Ct);
        Assert.Equal(SourceRunStatus.Cancelled, (await db.SourceRuns.SingleAsync(Ct)).Status);
    }

    [Fact]
    public async Task Ats_sources_read_every_followed_board_and_warn_about_missing_ones()
    {
        await using var context = await CreateAsync();
        await context.Companies.AddAsync("Woodgrove Bank", Domain.Jobs.AtsKind.Greenhouse, "woodgrove", null, Ct);
        await context.Companies.AddAsync("Gone Inc", Domain.Jobs.AtsKind.Greenhouse, "gone", null, Ct);
        var handler = new StubHttpHandler(new()
        {
            ["https://gh.test/boards/woodgrove/jobs"] = (System.Net.HttpStatusCode.OK, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Sources", "greenhouse.json"))),
        });
        var greenhouse = new GreenhouseSource(new HttpClient(handler), Microsoft.Extensions.Options.Options.Create(SourceOptions()));

        var report = (await context.Search([greenhouse]).RunAsync(null, Ct)).Single();

        Assert.Equal(SourceRunStatus.Succeeded, report.Status);
        Assert.Equal((2, 1), (report.Ingestion!.Received, report.Ingestion.Added));
        Assert.Contains("\"gone\"", report.Message, StringComparison.Ordinal);
        Assert.Equal(2, handler.Requests.Count);
    }

    private sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
