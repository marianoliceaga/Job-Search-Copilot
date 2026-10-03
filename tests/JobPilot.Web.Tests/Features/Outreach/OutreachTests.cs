using JobPilot.Domain.Companies;
using JobPilot.Domain.Cv;
using JobPilot.Domain.Pipeline;
using JobPilot.Web.Features.Companies;
using JobPilot.Web.Features.Outreach;
using JobPilot.Web.Features.Tailoring;
using JobPilot.Web.Infrastructure.Llm;
using JobPilot.Web.Infrastructure.Persistence;
using JobPilot.Web.Tests.Features.Companies;
using JobPilot.Web.Tests.Features.Pipeline;
using JobPilot.Web.Tests.TestSupport;
using Microsoft.Extensions.Options;

namespace JobPilot.Web.Tests.Features.Outreach;

public class OutreachHandlerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly PromptLibrary Prompts = PromptLibrary.LoadFrom(Path.Combine(AppContext.BaseDirectory, "Prompts"));

    private static readonly TechnologyCatalog Catalog = new(Path.Combine(AppContext.BaseDirectory, "Seed", "technology-terms.txt"));

    internal sealed record Setup(PipelineTestContext Pipeline, CompanyStore Companies, Guid PostingId, Guid CompanyId, TailoringContextFactory Contexts) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Pipeline.DisposeAsync();
    }

    internal static async Task<Setup> CreateAsync()
    {
        var pipeline = await PipelineTestContext.CreateAsync();
        await pipeline.Jobs.Cvs.SaveAsync(new MasterCv
        {
            Contact = new CvContact { FullName = "Alex Rivera" },
            Headline = "Senior Software Engineer",
            Summary = "Backend engineer focused on payments.",
            Experiences =
            [
                new CvExperience
                {
                    Company = "Northwind Bank", Title = "Tech Lead", Start = new PartialDate(2019, 3), IsCurrent = true, Technologies = ["C#", "Azure"],
                    Achievements = [new CvAchievement { Text = "Cut payment latency by 40%." }],
                },
            ],
        }, "test", Ct);
        var postingId = await pipeline.AddPostingAsync("Senior .NET Engineer", "Contoso", "https://remotive.com/jobs/5");
        var companies = CompanyStoreTests.Store(pipeline);
        var companyId = await companies.CreateAsync("Contoso", null, Ct);
        await companies.AddTextAsync(companyId, "Contoso runs its platform on Kubernetes and serves 300 retailers.", null, null, Ct);
        var source = (await companies.GetAsync(companyId, Ct))!.Sources[0];
        await companies.SaveResearchAsync(companyId,
        [
            new CompanyFact { Category = CompanyFactCategory.Technology, Statement = "Usa Kubernetes.", Quote = "runs its platform on Kubernetes", SourceId = source.Id, Verification = FactVerification.QuoteFound },
            new CompanyFact { Category = CompanyFactCategory.Funding, Statement = "Levantó USD 50M.", Quote = "raised 50M", SourceId = source.Id, Verification = FactVerification.Unverified },
        ], [], "company-research.v1", "fake", Ct);
        var contexts = new TailoringContextFactory(pipeline.Jobs.Db, pipeline.Jobs.Cvs, pipeline.Jobs.Profiles, Catalog);
        return new Setup(pipeline, companies, postingId, companyId, contexts);
    }

    private static OutreachHandler Handler(Setup setup, FakeLlmClient llm, int connectionMax = 200) =>
        new(setup.Contexts, setup.Companies, setup.Pipeline.Jobs.Db, Prompts, llm,
            Options.Create(new OutreachOptions { ConnectionNoteMaxLength = connectionMax }), setup.Pipeline.Jobs.Time);

    private static OutreachResponse Response(string formalSecond = "I cut payment latency by 40% and would love to help a platform that runs on Kubernetes.", string subject = "") => new(
    [
        new OutreachVariantResponse(OutreachTone.Direct, subject, [new OutreachParagraphResponse("Are you the right person to talk about the Senior .NET Engineer role?", [])]),
        new OutreachVariantResponse(OutreachTone.Formal, subject,
        [
            new OutreachParagraphResponse("I am reaching out about the Senior .NET Engineer role at Contoso.", []),
            new OutreachParagraphResponse(formalSecond, ["E1.A1", "F1"]),
        ]),
        new OutreachVariantResponse(OutreachTone.Friendly, subject, [new OutreachParagraphResponse("I'd be glad to chat about how I could help.", [])]),
    ], "Tres tonos.");

    private static OutreachInput Input(Setup setup, OutreachChannel channel = OutreachChannel.LinkedInMessage) =>
        new(setup.PostingId, null, channel, OutreachLength.Short, "Jordan Lee", "Recruiter", null);

    [Fact]
    public async Task Only_verified_company_facts_reach_the_prompt()
    {
        await using var setup = await CreateAsync();
        var handler = Handler(setup, new FakeLlmClient());

        var (prepared, error) = await handler.PrepareAsync(Input(setup), Ct);

        Assert.Null(error);
        Assert.Equal(setup.CompanyId, prepared!.Company!.Id);
        var user = prepared.Prompt.UserMessage;
        Assert.Contains("F1: Usa Kubernetes.", user, StringComparison.Ordinal);
        Assert.DoesNotContain("50M", user, StringComparison.Ordinal);
        Assert.Contains("<untrusted_data name=\"facts\">", user, StringComparison.Ordinal);
        Assert.Contains("Recipient: Jordan Lee, Recruiter", user, StringComparison.Ordinal);
        Assert.Contains("E1.A1: Cut payment latency by 40%.", user, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Three_variants_are_composed_ordered_and_stored()
    {
        var llm = new FakeLlmClient().Returns(Response());
        await using var setup = await CreateAsync();
        var handler = Handler(setup, llm);
        var (prepared, _) = await handler.PrepareAsync(Input(setup), Ct);

        var result = await handler.RunAsync(prepared!, Ct);

        Assert.Null(result.Error);
        var variants = result.Drafts!.Variants;
        Assert.Equal([OutreachTone.Formal, OutreachTone.Friendly, OutreachTone.Direct], variants.Select(v => v.Tone));
        Assert.StartsWith("Hi Jordan,\n\nI am reaching out", variants[0].Body, StringComparison.Ordinal);
        Assert.EndsWith("Best,\nAlex Rivera", variants[0].Body, StringComparison.Ordinal);
        // Kubernetes is allowed because the paragraph cites the verified fact F1.
        Assert.All(variants, v => Assert.Empty(v.Warnings));
        // A subject on a LinkedIn message is ignored, including what it would claim.
        var withSubject = Response().Variants[0] with { Subject = "Senior .NET Engineer and Kubernetes expert" };
        Assert.Empty(OutreachHandler.Warnings(prepared!, withSubject));
        Assert.Null(variants[0].Subject);

        var history = await new OutreachStore(setup.Pipeline.Jobs.Db, setup.Pipeline.Store, setup.Pipeline.Jobs.Time).RecentAsync(10, Ct);
        var item = Assert.Single(history);
        Assert.Equal(("Contoso", "Senior .NET Engineer — Contoso"), (item.Company, item.Offer));
        Assert.Equal(3, item.Drafts.Variants.Count);
    }

    [Fact]
    public async Task Invented_numbers_and_uncited_technologies_are_warnings()
    {
        var llm = new FakeLlmClient().Returns(Response("I cut payment latency by 70% and I know Kubernetes well.") with { });
        await using var setup = await CreateAsync();
        var handler = Handler(setup, llm);
        var invented = Response() with
        {
            Variants = [.. Response().Variants.Select(v => v.Tone == OutreachTone.Formal
                ? v with { Paragraphs = [new OutreachParagraphResponse("I cut payment latency by 70% and I know Kubernetes well.", ["E1.A1"])] }
                : v)],
        };
        var (prepared, _) = await handler.PrepareAsync(Input(setup), Ct);

        var warnings = OutreachHandler.Warnings(prepared!, invented.Variants.Single(v => v.Tone == OutreachTone.Formal));

        Assert.Contains(warnings, w => w.Contains("70", StringComparison.Ordinal));
        Assert.Contains(warnings, w => w.Contains("Kubernetes", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Connection_notes_must_fit_the_limit_and_placeholders_and_unknown_references_are_rejected()
    {
        await using var setup = await CreateAsync();
        var handler = Handler(setup, new FakeLlmClient(), connectionMax: 60);
        var (prepared, _) = await handler.PrepareAsync(Input(setup, OutreachChannel.LinkedInConnection), Ct);
        var response = Response() with
        {
            Variants = [.. Response().Variants.Select(v => v.Tone == OutreachTone.Direct
                ? v with { Paragraphs = [new OutreachParagraphResponse("Hi [Name], quick question.", ["F9"])] }
                : v)],
        };

        var problems = handler.Validate(prepared!, response);

        Assert.Contains(problems, p => p.Contains("must fit in 60 characters", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("[Name]", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("\"F9\"", StringComparison.Ordinal));
        Assert.StartsWith("Hi Jordan, Are you", OutreachHandler.Compose(prepared!, ["Are you the right person?"]), StringComparison.Ordinal);
    }

    [Fact]
    public async Task E_mails_need_a_subject_and_company_only_outreach_works()
    {
        var llm = new FakeLlmClient().Returns(Response(subject: "Senior .NET Engineer at Contoso"));
        await using var setup = await CreateAsync();
        var handler = Handler(setup, llm);

        var (prepared, _) = await handler.PrepareAsync(new OutreachInput(null, setup.CompanyId, OutreachChannel.Email, OutreachLength.Medium, null, "Hiring manager", null), Ct);
        Assert.Contains(handler.Validate(prepared!, Response()), p => p.Contains("needs a subject", StringComparison.Ordinal));
        var result = await handler.RunAsync(prepared!, Ct);

        Assert.Contains("(no specific offer)", prepared!.Prompt.UserMessage, StringComparison.Ordinal);
        Assert.Equal("Senior .NET Engineer at Contoso", result.Drafts!.Variants[0].Subject);
        Assert.StartsWith("Hello,", result.Drafts.Variants[0].Body, StringComparison.Ordinal);
        Assert.EndsWith("Best regards,\nAlex Rivera", result.Drafts.Variants[0].Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Marking_as_sent_records_the_text_and_adds_it_to_the_pipeline_timeline()
    {
        var llm = new FakeLlmClient().Returns(Response());
        await using var setup = await CreateAsync();
        var applicationId = await setup.Pipeline.Store.StartAsync(setup.PostingId, ApplicationStage.Applied, Ct);
        var handler = Handler(setup, llm);
        var (prepared, _) = await handler.PrepareAsync(Input(setup), Ct);
        var result = await handler.RunAsync(prepared!, Ct);
        var store = new OutreachStore(setup.Pipeline.Jobs.Db, setup.Pipeline.Store, setup.Pipeline.Jobs.Time);

        await store.MarkSentAsync(result.RecordId!.Value, "Hi Jordan, edited text.", Ct);

        var item = Assert.Single(await store.RecentAsync(10, Ct));
        Assert.Equal("Hi Jordan, edited text.", item.Record.SentText);
        var sent = Assert.Single((await setup.Pipeline.Store.GetAsync(applicationId, Ct))!.Application.Events, e => e.Kind == ApplicationEventKind.MessageSent);
        Assert.Equal("Mensaje de LinkedIn\n\nHi Jordan, edited text.", sent.Text);
    }
}

public class LinkedInReviewHandlerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly PromptLibrary Prompts = PromptLibrary.LoadFrom(Path.Combine(AppContext.BaseDirectory, "Prompts"));

    private static LinkedInReviewHandler Handler(OutreachHandlerTests.Setup setup, FakeLlmClient llm) =>
        new(setup.Contexts, setup.Pipeline.Jobs.Documents, Prompts, llm, Options.Create(new OutreachOptions { HeadlineMaxLength = 60, AboutMaxLength = 300 }), setup.Pipeline.Jobs.Time);

    private static LinkedInReviewResponse Response(string about = "I lead payments engineering and cut payment latency by 40%.") => new(
        "Tu titular es genérico.",
        ["Mencioná pagos."],
        [new TracedTextResponse("Tech Lead · Payments · C# and Azure", ["E1", "HEAD"])],
        [new TracedTextResponse(about, ["E1.A1", "SUM"])]);

    [Fact]
    public async Task The_review_uses_the_pasted_profile_and_is_stored_with_traceability_results()
    {
        var llm = new FakeLlmClient().Returns(Response("I lead payments engineering and cut payment latency by 65%."));
        await using var setup = await OutreachHandlerTests.CreateAsync();
        var handler = Handler(setup, llm);

        var (prepared, error) = await handler.PrepareAsync(new LinkedInReviewInput("Software Engineer", "I write code.", "Senior .NET roles"), Ct);
        var (review, llmError) = await handler.RunAsync(prepared!, Ct);

        Assert.Null(error);
        Assert.Null(llmError);
        Assert.Contains("CURRENT HEADLINE\nSoftware Engineer", prepared!.Prompt.UserMessage.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.Contains("at most 60 characters", prepared.Prompt.SystemInstruction, StringComparison.Ordinal);
        Assert.Empty(review!.Headlines[0].Problems);
        Assert.Equal("E1, HEAD", review.Headlines[0].Sources);
        Assert.Contains("65", Assert.Single(review.About[0].Problems), StringComparison.Ordinal);
        Assert.Equal("Tu titular es genérico.", (await handler.LastAsync(Ct))!.Assessment);
    }

    [Fact]
    public async Task Limits_placeholders_and_references_are_enforced()
    {
        await using var setup = await OutreachHandlerTests.CreateAsync();
        var handler = Handler(setup, new FakeLlmClient());
        var response = Response(new string('a', 301)) with
        {
            Headlines = [new TracedTextResponse("Senior Engineer at [Company] with a very long headline text that goes on", ["E7"])],
        };
        var (prepared, _) = await handler.PrepareAsync(new LinkedInReviewInput(null, null, null), Ct);

        var problems = handler.Validate(prepared!.Context, response);

        Assert.Contains(problems, p => p.Contains("longer than 60", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("at most 300 characters", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("[Company]", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("\"E7\"", StringComparison.Ordinal));
    }
}
