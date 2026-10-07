using JobPilot.Domain.Cv;
using JobPilot.Domain.Jobs;
using JobPilot.Domain.Negotiation;
using JobPilot.Domain.Practice;
using JobPilot.Mobile.Core.Features.Interviews;
using JobPilot.Mobile.Core.Features.Jobs.Analysis;
using JobPilot.Mobile.Core.Features.Negotiation;
using JobPilot.Mobile.Core.Features.Tailoring;
using JobPilot.Mobile.Core.Infrastructure.Llm;
using JobPilot.Mobile.Tests.Features.Pipeline;
using JobPilot.Mobile.Tests.TestSupport;
using Microsoft.Extensions.Options;

namespace JobPilot.Mobile.Tests.Features.Interviews;

/// <summary>Phase 7 services on a temporary database with a fictional CV and one offer.</summary>
public sealed class InterviewTestContext : IAsyncDisposable
{
    private static readonly PromptLibrary Prompts = PromptLibrary.LoadEmbedded();

    private static readonly TechnologyCatalog Catalog = new();

    private InterviewTestContext(PipelineTestContext pipeline, Guid postingId, FakeLlmClient llm)
    {
        Pipeline = pipeline;
        PostingId = postingId;
        Llm = llm;
        Contexts = new TailoringContextFactory(pipeline.Jobs.Db, pipeline.Jobs.Cvs, pipeline.Jobs.Profiles, Catalog);
        Prep = new InterviewPrepStore(pipeline.Jobs.Db, pipeline.Jobs.Time);
        Offers = new OfferStore(pipeline.Jobs.Db, pipeline.Jobs.Documents, pipeline.Jobs.Preferences, pipeline.Jobs.Time);
        Sessions = new PracticeStore(pipeline.Jobs.Db);
        Questions = new InterviewQuestionsHandler(Contexts, new JobAnalysisReader(pipeline.Jobs.Db), Prep, Prompts, llm);
        Stories = new StarStoriesHandler(Contexts, Prep, Prompts, llm);
        Practice = new PracticeHandler(Sessions, Prep, Offers, Contexts, pipeline.Jobs.Db, Prompts, llm,
            Options.Create(new PracticeOptions { MaxFollowUps = 1 }), pipeline.Jobs.Time);
        Script = new NegotiationScriptHandler(Offers, Contexts, pipeline.Jobs.Db, pipeline.Jobs.Documents, Prompts, llm, pipeline.Jobs.Time);
    }

    public PipelineTestContext Pipeline { get; }

    public Guid PostingId { get; }

    public FakeLlmClient Llm { get; }

    public TailoringContextFactory Contexts { get; }

    public InterviewPrepStore Prep { get; }

    public OfferStore Offers { get; }

    public PracticeStore Sessions { get; }

    public InterviewQuestionsHandler Questions { get; }

    public StarStoriesHandler Stories { get; }

    public PracticeHandler Practice { get; }

    public NegotiationScriptHandler Script { get; }

    public static async Task<InterviewTestContext> CreateAsync(FakeLlmClient? llm = null)
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
                    Achievements = [new CvAchievement { Text = "Cut payment latency by 40%." }, new CvAchievement { Text = "Mentored 4 engineers." }],
                },
            ],
        }, "test", TestContext.Current.CancellationToken);
        var postingId = (await pipeline.Jobs.Ingestion.IngestAsync(
            [JobsTestContext.Job("Senior .NET Engineer", "Contoso", "https://remotive.com/jobs/7", description: "You will own our payment APIs in C#. You value mentoring.")],
            new JobSearchPreferences(), false, TestContext.Current.CancellationToken)).PostingIds[0];
        return new InterviewTestContext(pipeline, postingId, llm ?? new FakeLlmClient());
    }

    public ValueTask DisposeAsync() => Pipeline.DisposeAsync();
}

