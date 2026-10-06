using JobPilot.Domain.Llm;
using JobPilot.Mobile.Core.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace JobPilot.Mobile.Core.Infrastructure.Llm;

public interface ILlmCallLog
{
    Task AddAsync(LlmCallRecord record);
}

public sealed record LlmCallSummary(int Calls, int Failed, long InputTokens, long OutputTokens, decimal EstimatedCostUsd, int CallsWithoutPrice);

public sealed record LlmUseCaseUsage(string UseCase, LlmCallSummary Summary);

public sealed partial class DbLlmCallLog(IDbContextFactory<MobileDbContext> dbFactory, ILogger<DbLlmCallLog> logger) : ILlmCallLog
{
    /// <summary>
    /// Persists the record. Runs even if the caller was cancelled (the call already happened and may have cost
    /// money); a logging failure is logged but never hides the LLM result from the owner.
    /// </summary>
    public async Task AddAsync(LlmCallRecord record)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(CancellationToken.None);
            db.LlmCalls.Add(record);
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (DbUpdateException ex)
        {
            LogPersistFailed(ex, record.UseCase);
        }
    }

    public async Task<IReadOnlyList<LlmCallRecord>> GetRecentAsync(int take, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.LlmCalls.AsNoTracking()
            .OrderByDescending(c => c.StartedAtUtc).ThenByDescending(c => c.Id)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public Task<LlmCallSummary> GetSummaryAsync(CancellationToken cancellationToken) => GetSummaryAsync(null, cancellationToken);

    /// <param name="sinceUtc">Only calls started at or after this time; null for all.</param>
    public async Task<LlmCallSummary> GetSummaryAsync(DateTime? sinceUtc, CancellationToken cancellationToken) =>
        Summarize(await LoadAsync(sinceUtc, cancellationToken));

    public async Task<IReadOnlyList<LlmUseCaseUsage>> GetUsageByUseCaseAsync(DateTime? sinceUtc, CancellationToken cancellationToken) =>
        (await LoadAsync(sinceUtc, cancellationToken))
            .GroupBy(r => r.UseCase)
            .Select(g => new LlmUseCaseUsage(g.Key, Summarize(g.ToList())))
            .OrderByDescending(u => u.Summary.EstimatedCostUsd).ThenByDescending(u => u.Summary.Calls)
            .ToList();

    private async Task<List<LlmCallRecord>> LoadAsync(DateTime? sinceUtc, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        // Aggregated in memory: SQLite cannot sum decimals and the table is small (single user).
        var query = db.LlmCalls.AsNoTracking();
        if (sinceUtc is { } since)
        {
            query = query.Where(c => c.StartedAtUtc >= since);
        }

        return await query.ToListAsync(cancellationToken);
    }

    private static LlmCallSummary Summarize(IReadOnlyCollection<LlmCallRecord> rows) => new(
        Calls: rows.Count,
        Failed: rows.Count(r => r.Outcome != LlmCallOutcome.Success),
        InputTokens: rows.Sum(r => (long)r.PromptTokens + r.ToolUsePromptTokens),
        OutputTokens: rows.Sum(r => (long)r.OutputTokens + r.ThoughtsTokens),
        EstimatedCostUsd: rows.Sum(r => r.EstimatedCostUsd ?? 0m),
        CallsWithoutPrice: rows.Count(r => r.EstimatedCostUsd is null && r.PromptTokens + r.OutputTokens > 0));

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not persist the LLM call record for use case {UseCase}.")]
    private partial void LogPersistFailed(Exception exception, string useCase);
}
