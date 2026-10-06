using System.Text;
using System.Text.Json;
using JobPilot.Domain.Jobs;
using JobPilot.Domain.Prompts;
using JobPilot.Web.Features.Profile;
using JobPilot.Web.Infrastructure.Llm;
using JobPilot.Web.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JobPilot.Web.Features.Jobs.Analysis;

public sealed record PreparedJobAnalysis(JobPosting Posting, RenderedPrompt Prompt, string CvText);

public sealed record JobAnalysisRunResult(LlmResult<JobAnalysisResponse> Llm, VerifiedJobAnalysis? Analysis);

/// <summary>Analyses an offer against the profile and the master CV, verifies the result and stores it.</summary>
public sealed class JobAnalysisHandler(
    IDbContextFactory<JobPilotDbContext> dbFactory,
    ProfileStore profiles,
    MasterCvStore cvs,
    PromptLibrary prompts,
    ILlmClient llm,
    TimeProvider time)
{
    public async Task<(PreparedJobAnalysis? Prepared, string? Error)> PrepareAsync(Guid postingId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var posting = await db.JobPostings.AsNoTracking().FirstOrDefaultAsync(p => p.Id == postingId, cancellationToken);
        if (posting is null)
        {
            return (null, "La oferta no existe.");
        }

        if (string.IsNullOrWhiteSpace(posting.Description))
        {
            return (null, "La oferta no tiene descripción (por ejemplo, llegó por una alerta de LinkedIn): abrila en la fuente y pegá el texto en \"Descripción\" para analizarla.");
        }

        var cv = await cvs.GetAsync(cancellationToken);
        if (cv is null)
        {
            return (null, "Primero importá tu CV maestro (Perfil y CV maestro): el análisis compara la oferta con él.");
        }

        var profile = await profiles.GetAsync(cancellationToken);
        var cvText = CandidateText.Cv(cv.Cv);
        var prompt = prompts.Get(LlmUseCases.JobAnalysis).Render(new Dictionary<string, string>
        {
            ["country"] = profile.WorkFromCountry ?? "the candidate's country (not stated)",
            ["profile"] = CandidateText.Profile(profile),
            ["cv"] = cvText,
            ["posting"] = PostingText(posting),
        });
        return (new PreparedJobAnalysis(posting, prompt, cvText), null);
    }

    public async Task<JobAnalysisRunResult> RunAsync(PreparedJobAnalysis prepared, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        var result = await llm.GenerateAsync(new LlmRequest<JobAnalysisResponse>(prepared.Prompt) { Validate = Validate }, cancellationToken);
        if (!result.IsSuccess)
        {
            return new JobAnalysisRunResult(result, null);
        }

        var verified = AnalysisVerifier.Verify(result.Value!, prepared.Posting, prepared.CvText);
        await using var db = await dbFactory.CreateDbContextAsync(CancellationToken.None);
        db.JobAnalyses.Add(new JobAnalysisRecord
        {
            JobPostingId = prepared.Posting.Id,
            CreatedAtUtc = time.GetUtcNow().UtcDateTime,
            PromptId = prepared.Prompt.PromptId,
            Model = result.Model,
            FitScore = verified.Analysis.FitScore,
            Json = JsonSerializer.Serialize(verified, LlmJson.Options),
        });
        await db.SaveChangesAsync(CancellationToken.None);
        return new JobAnalysisRunResult(result, verified);
    }

    public static string PostingText(JobPosting posting)
    {
        ArgumentNullException.ThrowIfNull(posting);
        var builder = new StringBuilder();
        builder.AppendLine($"Title: {posting.Title}");
        builder.AppendLine($"Company: {posting.Company}");
        builder.AppendLine($"Location: {posting.Location ?? "not stated"}");
        if (posting.EmploymentType is not null)
        {
            builder.AppendLine($"Employment type: {posting.EmploymentType}");
        }

        if (posting.SalaryText is not null)
        {
            builder.AppendLine($"Salary: {posting.SalaryText}");
        }

        if (posting.Tags.Count > 0)
        {
            builder.AppendLine($"Tags: {string.Join(", ", posting.Tags)}");
        }

        builder.AppendLine().AppendLine(posting.Description);
        return builder.ToString().Trim();
    }

    internal static IReadOnlyList<string> Validate(JobAnalysisResponse analysis)
    {
        var problems = new List<string>();
        if (analysis.FitScore is < 0 or > 100)
        {
            problems.Add("fitScore must be between 0 and 100");
        }

        if (analysis.Compensation is { Min: { } min, Max: { } max } && min > max)
        {
            problems.Add("compensation.min is greater than compensation.max");
        }

        if (analysis.Compensation.Quote is null && (analysis.Compensation.Min is not null || analysis.Compensation.Max is not null))
        {
            problems.Add("compensation amounts require a verbatim quote of the posting");
        }

        if (string.IsNullOrWhiteSpace(analysis.FitJustification) || string.IsNullOrWhiteSpace(analysis.Summary))
        {
            problems.Add("fitJustification and summary are required");
        }

        return problems;
    }
}
