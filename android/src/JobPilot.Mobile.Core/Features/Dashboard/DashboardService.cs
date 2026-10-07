using JobPilot.Domain.Pipeline;
using JobPilot.Mobile.Core.Features.Pipeline;
using JobPilot.Mobile.Core.Infrastructure.Llm;
using JobPilot.Mobile.Core.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JobPilot.Mobile.Core.Features.Dashboard;

public sealed record OfferCounts(int Total, int Last7Days, int Last30Days, int Analyzed, int Discarded);

public sealed record DashboardData(
    OfferCounts Offers,
    PipelineMetricsResult Pipeline,
    IReadOnlyList<PipelineCard> DueReminders,
    IReadOnlyList<PipelineCard> UpcomingInterviews,
    LlmCallSummary LlmTotal,
    LlmCallSummary LlmLast30Days,
    IReadOnlyList<LlmUseCaseUsage> LlmByUseCase);

/// <summary>Metrics computed on load from the local database.</summary>
public sealed class DashboardService(IDbContextFactory<MobileDbContext> dbFactory, ApplicationStore applications, DbLlmCallLog calls, TimeProvider time)
{
    public const int UpcomingDays = 14;

    public async Task<DashboardData> GetAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow().UtcDateTime;
        OfferCounts offers;
        await using (var db = await dbFactory.CreateDbContextAsync(cancellationToken))
        {
            offers = new OfferCounts(
                await db.JobPostings.CountAsync(cancellationToken),
                await db.JobPostings.CountAsync(p => p.FirstSeenAtUtc >= now.AddDays(-7), cancellationToken),
                await db.JobPostings.CountAsync(p => p.FirstSeenAtUtc >= now.AddDays(-30), cancellationToken),
                await db.JobAnalyses.Select(a => a.JobPostingId).Distinct().CountAsync(cancellationToken),
                await db.JobPostings.CountAsync(p => p.IsDiscarded, cancellationToken));
        }

        var all = await applications.GetAllAsync(cancellationToken);
        var board = await applications.GetBoardAsync(cancellationToken);
        return new DashboardData(
            offers,
            PipelineMetrics.Compute(all),
            board.Where(c => c.Reminder?.IsDue(now) == true).OrderBy(c => c.Reminder!.DueAtUtc).ToList(),
            board.Where(c => c.NextInterviewUtc is { } at && at <= now.AddDays(UpcomingDays)).OrderBy(c => c.NextInterviewUtc).ToList(),
            await calls.GetSummaryAsync(cancellationToken),
            await calls.GetSummaryAsync(now.AddDays(-30), cancellationToken),
            await calls.GetUsageByUseCaseAsync(null, cancellationToken));
    }
}
