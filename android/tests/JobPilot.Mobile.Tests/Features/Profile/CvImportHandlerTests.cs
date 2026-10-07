using System.Net;
using JobPilot.Domain.Llm;
using JobPilot.Mobile.Core.Features.Profile.Import;
using JobPilot.Mobile.Core.Infrastructure.Llm;
using JobPilot.Mobile.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace JobPilot.Mobile.Tests.Features.Profile;

public class CvImportHandlerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly PromptLibrary Prompts = PromptLibrary.LoadEmbedded();

    private static ExtractedDocument FixtureDocument(string fileName = "fictional-cv.md") =>
        CvTextExtractor.Extract(fileName, File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Cv", fileName))).Document!;

    [Fact]
    public void The_prompt_contains_the_whole_extracted_text()
    {
        var document = FixtureDocument();

        var prepared = new CvImportHandler(Prompts, new FakeLlmClient()).Prepare(document);

        Assert.Equal("cv-import.v1", prepared.Prompt.PromptId);
        Assert.Contains($"<cv_document>\n{document.Text}\n</cv_document>", prepared.Prompt.UserMessage, StringComparison.Ordinal);
        Assert.Contains("Never infer, complete, translate", prepared.Prompt.SystemInstruction, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Maps_the_llm_result_to_a_master_cv_draft_with_ids_and_no_flags_when_faithful()
    {
        var llm = new FakeLlmClient().Returns(FaithfulImport());
        var handler = new CvImportHandler(Prompts, llm);

        var result = await handler.RunAsync(handler.Prepare(FixtureDocument()), Ct);

        Assert.True(result.Llm.IsSuccess);
        var cv = Assert.IsType<JobPilot.Domain.Cv.MasterCv>(result.Cv);
        Assert.Empty(result.Flags);
        Assert.Equal(3, cv.Experiences.Count);
        Assert.Equal(new JobPilot.Domain.Cv.PartialDate(2019, 3), cv.Experiences[0].Start);
        Assert.True(cv.Experiences[0].IsCurrent);
        Assert.Equal(new JobPilot.Domain.Cv.PartialDate(2009), cv.Experiences[2].Start);
        Assert.DoesNotContain(Guid.Empty, cv.AllElementIds());
        Assert.Empty(cv.Validate());
    }

    [Fact]
    public async Task Flags_values_that_are_not_in_the_document()
    {
        var import = FaithfulImport() with
        {
            Skills = [new ImportedSkill("Kubernetes", "Cloud")],
        };
        import.Experiences[0].Achievements[0] = "Cut payment processing latency by 70% for 2,000,000 monthly transactions.";
        var handler = new CvImportHandler(Prompts, new FakeLlmClient().Returns(import));

        var result = await handler.RunAsync(handler.Prepare(FixtureDocument()), Ct);

        Assert.Contains(result.Flags, f => f.ElementKey == result.Cv!.Skills[0].Id.ToString());
        Assert.Contains(result.Flags, f => f.ElementKey == result.Cv!.Experiences[0].Achievements[0].Id.ToString() && f.Message.Contains("70", StringComparison.Ordinal));
    }

    [Fact]
    public void Validation_rejects_bad_dates_and_inconsistent_current_roles()
    {
        var import = FaithfulImport() with
        {
            Experiences =
            [
                new ImportedExperience("Acme", "Dev", null, "March 2019", null, false, null, [], []),
                new ImportedExperience("Acme", "Dev", null, "2019", "2020", true, null, [], []),
                new ImportedExperience("", "Dev", null, "2021", "2020", false, null, [], []),
            ],
        };

        var problems = CvImportHandler.Validate(import);

        Assert.Contains(problems, p => p.Contains("experiences[0].start \"March 2019\"", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("experiences[1]: isCurrent is true", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("experiences[2].company is empty", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("experiences[2].end is before start", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Llm_errors_produce_no_draft()
    {
        var llm = new FakeLlmClient().Fails(new LlmError(LlmErrorKind.Timeout, "Gemini no respondió a tiempo."));
        var handler = new CvImportHandler(Prompts, llm);

        var result = await handler.RunAsync(handler.Prepare(FixtureDocument()), Ct);

        Assert.Null(result.Cv);
        Assert.Empty(result.Flags);
        Assert.Equal(LlmErrorKind.Timeout, result.Llm.Error?.Kind);
    }

    [Fact]
    public async Task The_import_schema_round_trips_through_the_real_gemini_client()
    {
        await using var host = await LlmTestHost.CreateAsync();
        host.Handler.RespondJson(FaithfulImport());
        var handler = new CvImportHandler(Prompts, host.Client);

        var result = await handler.RunAsync(handler.Prepare(FixtureDocument("fictional-cv.pdf")), Ct);

        Assert.True(result.Llm.IsSuccess, result.Llm.Error?.ToString());
        Assert.Equal("Northwind Bank", result.Cv!.Experiences[0].Company);
        var schema = Assert.Single(host.Handler.Requests).Json["generationConfig"]!["responseFormat"]!["text"]!["schema"]!;
        Assert.NotNull(schema["properties"]!["experiences"]!["items"]!["properties"]!["achievements"]);
        Assert.Equal("cv-import", Assert.Single(await host.CallRecordsAsync()).UseCase);
    }

    internal static ImportedCv FaithfulImportForTests() => FaithfulImport();

    private static ImportedCv FaithfulImport() => new(
        new ImportedContact("Alex Rivera", "alex.rivera@example.com", "+1 (555) 0100", "Córdoba, Argentina",
            "https://www.linkedin.com/in/alex-rivera-example", null, null),
        "Senior Software Engineer",
        "Backend engineer with 15 years of experience building payment and logistics platforms with C# and .NET.",
        [
            new ImportedExperience("Northwind Bank", "Tech Lead", null, "2019-03", null, true,
                "Leads a team of 6 engineers working on the payments platform.",
                [
                    "Cut payment processing latency by 40% for 2,000,000 monthly transactions.",
                    "Migrated the core ledger from WCF to ASP.NET Core and Azure.",
                ],
                ["WCF", "ASP.NET Core", "Azure"]),
            new ImportedExperience("Contoso Retail", "Senior Software Engineer", null, "2014-06", "2019-02", false, null,
                ["Built the order management API used by 120 stores.", "Introduced automated testing, raising coverage from 20% to 75%."],
                []),
            new ImportedExperience("Fabrikam Logistics", "Software Developer", null, "2009", "2014", false, null,
                ["Developed route planning tools in C# and SQL Server."], ["C#", "SQL Server"]),
        ],
        [new ImportedSkill("C#", "Languages"), new ImportedSkill("Blazor", "Frameworks"), new ImportedSkill("Azure", "Cloud")],
        [new ImportedCertification("Microsoft Certified: Azure Developer Associate", null, "2021", null)],
        [new ImportedLanguage("Spanish", "native"), new ImportedLanguage("English", "C1")],
        [new ImportedEducation("Example State University", "B.Sc.", "Computer Science", "2004", "2009")]);
}