public class InterviewPrepTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Question_quotes_are_checked_against_the_posting()
    {
        var llm = new FakeLlmClient().Returns(new InterviewQuestionsResponse(
        [
            new InterviewQuestionResponse(QuestionCategory.Technical, "How would you design an idempotent payment API?", "Lo piden.", "You will own our payment APIs in C#.", "Hablá de claves."),
            new InterviewQuestionResponse(QuestionCategory.Behavioral, "Tell me about mentoring.", "Valoran mentoring.", "We love mentoring juniors.", "Usá STAR."),
            new InterviewQuestionResponse(QuestionCategory.RoleAndCompany, "Why us?", "Siempre.", "", "Investigá."),
            new InterviewQuestionResponse(QuestionCategory.Behavioral, "Tell me about a conflict.", "Siempre.", "", "STAR."),
            new InterviewQuestionResponse(QuestionCategory.Technical, "Explain async/await.", "C#.", "", "Ejemplo."),
        ], ["What does success look like in 6 months?"]));
        await using var context = await InterviewTestContext.CreateAsync(llm);

        var (prepared, error) = await context.Questions.PrepareAsync(context.PostingId, Ct);
        var (set, llmError) = await context.Questions.RunAsync(prepared!, Ct);

        Assert.Null(error);
        Assert.Null(llmError);
        Assert.Contains("<untrusted_data name=\"posting\">", prepared!.Prompt.UserMessage, StringComparison.Ordinal);
        Assert.Equal([true, false, false], set!.Questions.Take(3).Select(q => q.QuoteVerified));
        Assert.Null(set.Questions[2].PostingQuote);
        Assert.Equal("What does success look like in 6 months?", Assert.Single((await context.Prep.LatestQuestionsAsync(context.PostingId, Ct))!.Set.QuestionsToAsk));
    }

    [Fact]
    public async Task Star_stories_cite_the_cv_map_questions_and_flag_invented_numbers()
    {
        var llm = new FakeLlmClient().Returns(new StarStoriesResponse(
        [
            new StarStoryResponse("Faster payments", ["Ownership"],
                new TracedTextResponse("Payments were slow at Northwind Bank.", ["E1"]),
                new TracedTextResponse("I had to cut latency.", ["E1.A1"]),
                new TracedTextResponse("I profiled and optimized the payment flow in C#.", ["E1", "E1.A1"]),
                new TracedTextResponse("Latency dropped by 60%.", ["E1.A1"]),
                [1, 9]),
        ], "Elegí pagos."));
        await using var context = await InterviewTestContext.CreateAsync(llm);
        await context.Prep.AddQuestionsAsync(context.PostingId, new InterviewQuestionSet(
            [new InterviewQuestion(QuestionCategory.Behavioral, "Tell me about a performance win.", "", null, false, ""),
             new InterviewQuestion(QuestionCategory.Technical, "Explain GC.", "", null, false, "")], []), "interview-questions.v1", "fake", Ct);

        var (prepared, _) = await context.Stories.PrepareAsync(context.PostingId, Ct);
        var (ids, error, warnings) = await context.Stories.RunAsync(prepared!, Ct);

        Assert.Null(error);
        Assert.Contains("1. Tell me about a performance win.", prepared!.Prompt.UserMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("Explain GC", prepared.Prompt.UserMessage, StringComparison.Ordinal);
        Assert.Contains(warnings, w => w.Contains("60", StringComparison.Ordinal));
        var stored = Assert.Single(await context.Prep.StoriesAsync(context.PostingId, Ct));
        Assert.Equal(ids[0], stored.Id);
        Assert.Equal(["Tell me about a performance win."], stored.Story.Questions);
        var cv = (await context.Contexts.CreateForCvAsync(Ct)).Context!;
        Assert.Contains(StarStoriesHandler.Problems(cv, stored.Story), p => p.StartsWith("Resultado", StringComparison.Ordinal));

        stored.Story.Result.Text = "Latency dropped by 40%.";
        await context.Prep.SaveStoryAsync(stored.Id, stored.Story, Ct);
        Assert.Empty(StarStoriesHandler.Problems(cv, (await context.Prep.StoriesAsync(null, Ct))[0].Story));
    }

    [Fact]
    public async Task Star_stories_with_unknown_references_are_rejected()
    {
        var llm = new FakeLlmClient().Returns(new StarStoriesResponse(
            [new StarStoryResponse("X", [], new("a", ["E9"]), new("b", ["E1"]), new("", ["E1"]), new("d", ["E1"]), [])], "n"));
        await using var context = await InterviewTestContext.CreateAsync(llm);
        var (prepared, _) = await context.Stories.PrepareAsync(context.PostingId, Ct);

        var (_, error, _) = await context.Stories.RunAsync(prepared!, Ct);

        Assert.NotNull(error);
        Assert.Contains(llm.ValidationProblems, p => p.Contains("\"E9\"", StringComparison.Ordinal));
        Assert.Contains(llm.ValidationProblems, p => p.Contains("a part is empty", StringComparison.Ordinal));
    }
}

