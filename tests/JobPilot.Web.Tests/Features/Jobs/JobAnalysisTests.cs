using JobPilot.Domain.Jobs;
using JobPilot.Web.Features.Jobs;
using JobPilot.Web.Features.Jobs.Analysis;
using JobPilot.Web.Infrastructure.Llm;
using JobPilot.Web.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace JobPilot.Web.Tests.Features.Jobs;

public class JobAnalysisTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly PromptLibrary Prompts = PromptLibrary.LoadFrom(Path.Combine(AppContext.BaseDirectory, "Prompts"));

    private static JobPosting Posting(string description = "We need C# and Kubernetes. Contractor role. Open to LATAM. Pay: USD 120k - 150k per year.") => new()
    {
        Id = Guid.NewGuid(),
        SourceKey = "remotive",
        SourceName = "Remotive",
        Title = "Senior .NET Engineer",
        Company = "Contoso",
        Url = "https://remotive.com/jobs/1",
        UrlKey = "https://remotive.com/jobs/1",
        Fingerprint = "contoso|senior .net engineer",
        Location = "LATAM",
        Description = description,
    };

    private static JobAnalysisResponse Response(CompensationFinding? compensation = null, ContractFinding? contract = null, IReadOnlyList<string>? missing = null) => new(
        FitScore: 82,
        FitJustification: "Buen encaje.",
        Requirements: [new RequirementAssessment("C#", RequirementStatus.Met, "17 años de C#")],
        MissingKeywords: [.. missing ?? ["Kubernetes"]],
        Compensation: compensation ?? new CompensationFinding("Pay: USD 120k - 150k per year.", 120_000, 150_000, "USD", "year"),
        Contract: contract ?? new ContractFinding(ContractModel.Contractor, Certainty.Stated, "Contractor role.", "Lo dice la oferta."),
        Eligibility: new EligibilityFinding(EligibilityVerdict.Eligible, "Open to LATAM.", "Abierta a LATAM."),
        RedFlags: [],
        Summary: "Oferta interesante.");

    [Fact]
    public void A_quoted_salary_is_kept()
    {
        var verified = AnalysisVerifier.Verify(Response(), Posting(), "C#");

        Assert.Equal(CompensationOrigin.PostingQuote, verified.CompensationOrigin);
        Assert.Equal((120_000m, 150_000m), (verified.Analysis.Compensation.Min!.Value, verified.Analysis.Compensation.Max!.Value));
        Assert.Empty(verified.VerificationNotes);
    }

    [Fact]
    public void Compensation_without_a_verifiable_quote_becomes_not_informed()
    {
        var posting = Posting("We need C#. No salary information here.");

        var guessed = AnalysisVerifier.Verify(Response(new CompensationFinding("Pay: USD 120k - 150k per year.", 120_000, 150_000, "USD", "year")), posting, "C#");
        var noQuote = AnalysisVerifier.Verify(Response(new CompensationFinding(null, null, null, null, null), missing: []), posting, "C#");

        Assert.Equal(CompensationOrigin.NotInformed, guessed.CompensationOrigin);
        Assert.Equal(new CompensationFinding(null, null, null, null, null), guessed.Analysis.Compensation);
        Assert.Contains(guessed.VerificationNotes, n => n.Contains("no informada", StringComparison.Ordinal));
        Assert.Equal(CompensationOrigin.NotInformed, noQuote.CompensationOrigin);
        Assert.DoesNotContain(noQuote.VerificationNotes, n => n.Contains("Compensación", StringComparison.Ordinal));
    }

    [Fact]
    public void Amounts_that_are_not_in_the_quote_are_removed()
    {
        var verified = AnalysisVerifier.Verify(Response(new CompensationFinding("Pay: USD 120k - 150k per year.", 120_000, 180_000, "USD", "year")), Posting(), "C#");

        Assert.Equal(120_000m, verified.Analysis.Compensation.Min);
        Assert.Null(verified.Analysis.Compensation.Max);
    }

    [Fact]
    public void The_source_structured_salary_wins_over_the_llm()
    {
        var posting = Posting();
        posting.SalaryMin = 100_000;
        posting.SalaryCurrency = "USD";
        posting.SalaryPeriod = SalaryPeriod.Year;

        var verified = AnalysisVerifier.Verify(Response(), posting, "C#");

        Assert.Equal(CompensationOrigin.Source, verified.CompensationOrigin);
        Assert.Equal(100_000m, verified.Analysis.Compensation.Min);
    }

    [Fact]
    public void Unverifiable_quotes_downgrade_the_contract_model_and_drop_wrong_keywords()
    {
        var verified = AnalysisVerifier.Verify(
            Response(contract: new ContractFinding(ContractModel.Employee, Certainty.Stated, "Full-time W2 employee.", "x"), missing: ["Kubernetes", "C#", "Terraform"]),
            Posting(), "Skills: C#");

        Assert.Equal(Certainty.Inferred, verified.Analysis.Contract.Certainty);
        Assert.Null(verified.Analysis.Contract.Quote);
        Assert.Equal(["Kubernetes"], verified.Analysis.MissingKeywords);
    }

    [Fact]
    public void Validation_rejects_out_of_range_scores_and_unquoted_amounts()
    {
        var problems = JobAnalysisHandler.Validate(Response(new CompensationFinding(null, 1, 2, "USD", "year")) with { FitScore = 140 });

        Assert.Contains("fitScore must be between 0 and 100", problems);
        Assert.Contains("compensation amounts require a verbatim quote of the posting", problems);
    }

    [Fact]
    public async Task Analysis_requires_a_master_cv()
    {
        await using var context = await JobsTestContext.CreateAsync();
        var id = (await context.Ingestion.IngestAsync([JobsTestContext.Job()], new JobSearchPreferences(), false, Ct)).PostingIds[0];

        var (prepared, error) = await Handler(context, new FakeLlmClient()).PrepareAsync(id, Ct);

        Assert.Null(prepared);
        Assert.Contains("CV maestro", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Analysis_sends_profile_cv_and_wrapped_posting_and_stores_the_verified_result()
    {
        await using var context = await JobsTestContext.CreateAsync();
        await context.SaveCvAsync();
        var job = JobsTestContext.Job(description: "We need C# and Kubernetes. Contractor role. Open to LATAM. Pay: USD 120k - 150k per year.");
        var id = (await context.Ingestion.IngestAsync([job], new JobSearchPreferences(), false, Ct)).PostingIds[0];
        var llm = new FakeLlmClient().Returns(Response());
        var handler = Handler(context, llm);

        var (prepared, _) = await handler.PrepareAsync(id, Ct);
        var result = await handler.RunAsync(prepared!, Ct);

        var prompt = Assert.Single(llm.Prompts);
        Assert.Contains("Works remotely from: Argentina", prompt.UserMessage, StringComparison.Ordinal);
        Assert.Contains("Tech Lead at Northwind", prompt.UserMessage, StringComparison.Ordinal);
        Assert.Contains("<untrusted_data name=\"posting\">\nTitle: Senior .NET Engineer", prompt.UserMessage, StringComparison.Ordinal);
        Assert.Contains("remotely from Argentina", prompt.SystemInstruction, StringComparison.Ordinal);
        Assert.Equal(82, result.Analysis!.Analysis.FitScore);

        var item = await context.List.GetItemAsync(id, Ct);
        Assert.NotNull(item!.Analysis);
        Assert.True(item.EligibilityFromAnalysis);
        Assert.Equal(EligibilityStatus.Likely, item.Eligibility.Status);
        Assert.False(item.Rank.IsPreliminary);
        Assert.Equal(135_000m, item.Compensation.Range!.Midpoint);
        await using var db = await context.Db.CreateDbContextAsync(Ct);
        Assert.Equal("job-analysis.v1", (await db.JobAnalyses.SingleAsync(Ct)).PromptId);
    }

    [Fact]
    public async Task The_analysis_schema_round_trips_through_the_real_gemini_client()
    {
        await using var host = await LlmTestHost.CreateAsync();
        host.Handler.RespondJson(Response());
        var prompt = Prompts.Get(LlmUseCases.JobAnalysis).Render(new Dictionary<string, string>
        {
            ["country"] = "Argentina", ["profile"] = "p", ["cv"] = "c", ["posting"] = "x",
        });

        var result = await host.Client.GenerateAsync(new LlmRequest<JobAnalysisResponse>(prompt), Ct);

        Assert.True(result.IsSuccess, result.Error?.ToString());
        var schema = Assert.Single(host.Handler.Requests).Json["generationConfig"]!["responseFormat"]!["text"]!["schema"]!;
        Assert.Equal(["Contractor", "Eor", "Employee", "Unknown"], schema["properties"]!["contract"]!["properties"]!["model"]!["enum"]!.AsArray().Select(n => (string?)n));
    }

    private static JobAnalysisHandler Handler(JobsTestContext context, ILlmClient llm) =>
        new(context.Db, context.Profiles, context.Cvs, Prompts, llm, context.Time);
}
