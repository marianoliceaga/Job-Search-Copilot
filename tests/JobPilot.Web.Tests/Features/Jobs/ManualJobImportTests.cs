using System.Net;
using JobPilot.Domain.Llm;
using JobPilot.Web.Features.Jobs;
using JobPilot.Web.Features.Jobs.Sources;
using JobPilot.Web.Infrastructure.Llm;
using JobPilot.Web.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace JobPilot.Web.Tests.Features.Jobs;

public class ManualJobImportTests
{
    private const string PastedText = """
        Senior .NET Engineer at Contoso Cloud
        Remote - LATAM. Contractor engagement.
        Compensation: USD 6,500 - 8,000 per month.
        We use C#, ASP.NET Core and Azure.
        """;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly PromptLibrary Prompts = PromptLibrary.LoadFrom(Path.Combine(AppContext.BaseDirectory, "Prompts"));

    private static ManualJobImport Create(JobsTestContext context, ILlmClient llm, HttpMessageHandler? http = null)
    {
        var options = Options.Create(JobsTestContext.SourceOptions());
        var client = new HttpClient(http ?? new StubHttpHandler([]));
        IJobSource[] sources = [new GreenhouseSource(client, options), new LeverSource(client, options), new AshbySource(client, options)];
        return new ManualJobImport(sources, context.Ingestion, context.Preferences, Prompts, llm);
    }

    [Theory]
    [InlineData("https://www.linkedin.com/jobs/view/123456", "no permite el acceso automatizado")]
    [InlineData("https://careers.example.com/jobs/42", "no es de un ATS con API pública")]
    public async Task Other_sites_ask_for_the_pasted_text(string url, string message)
    {
        await using var context = await JobsTestContext.CreateAsync();
        var http = new StubHttpHandler([]);

        var result = await Create(context, new FakeLlmClient(), http).ImportFromUrlAsync(url, Ct);

        Assert.Equal(UrlImportOutcome.NeedsText, result.Outcome);
        Assert.Contains(message, result.Message, StringComparison.Ordinal);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task Ats_urls_are_read_through_the_public_api_without_keyword_filters()
    {
        await using var context = await JobsTestContext.CreateAsync();
        var http = new StubHttpHandler(new()
        {
            ["https://gh.test/boards/woodgrove/jobs"] = (HttpStatusCode.OK, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Sources", "greenhouse.json"))),
        });

        var result = await Create(context, new FakeLlmClient(), http)
            .ImportFromUrlAsync("https://job-boards.greenhouse.io/woodgrove/jobs/4000000002?gh_src=x", Ct);

        Assert.Equal(UrlImportOutcome.Imported, result.Outcome);
        await using var db = await context.Db.CreateDbContextAsync(Ct);
        var posting = await db.JobPostings.SingleAsync(Ct);
        Assert.Equal(("Account Executive", result.PostingId), (posting.Title, (Guid?)posting.Id));
    }

    [Fact]
    public async Task Closed_ats_postings_are_reported()
    {
        await using var context = await JobsTestContext.CreateAsync();
        var http = new StubHttpHandler(new() { ["https://gh.test/boards/woodgrove/jobs"] = (HttpStatusCode.OK, """{"jobs":[]}""") });

        var result = await Create(context, new FakeLlmClient(), http).ImportFromUrlAsync("https://job-boards.greenhouse.io/woodgrove/jobs/1", Ct);

        Assert.Equal(UrlImportOutcome.NotFound, result.Outcome);
    }

    [Fact]
    public async Task Extraction_keeps_the_pasted_text_as_description_and_a_verified_salary()
    {
        await using var context = await JobsTestContext.CreateAsync();
        var llm = new FakeLlmClient().Returns(new ExtractedJob("Senior .NET Engineer", "Contoso Cloud", "Remote - LATAM", "Contractor",
            "Compensation: USD 6,500 - 8,000 per month.", 6500, 8000, "usd", "month", ["C#", "ASP.NET Core", "Azure", "Kubernetes"]));
        var import = Create(context, llm);
        var prepared = import.PrepareExtraction(PastedText, "https://careers.example.com/jobs/42");

        var result = await import.ExtractAsync(prepared, Ct);

        Assert.Contains("<untrusted_data name=\"posting\">", prepared.Prompt.UserMessage, StringComparison.Ordinal);
        Assert.Empty(result.Warnings);
        var draft = result.Draft!;
        Assert.Equal(PastedText.Trim(), draft.Description);
        Assert.Equal((6500m, 8000m, "USD", Domain.Jobs.SalaryPeriod.Month), (draft.SalaryMin!.Value, draft.SalaryMax!.Value, draft.SalaryCurrency, draft.SalaryPeriod!.Value));
        Assert.Equal(["C#", "ASP.NET Core", "Azure"], draft.Tags);
        Assert.Equal("https://careers.example.com/jobs/42", draft.Url);

        var id = await import.SaveAsync(draft, Ct);
        await using var db = await context.Db.CreateDbContextAsync(Ct);
        Assert.Equal("manual", (await db.JobPostings.SingleAsync(p => p.Id == id, Ct)).SourceKey);
    }

    [Fact]
    public async Task A_salary_that_is_not_in_the_text_is_discarded()
    {
        await using var context = await JobsTestContext.CreateAsync();
        var llm = new FakeLlmClient().Returns(new ExtractedJob("Senior .NET Engineer", "Contoso Cloud", null, null,
            "Compensation: USD 9,000 per month.", 9000, null, "USD", "month", []));
        var import = Create(context, llm);

        var result = await import.ExtractAsync(import.PrepareExtraction(PastedText, null), Ct);

        Assert.Null(result.Draft!.SalaryText);
        Assert.Null(result.Draft.SalaryMin);
        Assert.Contains(result.Warnings, w => w.Contains("Se descartó la compensación", StringComparison.Ordinal));
        Assert.StartsWith("manual:", result.Draft.Url, StringComparison.Ordinal);
    }

    [Fact]
    public void Validation_requires_a_quote_for_amounts_and_a_known_period()
    {
        var problems = ManualJobImport.Validate(new ExtractedJob("T", "C", null, null, null, 100, null, "USD", "fortnight", []));

        Assert.Contains(problems, p => p.Contains("salaryPeriod", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("require salaryQuote", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Llm_errors_produce_no_draft()
    {
        await using var context = await JobsTestContext.CreateAsync();
        var import = Create(context, new FakeLlmClient().Fails(new LlmError(LlmErrorKind.RateLimited, "429")));

        var result = await import.ExtractAsync(import.PrepareExtraction(PastedText, null), Ct);

        Assert.Null(result.Draft);
        Assert.Equal(LlmErrorKind.RateLimited, result.Llm.Error!.Kind);
    }
}