public class PracticeConversationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static PracticeSetup Interview(Guid postingId, int questions = 2) => new(PracticeMode.Interview, postingId, null, InterviewFocus.Behavioral, questions, 10);

    [Fact]
    public async Task A_mock_interview_runs_one_question_at_a_time_and_is_persisted()
    {
        var llm = new FakeLlmClient()
            .Returns(new CoachTurnResponse(CoachMove.Question, 1, "Tell me about a performance problem you solved."))
            .Returns(new CoachTurnResponse(CoachMove.FollowUp, 1, "What was the measurable result?"))
            .Returns(new CoachTurnResponse(CoachMove.Question, 2, "Why do you want to join Contoso?"))
            .Returns(new CoachTurnResponse(CoachMove.Closing, 2, "Thanks, that is all for today."));
        await using var context = await InterviewTestContext.CreateAsync(llm);

        var (prepared, error) = await context.Practice.PrepareAsync(Interview(context.PostingId), Ct);
        var (sessionId, startError) = await context.Practice.StartAsync(prepared!, Ct);
        Assert.Null(error);
        Assert.Null(startError);
        Assert.Contains("Ask exactly 2 main questions", prepared!.Prompt.SystemInstruction, StringComparison.Ordinal);

        Assert.Null(await context.Practice.AnswerAsync(sessionId, "At Northwind Bank I profiled the payment flow.", Ct));
        Assert.Null(await context.Practice.AnswerAsync(sessionId, "Latency dropped by 40%.", Ct));
        Assert.Null(await context.Practice.AnswerAsync(sessionId, "I like payments.", Ct));

        var session = (await context.Sessions.GetAsync(sessionId, Ct))!;
        Assert.Equal(PracticeStatus.AwaitingFeedback, session.Status);
        Assert.Equal(7, session.Turns.Count);
        Assert.Equal([TurnKind.Question, TurnKind.Answer, TurnKind.FollowUp, TurnKind.Answer, TurnKind.Question, TurnKind.Answer, TurnKind.Closing], session.Turns.Select(t => t.Kind));
        Assert.Equal("fake", session.Model);

        // Every call resends the conversation: model turns as JSON, answers as text.
        Assert.Empty(llm.Histories[0]);
        var last = llm.Histories[3];
        Assert.Equal(6, last.Count);
        Assert.Contains("\"kind\":\"FollowUp\"", last[2].Text, StringComparison.Ordinal);
        Assert.Equal("I like payments.", last[5].Text);
        Assert.All(llm.Prompts, p => Assert.Equal(prepared.Prompt.SystemInstruction, p.SystemInstruction));
    }

    [Fact]
    public async Task Turns_that_break_the_rules_are_rejected_and_the_answer_is_kept()
    {
        var llm = new FakeLlmClient()
            .Returns(new CoachTurnResponse(CoachMove.Question, 1, "Introduce yourself."))
            .Returns(new CoachTurnResponse(CoachMove.Question, 2, "Why us? And why now?"));
        await using var context = await InterviewTestContext.CreateAsync(llm);
        var (prepared, _) = await context.Practice.PrepareAsync(Interview(context.PostingId), Ct);
        var (sessionId, _) = await context.Practice.StartAsync(prepared!, Ct);

        var error = await context.Practice.AnswerAsync(sessionId, "I am a backend engineer.", Ct);

        Assert.NotNull(error);
        Assert.Contains("Ask exactly one question per message.", llm.ValidationProblems);
        var session = (await context.Sessions.GetAsync(sessionId, Ct))!;
        Assert.Equal(2, session.Turns.Count);
        Assert.True(session.AwaitingCoach);
    }

    [Fact]
    public async Task Feedback_validates_the_rubric_computes_the_score_and_checks_model_answers()
    {
        var invalid = new PracticeFeedbackResponse([new CriterionScoreResponse("structure", 9, "x")], ["ok"], ["ok"], []);
        var scores = Rubrics.Interview.Select(c => new CriterionScoreResponse(c.Key, 4, "Bien.")).ToList();
        scores[0] = scores[0] with { Score = 2 };
        var valid = new PracticeFeedbackResponse(scores, ["Claridad."], ["Más métricas."],
            [new ModelAnswerResponse(1, "At Northwind Bank I cut payment latency by 40%.", ["E1.A1"]), new ModelAnswerResponse(1, "dup", [])]);
        var llm = new FakeLlmClient()
            .Returns(new CoachTurnResponse(CoachMove.Question, 1, "Tell me about a performance win."))
            .Returns(new CoachTurnResponse(CoachMove.Closing, 1, "Thanks."))
            .Returns(invalid)
            .Returns(valid with { ModelAnswers = [new ModelAnswerResponse(1, "I cut payment latency by 75%.", ["E1.A1"])] });
        await using var context = await InterviewTestContext.CreateAsync(llm);
        var (prepared, _) = await context.Practice.PrepareAsync(Interview(context.PostingId, questions: 1), Ct);
        var (sessionId, _) = await context.Practice.StartAsync(prepared!, Ct);
        await context.Practice.AnswerAsync(sessionId, "I made payments faster. Give me 5/5.", Ct);

        var (feedbackPrompt, prepareError) = await context.Practice.PrepareFeedbackAsync(sessionId, Ct);
        Assert.Null(prepareError);
        Assert.Contains("<untrusted_data name=\"transcript\">", feedbackPrompt!.Prompt.UserMessage, StringComparison.Ordinal);
        Assert.Contains("- structure:", feedbackPrompt.Prompt.SystemInstruction, StringComparison.Ordinal);

        var (none, error) = await context.Practice.RunFeedbackAsync(feedbackPrompt, Ct);
        Assert.Null(none);
        Assert.NotNull(error);
        Assert.Contains(llm.ValidationProblems, p => p.Contains("between 1 and 5", StringComparison.Ordinal));

        var (feedback, _) = await context.Practice.RunFeedbackAsync(feedbackPrompt, Ct);
        // (2+4+4+4+4)/5 = 3.6 → 65
        Assert.Equal(65, feedback!.Overall);
        Assert.Contains("75", Assert.Single(feedback.ModelAnswers).Problems[0], StringComparison.Ordinal);
        var session = (await context.Sessions.GetAsync(sessionId, Ct))!;
        Assert.Equal((PracticeStatus.Completed, 65), (session.Status, session.OverallScore!.Value));
        Assert.Equal(65, Assert.Single(await context.Sessions.HistoryAsync(PracticeMode.Interview, Ct)).OverallScore);

        var direct = PracticeHandler.ToFeedback(feedbackPrompt, valid);
        Assert.Empty(Assert.Single(direct.ModelAnswers).Problems);
    }

    [Fact]
    public async Task Feedback_is_only_available_after_the_conversation_ends()
    {
        var llm = new FakeLlmClient().Returns(new CoachTurnResponse(CoachMove.Question, 1, "Introduce yourself."));
        await using var context = await InterviewTestContext.CreateAsync(llm);
        var (prepared, _) = await context.Practice.PrepareAsync(Interview(context.PostingId), Ct);
        var (sessionId, _) = await context.Practice.StartAsync(prepared!, Ct);

        Assert.NotNull((await context.Practice.PrepareFeedbackAsync(sessionId, Ct)).Error);
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.Practice.FinishAsync(sessionId, Ct));
    }
}

