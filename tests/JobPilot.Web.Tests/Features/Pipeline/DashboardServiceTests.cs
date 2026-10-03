using JobPilot.Domain.Llm;
using JobPilot.Domain.Pipeline;
using JobPilot.Web.Features.Dashboard;
using JobPilot.Web.Infrastructure.Llm;
using Microsoft.Extensions.Logging.Abstractions;

namespace JobPilot.Web.Tests.Features.Pipeline;

public class DashboardServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Metrics_include_offers_pipeline_reminders_interviews_and_llm_cost()
    {
        await using var context = await PipelineTestContext.CreateAsync();
        var calls = new DbLlmCallLog(context.Jobs.Db, NullLogger<DbLlmCallLog>.Instance);
        var now = context.Now;
        await calls.AddAsync(Call("job-analysis", now.AddDays(-40), 0.010m));
        await calls.AddAsync(Call("job-analysis", now.AddDays(-1), 0.002m));
        await calls.AddAsync(Call("follow-up-email", now.AddDays(-2), null));

        var applied = await context.Store.StartAsync(await context.AddPostingAsync(), ApplicationStage.Applied, Ct);
        var interviewing = await context.Store.StartAsync(await context.AddPostingAsync("Staff Engineer", "Contoso", "https://remotive.com/jobs/2"), ApplicationStage.Applied, Ct);
        await context.AddPostingAsync("Platform Engineer", "Fabrikam", "https://remotive.com/jobs/3");
        context.Jobs.Time.Advance(TimeSpan.FromDays(8));
        await context.Store.MoveAsync(interviewing, ApplicationStage.Interview, null, Ct);
        await context.Store.ScheduleInterviewAsync(interviewing, context.Now.AddDays(2), 60, null, Ct);

        var data = await new DashboardService(context.Jobs.Db, context.Store, calls, context.Jobs.Time).GetAsync(Ct);

        Assert.Equal((3, 0, 3), (data.Offers.Total, data.Offers.Last7Days, data.Offers.Last30Days));
        Assert.Equal((2, 1), (data.Pipeline.Applied, data.Pipeline.Responded));
        Assert.Equal(applied, Assert.Single(data.DueReminders).Id);
        Assert.Equal(interviewing, Assert.Single(data.UpcomingInterviews).Id);
        Assert.Equal((3, 0.012m, 1), (data.LlmTotal.Calls, data.LlmTotal.EstimatedCostUsd, data.LlmTotal.CallsWithoutPrice));
        Assert.Equal((2, 0.002m), (data.LlmLast30Days.Calls, data.LlmLast30Days.EstimatedCostUsd));
        Assert.Equal(["job-analysis", "follow-up-email"], data.LlmByUseCase.Select(u => u.UseCase));
        Assert.Equal(2, data.LlmByUseCase[0].Summary.Calls);
    }

    private static LlmCallRecord Call(string useCase, DateTime at, decimal? cost) => new()
    {
        UseCase = useCase,
        Model = "gemini-test",
        PromptId = useCase + ".v1",
        StartedAtUtc = at,
        Outcome = LlmCallOutcome.Success,
        PromptTokens = 1000,
        OutputTokens = 200,
        EstimatedCostUsd = cost,
    };
}
