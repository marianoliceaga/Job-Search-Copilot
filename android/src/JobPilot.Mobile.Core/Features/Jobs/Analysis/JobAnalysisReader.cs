using System.Text;
using System.Text.Json;
using JobPilot.Mobile.Core.Infrastructure.Llm;
using JobPilot.Mobile.Core.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JobPilot.Mobile.Core.Features.Jobs.Analysis;

/// <summary>Reads the latest verified analysis of an offer.</summary>
public sealed class JobAnalysisReader(IDbContextFactory<MobileDbContext> dbFactory)
{
    public async Task<VerifiedJobAnalysis?> LatestAsync(Guid postingId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var json = await db.JobAnalyses.AsNoTracking()
            .Where(a => a.JobPostingId == postingId)
            .OrderByDescending(a => a.CreatedAtUtc).ThenByDescending(a => a.Id)
            .Select(a => a.Json)
            .FirstOrDefaultAsync(cancellationToken);
        return json is null ? null : JsonSerializer.Deserialize<VerifiedJobAnalysis>(json, LlmJson.Options);
    }

    /// <summary>Requirements and missing keywords, as context for tailoring (derived from untrusted posting text).</summary>
    public async Task<string?> LatestSummaryAsync(Guid postingId, CancellationToken cancellationToken)
    {
        var verified = await LatestAsync(postingId, cancellationToken);
        if (verified is null)
        {
            return null;
        }

        var builder = new StringBuilder();
        builder.AppendLine("Requirements:");
        foreach (var requirement in verified.Analysis.Requirements)
        {
            builder.AppendLine($"- [{requirement.Status}] {requirement.Requirement}");
        }

        if (verified.Analysis.MissingKeywords.Count > 0)
        {
            builder.AppendLine($"Keywords of the posting missing from the CV (do NOT claim them): {string.Join(", ", verified.Analysis.MissingKeywords)}");
        }

        return builder.ToString().Trim();
    }
}