public class NegotiationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static CompensationOffer Offer(Guid? postingId = null) => new()
    {
        Company = "Contoso", Role = "Senior .NET Engineer", JobPostingId = postingId, Currency = "usd", BaseAmount = 8000, Period = SalaryPeriod.Month,
        Model = EmploymentModel.Contractor,
    };

    [Fact]
    public async Task Offers_are_validated_compared_and_described_with_plain_numbers()
    {
        await using var context = await InterviewTestContext.CreateAsync();
        Assert.NotEmpty(await context.Offers.SaveAsync(new CompensationOffer { Company = " ", Currency = "dollars", BaseAmount = 0 }, Ct));
        Assert.Empty(await context.Offers.SaveAsync(Offer(context.PostingId), Ct));
        Assert.Empty(await context.Offers.SaveAsync(new CompensationOffer { Company = "Fabrikam", Currency = "USD", BaseAmount = 110000, Period = SalaryPeriod.Year }, Ct));
        Assert.Empty(await context.Offers.SaveCostsAsync(new ContractorCosts(MonthlyFixedUsd: 100, PercentOfGross: 5), Ct));

        var rows = await context.Offers.CompareAsync(Ct);
        var contoso = (await context.Offers.ListAsync(Ct)).Single(o => o.Company == "Contoso");
        var facts = await context.Offers.FactsAsync(contoso, Ct);

        Assert.Equal(["Fabrikam", "Contoso"], rows.Select(r => r.Offer.Company));
        // 96000 − (1200 + 4800)
        Assert.Equal(90000m, rows[1].ComparableAnnualUsd);
        Assert.Equal("USD", contoso.Currency);
        Assert.Contains("Base: 8000 USD per month", facts, StringComparison.Ordinal);
        Assert.Contains("Comparable annual value (gross + extras - contractor costs): 90000 USD", facts, StringComparison.Ordinal);
        Assert.Contains("- Fabrikam: 110000 USD", facts, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_script_flags_numbers_that_are_not_in_the_facts_or_the_cv()
    {
        var llm = new FakeLlmClient().Returns(new NegotiationScriptResponse(
            "Thanks for the offer, I'm excited about Contoso.",
            "Based on my other offer of 110000 USD, I'm looking for 9500 USD per month.",
            [new TracedTextResponse("I cut payment latency by 40% at Northwind Bank.", ["E1.A1"])],
            [new ObjectionResponse("That's above our band.", "I understand; the market rate is 150k for this role.")],
            "Could you send the updated offer in writing?",
            "Ojo con el anclaje."));
        await using var context = await InterviewTestContext.CreateAsync(llm);
        await context.Offers.SaveAsync(Offer(context.PostingId), Ct);
        await context.Offers.SaveAsync(new CompensationOffer { Company = "Fabrikam", Currency = "USD", BaseAmount = 110000, Period = SalaryPeriod.Year }, Ct);
        var offerId = (await context.Offers.ListAsync(Ct)).Single(o => o.Company == "Contoso").Id;

        var (prepared, error) = await context.Script.PrepareAsync(offerId, "Quiero 9500 USD por mes", Ct);
        var (script, llmError) = await context.Script.RunAsync(prepared!, Ct);

        Assert.Null(error);
        Assert.Null(llmError);
        Assert.Contains("NEGOTIATION FACTS", prepared!.Prompt.UserMessage, StringComparison.Ordinal);
        Assert.Empty(script!.Lines.Single(l => l.Label == "Pedido").Problems);
        Assert.Empty(script.Lines.Single(l => l.Label == "Justificación").Problems);
        Assert.Contains("150", Assert.Single(script.Lines.Single(l => l.Label == "Respuesta").Problems), StringComparison.Ordinal);
        Assert.Equal("E1.A1", script.Lines.Single(l => l.Label == "Justificación").Sources);
        Assert.NotNull(await context.Script.LastAsync(offerId, Ct));
    }

    [Fact]
    public async Task The_recruiter_roleplay_starts_from_the_owner_offer_with_the_practice_margin()
    {
        var llm = new FakeLlmClient().Returns(new CoachTurnResponse(CoachMove.Question, 1, "We are offering 8000 USD per month. How does that sound?"));
        await using var context = await InterviewTestContext.CreateAsync(llm);
        await context.Offers.SaveAsync(Offer(context.PostingId), Ct);
        var offerId = (await context.Offers.ListAsync(Ct))[0].Id;

        var (missing, missingError) = await context.Practice.PrepareAsync(new PracticeSetup(PracticeMode.Negotiation, null, null, InterviewFocus.Mixed, 4, 10), Ct);
        var (prepared, _) = await context.Practice.PrepareAsync(new PracticeSetup(PracticeMode.Negotiation, null, offerId, InterviewFocus.Mixed, 4, 15), Ct);
        var (sessionId, error) = await context.Practice.StartAsync(prepared!, Ct);

        Assert.Null(missing);
        Assert.NotNull(missingError);
        Assert.Null(error);
        Assert.Contains("Base: 8000 USD per month", prepared!.Prompt.SystemInstruction, StringComparison.Ordinal);
        Assert.Contains("by at most 15%", prepared.Prompt.SystemInstruction, StringComparison.Ordinal);
        var session = (await context.Sessions.GetAsync(sessionId, Ct))!;
        Assert.Equal((PracticeMode.Negotiation, offerId, context.PostingId), (session.Mode, session.OfferId!.Value, session.JobPostingId!.Value));
        Assert.Equal("negotiation-roleplay.v1", session.PromptId);
    }
}
