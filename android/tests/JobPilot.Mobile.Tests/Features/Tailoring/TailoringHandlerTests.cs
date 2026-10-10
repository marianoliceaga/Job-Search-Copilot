using JobPilot.Domain.Cv;
using JobPilot.Domain.Jobs;
using JobPilot.Domain.Llm;
using JobPilot.Domain.Tailoring;
using JobPilot.Mobile.Core.Features.Jobs.Analysis;
using JobPilot.Mobile.Core.Features.Tailoring;
using JobPilot.Mobile.Core.Infrastructure.Llm;
using JobPilot.Mobile.Core.Infrastructure.Persistence;
using JobPilot.Mobile.Tests.TestSupport;

namespace JobPilot.Mobile.Tests.Features.Tailoring;

public class TailoringHandlerTests
{
    private const string PostingDescription = "We need C# and Azure engineers. Our platform serves 300 stores.";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly PromptLibrary Prompts = PromptLibrary.LoadEmbedded();

    private static readonly TechnologyCatalog Catalog = new();

    private sealed record Setup(JobsTestContext Jobs, Guid PostingId, TailoringHandler Handler, TailoredDocumentStore Store, TailoringContextFactory Contexts, FakeLlmClient Llm) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Jobs.DisposeAsync();
    }

    private static async Task<Setup> CreateAsync(FakeLlmClient llm)
    {
        var jobs = await JobsTestContext.CreateAsync();
        var cv = new MasterCv
        {
            Contact = new CvContact { FullName = "Alex Rivera" },
            Headline = "Senior Software Engineer",
            Summary = "Backend engineer with 15 years of experience.",
            Experiences =
            [
                new CvExperience
                {
                    Company = "Northwind Bank", Title = "Tech Lead", Start = new PartialDate(2019, 3), IsCurrent = true, Technologies = ["C#", ".NET", "Azure"],
                    Achievements = [new CvAchievement { Text = "Cut payment latency by 40%." }, new CvAchievement { Text = "Migrated the ledger to Azure." }],
                },
                new CvExperience
                {
                    Company = "Contoso Retail", Title = "Senior Software Engineer", Start = new PartialDate(2014, 6), End = new PartialDate(2019, 2),
                    Achievements = [new CvAchievement { Text = "Built the order API used by 120 stores." }],
                },
            ],
            Skills = [new CvSkill { Name = "Blazor" }],
        };
        await jobs.Cvs.SaveAsync(cv, "test", Ct);
        await jobs.SaveProfileAsync();
        var postingId = (await jobs.Ingestion.IngestAsync([JobsTestContext.Job(description: PostingDescription)], new JobSearchPreferences(), false, Ct)).PostingIds[0];
        var contexts = new TailoringContextFactory(jobs.Db, jobs.Cvs, jobs.Profiles, Catalog);
        var store = new TailoredDocumentStore(jobs.Db, jobs.Time);
        var handler = new TailoringHandler(contexts, store, new JobAnalysisReader(jobs.Db), Prompts, llm);
        return new Setup(jobs, postingId, handler, store, contexts, llm);
    }

    private static TailoredCvResponse CvResponse(string firstBullet = "Reduced payment latency by 40% on Azure.") => new(
        new TracedTextResponse("Senior .NET Engineer", ["HEAD"]),
        new TracedTextResponse("Backend engineer with 15 years of experience in C# and Azure.", ["SUM", "E1"]),
        [
            new TailoredExperienceResponse("E2", [new TracedTextResponse("Built an order API used by 120 stores.", ["E2.A1"])]),
            new TailoredExperienceResponse("E1", [new TracedTextResponse(firstBullet, ["E1.A1", "E1"]), new TracedTextResponse("Migrated the ledger to Azure.", ["E1.A2"])]),
        ],
        ["Azure", "C#", "Blazor"],
        "Prioricé pagos y Azure.");

    [Fact]
    public async Task The_prompt_contains_the_cv_with_references_and_the_wrapped_posting()
    {
        await using var setup = await CreateAsync(new FakeLlmClient());

        var (prepared, error) = await setup.Handler.PrepareAsync(setup.PostingId, TailoredKind.Cv, Ct);

        Assert.Null(error);
        var user = prepared!.Prompt.UserMessage;
        Assert.Contains("E1: Tech Lead — Northwind Bank (2019-03 – present)", user, StringComparison.Ordinal);
        Assert.Contains("E1.A1: Cut payment latency by 40%.", user, StringComparison.Ordinal);
        Assert.Contains("SKILLS (names you may use): Blazor, C#, .NET, Azure", user, StringComparison.Ordinal);
        Assert.Contains("<untrusted_data name=\"posting\">", user, StringComparison.Ordinal);
        Assert.Contains("- Recent hands-on experience with Blazor", user, StringComparison.Ordinal);
        Assert.Equal("cv-tailoring.v1", prepared.Prompt.PromptId);
    }

    [Fact]
    public async Task A_faithful_cv_becomes_a_draft_with_mapped_sources_in_master_order()
    {
        await using var setup = await CreateAsync(new FakeLlmClient().Returns(CvResponse()));
        var (prepared, _) = await setup.Handler.PrepareAsync(setup.PostingId, TailoredKind.Cv, Ct);

        var result = await setup.Handler.RunAsync(prepared!, Ct);

        Assert.Empty(result.Warnings);
        var stored = await setup.Store.GetAsync(result.DocumentId!.Value, Ct);
        Assert.Equal(TailoredStatus.Draft, stored!.Record.Status);
        Assert.Equal("Prioricé pagos y Azure.", stored.Record.Notes);
        var cv = stored.Cv!;
        Assert.Equal(prepared!.Context.Cv.Experiences.Select(e => e.Id), cv.Experiences.Select(e => e.ExperienceId));
        Assert.Equal([prepared.Context.Cv.Experiences[0].Achievements[0].Id.ToString(), prepared.Context.Cv.Experiences[0].Id.ToString()], cv.Experiences[0].Bullets[0].Sources);
        Assert.True(TailoredDocumentStore.Check(prepared.Context, cv).CanApprove);
    }

    [Fact]
    public async Task An_invented_metric_is_returned_flagged_and_blocks_approval()
    {
        await using var setup = await CreateAsync(new FakeLlmClient().Returns(CvResponse("Reduced payment latency by 65% on Azure.")));
        var (prepared, _) = await setup.Handler.PrepareAsync(setup.PostingId, TailoredKind.Cv, Ct);

        var result = await setup.Handler.RunAsync(prepared!, Ct);

        Assert.Contains(result.Warnings, w => w.Contains("65", StringComparison.Ordinal));
        var cv = (await setup.Store.GetAsync(result.DocumentId!.Value, Ct))!.Cv!;
        var check = TailoredDocumentStore.Check(prepared!.Context, cv);
        Assert.False(check.CanApprove);
        var flagged = Assert.Single(check.Blocking);
        Assert.Contains("65", flagged.Problems[0], StringComparison.Ordinal);

        flagged.Text.OwnerConfirmed = true;
        Assert.True(TailoredDocumentStore.Check(prepared.Context, cv).CanApprove);
    }

    [Fact]
    public async Task Unknown_references_are_structural_errors()
    {
        await using var setup = await CreateAsync(new FakeLlmClient());
        var (prepared, _) = await setup.Handler.PrepareAsync(setup.PostingId, TailoredKind.Cv, Ct);
        var response = CvResponse() with
        {
            Experiences = [new TailoredExperienceResponse("E7", [new TracedTextResponse("Did things.", ["E7.A1"])]), new TailoredExperienceResponse("E1", [])],
        };

        var problems = TailoringHandler.ValidateCv(prepared!.Context, response);

        Assert.Contains(problems, p => p.Contains("\"E7\" is not an experience reference", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("E1 has no bullets", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("cites \"E7.A1\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Skills_not_in_the_cv_block_approval()
    {
        await using var setup = await CreateAsync(new FakeLlmClient().Returns(CvResponse() with { Skills = ["Azure", "Kubernetes"] }));
        var (prepared, _) = await setup.Handler.PrepareAsync(setup.PostingId, TailoredKind.Cv, Ct);

        var result = await setup.Handler.RunAsync(prepared!, Ct);

        var check = TailoredDocumentStore.Check(prepared!.Context, (await setup.Store.GetAsync(result.DocumentId!.Value, Ct))!.Cv!);
        Assert.Equal(["Kubernetes"], check.UnknownSkills);
        Assert.False(check.CanApprove);
    }

    [Fact]
    public async Task Letters_may_use_numbers_from_the_posting_but_not_invented_ones()
    {
        var letter = new CoverLetterResponse(
        [
            new TracedTextResponse("Your 300 stores could use the kind of order API I built for 120 stores.", ["E2.A1"]),
            new TracedTextResponse("I cut payment latency by 70%.", ["E1.A1"]),
        ], "Notas.");
        await using var setup = await CreateAsync(new FakeLlmClient().Returns(letter));
        var (prepared, _) = await setup.Handler.PrepareAsync(setup.PostingId, TailoredKind.CoverLetter, Ct);

        var result = await setup.Handler.RunAsync(prepared!, Ct);

        var stored = (await setup.Store.GetAsync(result.DocumentId!.Value, Ct))!;
        Assert.Equal(TailoredKind.CoverLetter, stored.Record.Kind);
        var flagged = Assert.Single(TailoredDocumentStore.Check(prepared!.Context, stored.Letter!).Blocking);
        Assert.Contains("70", flagged.Problems[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Approved_documents_cannot_be_edited_until_reopened()
    {
        await using var setup = await CreateAsync(new FakeLlmClient().Returns(CvResponse()));
        var (prepared, _) = await setup.Handler.PrepareAsync(setup.PostingId, TailoredKind.Cv, Ct);
        var id = (await setup.Handler.RunAsync(prepared!, Ct)).DocumentId!.Value;
        var cv = (await setup.Store.GetAsync(id, Ct))!.Cv!;

        await setup.Store.SetStatusAsync(id, TailoredStatus.Approved, Ct);
        await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Store.SaveAsync(id, cv, Ct));
        await setup.Store.SetStatusAsync(id, TailoredStatus.Draft, Ct);
        cv.Headline.Text = "Edited";
        await setup.Store.SaveAsync(id, cv, Ct);

        Assert.Equal("Edited", (await setup.Store.GetAsync(id, Ct))!.Cv!.Headline.Text);
        Assert.Single(await setup.Store.ListAsync(setup.PostingId, Ct));
    }

    [Fact]
    public async Task Documents_are_validated_against_the_master_cv_version_they_came_from()
    {
        await using var setup = await CreateAsync(new FakeLlmClient().Returns(CvResponse()));
        var (prepared, _) = await setup.Handler.PrepareAsync(setup.PostingId, TailoredKind.Cv, Ct);
        var id = (await setup.Handler.RunAsync(prepared!, Ct)).DocumentId!.Value;

        var current = (await setup.Jobs.Cvs.GetAsync(Ct))!.Cv;
        current.Experiences.RemoveAt(1);
        await setup.Jobs.Cvs.SaveAsync(current, "edit", Ct);
        var stored = (await setup.Store.GetAsync(id, Ct))!;
        var (context, _) = await setup.Contexts.CreateAsync(setup.PostingId, stored.Record.MasterCvVersion, Ct);

        Assert.Equal(2, context!.Cv.Experiences.Count);
        Assert.True(TailoredDocumentStore.Check(context, stored.Cv!).CanApprove);
    }

    [Fact]
    public async Task Without_a_master_cv_nothing_is_generated()
    {
        await using var jobs = await JobsTestContext.CreateAsync();
        var postingId = (await jobs.Ingestion.IngestAsync([JobsTestContext.Job()], new JobSearchPreferences(), false, Ct)).PostingIds[0];
        var handler = new TailoringHandler(new TailoringContextFactory(jobs.Db, jobs.Cvs, jobs.Profiles, Catalog),
            new TailoredDocumentStore(jobs.Db, jobs.Time), new JobAnalysisReader(jobs.Db), Prompts, new FakeLlmClient());

        var (prepared, error) = await handler.PrepareAsync(postingId, TailoredKind.Cv, Ct);

        Assert.Null(prepared);
        Assert.Contains("CV maestro", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_tailoring_schema_round_trips_through_the_real_gemini_client()
    {
        await using var host = await LlmTestHost.CreateAsync();
        host.Handler.RespondJson(CvResponse());
        var prompt = Prompts.Get(LlmUseCases.CvTailoring).Render(new Dictionary<string, string> { ["cv"] = "c", ["emphases"] = "e", ["analysis"] = "a", ["posting"] = "p" });

        var result = await host.Client.GenerateAsync(new LlmRequest<TailoredCvResponse>(prompt), Ct);

        Assert.True(result.IsSuccess, result.Error?.ToString());
        Assert.Equal("E2", result.Value!.Experiences[0].Experience);
    }
}
