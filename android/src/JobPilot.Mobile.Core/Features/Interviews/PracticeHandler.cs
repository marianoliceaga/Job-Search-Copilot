using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using JobPilot.Domain.Practice;
using JobPilot.Domain.Prompts;
using JobPilot.Domain.Tailoring;
using JobPilot.Mobile.Core.Features.Jobs.Analysis;
using JobPilot.Mobile.Core.Features.Negotiation;
using JobPilot.Mobile.Core.Features.Tailoring;
using JobPilot.Mobile.Core.Infrastructure.Llm;
using JobPilot.Mobile.Core.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace JobPilot.Mobile.Core.Features.Interviews;

public sealed class PracticeOptions
{
    public const string SectionName = "Practice";

    public int MaxFollowUps { get; set; } = 1;

    public int DefaultQuestions { get; set; } = 5;

    public int DefaultNegotiationRounds { get; set; } = 5;

    public int DefaultFlexPercent { get; set; } = 10;
}

public enum CoachMove { Question, FollowUp, Closing }

public sealed record CoachTurnResponse(
    CoachMove Kind,
    [property: Description("Number of the main question (or negotiation round) this message belongs to.")] int QuestionNumber,
    [property: Description("What you say, in English. Exactly one question unless closing.")] string Message);

public sealed record CriterionScoreResponse(
    [property: Description("Criterion key from the rubric.")] string Criterion,
    [property: Description("Integer 1 to 5.")] int Score,
    [property: Description("In Spanish, one or two sentences.")] string Justification);

public sealed record ModelAnswerResponse(
    int QuestionNumber,
    [property: Description("Model answer in English.")] string Text,
    [property: Description("CV references it relies on (E1, E1.A2, SUM...).")] List<string> Sources);

public sealed record PracticeFeedbackResponse(
    [property: Description("One score per rubric criterion.")] List<CriterionScoreResponse> Scores,
    [property: Description("In Spanish.")] List<string> Strengths,
    [property: Description("In Spanish.")] List<string> Improvements,
    [property: Description("One model answer per question or round.")] List<ModelAnswerResponse> ModelAnswers);

/// <param name="Questions">Main questions (interview) or rounds (negotiation).</param>
/// <param name="FlexPercent">Negotiation only: how much the simulated recruiter may improve the base (practice parameter).</param>
public sealed record PracticeSetup(PracticeMode Mode, Guid? PostingId, long? OfferId, InterviewFocus Focus, int Questions, int FlexPercent);

public sealed record PreparedPractice(PracticeSetup Setup, RenderedPrompt Prompt);

public sealed record PreparedFeedback(PracticeSession Session, CvContext Context, string AllowedText, RenderedPrompt Prompt);

public sealed record PracticeHistoryItem(Guid Id, PracticeMode Mode, DateTime StartedAtUtc, PracticeStatus Status, int? OverallScore, string Title);

/// <summary>Practice sessions (mock interviews and negotiation roleplays).</summary>
public sealed class PracticeStore(IDbContextFactory<MobileDbContext> dbFactory)
{
    public async Task<PracticeSession?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.PracticeSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
    }

    public async Task SaveAsync(PracticeSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (await db.PracticeSessions.AnyAsync(s => s.Id == session.Id, cancellationToken))
        {
            db.PracticeSessions.Update(session);
        }
        else
        {
            db.PracticeSessions.Add(session);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await db.PracticeSessions.Where(s => s.Id == id).ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>Sessions of one mode, oldest first (the score chart reads them in order).</summary>
    public async Task<IReadOnlyList<PracticeHistoryItem>> HistoryAsync(PracticeMode mode, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var sessions = await db.PracticeSessions.AsNoTracking().Where(s => s.Mode == mode)
            .Select(s => new { s.Id, s.Mode, s.StartedAtUtc, s.Status, s.OverallScore, s.JobPostingId, s.OfferId })
            .ToListAsync(cancellationToken);
        var postingIds = sessions.Where(s => s.JobPostingId is not null).Select(s => s.JobPostingId!.Value).Distinct().ToList();
        var offerIds = sessions.Where(s => s.OfferId is not null).Select(s => s.OfferId!.Value).Distinct().ToList();
        var postings = await db.JobPostings.AsNoTracking().Where(p => postingIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => $"{p.Title} — {p.Company}", cancellationToken);
        var offers = await db.CompensationOffers.AsNoTracking().Where(o => offerIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.Company, cancellationToken);
        return sessions.OrderBy(s => s.StartedAtUtc)
            .Select(s => new PracticeHistoryItem(s.Id, s.Mode, s.StartedAtUtc, s.Status, s.OverallScore,
                s.OfferId is { } o && offers.TryGetValue(o, out var company) ? company
                : s.JobPostingId is { } p && postings.TryGetValue(p, out var title) ? title : "—"))
            .ToList();
    }
}

/// <summary>
/// Runs practice conversations with the model as interviewer or recruiter. The app enforces the conversation rules
/// (<see cref="PracticeSession.Check"/>), keeps the transcript locally and asks for rubric feedback at the end.
/// </summary>
public sealed class PracticeHandler(
    PracticeStore store,
    InterviewPrepStore prep,
    OfferStore offers,
    TailoringContextFactory contexts,
    IDbContextFactory<MobileDbContext> dbFactory,
    PromptLibrary prompts,
    ILlmClient llm,
    IOptions<PracticeOptions> options,
    TimeProvider time)
{
    private static readonly JsonSerializerOptions TurnJson = LlmJson.Options;

    public async Task<(PreparedPractice? Prepared, string? Error)> PrepareAsync(PracticeSetup setup, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(setup);
        if (setup.Questions is < 1 or > 15)
        {
            return (null, "Elegí entre 1 y 15 preguntas o rondas.");
        }

        var (cv, cvError) = await contexts.CreateForCvAsync(cancellationToken);
        if (cv is null)
        {
            return (null, cvError);
        }

        if (setup.Mode == PracticeMode.Interview)
        {
            if (setup.PostingId is not { } postingId)
            {
                return (null, "Elegí la oferta para la que practicás.");
            }

            var (context, error) = await contexts.CreateAsync(postingId, cvVersion: null, cancellationToken);
            if (context is null)
            {
                return (null, error);
            }

            var bank = (await prep.LatestQuestionsAsync(postingId, cancellationToken))?.Set.Questions;
            var prompt = prompts.Get(LlmUseCases.MockInterview).Render(new Dictionary<string, string>
            {
                ["company"] = context.Posting.Company,
                ["role"] = context.Posting.Title,
                ["focus"] = setup.Focus switch { InterviewFocus.Behavioral => "behavioral", InterviewFocus.Technical => "technical", _ => "mixed behavioral and technical" },
                ["planned"] = setup.Questions.ToString(CultureInfo.InvariantCulture),
                ["max_follow_ups"] = options.Value.MaxFollowUps.ToString(CultureInfo.InvariantCulture),
                ["cv"] = context.CvWithReferences(),
                ["bank"] = bank is null || bank.Count == 0 ? "(none)" : string.Join("\n", bank.Select(q => $"- [{q.Category}] {q.Question}")),
                ["posting"] = JobAnalysisHandler.PostingText(context.Posting),
            });
            return (new PreparedPractice(setup, prompt), null);
        }

        if (setup.OfferId is not { } offerId || await offers.GetAsync(offerId, cancellationToken) is not { } offer)
        {
            return (null, "Elegí la oferta que querés negociar (cargala en Negociación → Comparador).");
        }

        if (setup.FlexPercent is < 0 or > 50)
        {
            return (null, "El margen de la simulación tiene que estar entre 0% y 50%.");
        }

        var posting = "(not provided)";
        if (offer.JobPostingId is { } linked)
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            if (await db.JobPostings.AsNoTracking().FirstOrDefaultAsync(p => p.Id == linked, cancellationToken) is { } found)
            {
                posting = JobAnalysisHandler.PostingText(found);
            }
        }

        var negotiation = prompts.Get(LlmUseCases.NegotiationRoleplay).Render(new Dictionary<string, string>
        {
            ["company"] = offer.Company,
            ["role"] = offer.Role ?? "Senior Software Engineer",
            ["offer"] = OfferStore.Describe(offer),
            ["flex_percent"] = setup.FlexPercent.ToString(CultureInfo.InvariantCulture),
            ["planned"] = setup.Questions.ToString(CultureInfo.InvariantCulture),
            ["max_follow_ups"] = options.Value.MaxFollowUps.ToString(CultureInfo.InvariantCulture),
            ["cv"] = cv.CvWithReferences(),
            ["posting"] = posting,
        });
        return (new PreparedPractice(setup, negotiation), null);
    }

    /// <summary>Creates the session and asks for the first coach turn.</summary>
    public async Task<(Guid SessionId, LlmError? Error)> StartAsync(PreparedPractice prepared, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        var setup = prepared.Setup;
        var session = PracticeSession.Start(setup.Mode, setup.PostingId, setup.OfferId, setup.Focus, setup.Questions, options.Value.MaxFollowUps,
            prepared.Prompt.PromptId, time.GetUtcNow().UtcDateTime);
        session.SystemInstruction = prepared.Prompt.SystemInstruction;
        session.OpeningMessage = prepared.Prompt.UserMessage;
        if (setup.Mode == PracticeMode.Negotiation && setup.OfferId is { } offerId && await offers.GetAsync(offerId, cancellationToken) is { JobPostingId: { } postingId })
        {
            session.JobPostingId = postingId;
        }

        await store.SaveAsync(session, cancellationToken);
        return (session.Id, await NextAsync(session.Id, cancellationToken));
    }

    /// <summary>Asks the model for the next interviewer/recruiter message when it is its turn.</summary>
    public async Task<LlmError?> NextAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var session = await store.GetAsync(sessionId, cancellationToken) ?? throw new InvalidOperationException("La sesión no existe.");
        if (!session.AwaitingCoach)
        {
            return null;
        }

        var result = await llm.ChatAsync(new LlmRequest<CoachTurnResponse>(PromptOf(session))
        {
            Validate = response => session.Check(ToProposal(response)),
        }, History(session), cancellationToken);
        if (!result.IsSuccess)
        {
            return result.Error;
        }

        session.AddCoachTurn(ToProposal(result.Value!), time.GetUtcNow().UtcDateTime);
        session.Model = result.Model;
        await store.SaveAsync(session, cancellationToken);
        return null;
    }

    /// <summary>Saves the candidate's answer and asks for the next message. The answer is kept even if the model fails.</summary>
    public async Task<LlmError?> AnswerAsync(Guid sessionId, string answer, CancellationToken cancellationToken)
    {
        var session = await store.GetAsync(sessionId, cancellationToken) ?? throw new InvalidOperationException("La sesión no existe.");
        session.AddAnswer(answer, time.GetUtcNow().UtcDateTime);
        await store.SaveAsync(session, cancellationToken);
        return await NextAsync(sessionId, cancellationToken);
    }

    public async Task FinishAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var session = await store.GetAsync(sessionId, cancellationToken) ?? throw new InvalidOperationException("La sesión no existe.");
        session.Finish();
        await store.SaveAsync(session, cancellationToken);
    }

    public async Task<(PreparedFeedback? Prepared, string? Error)> PrepareFeedbackAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var session = await store.GetAsync(sessionId, cancellationToken);
        if (session is null)
        {
            return (null, "La sesión no existe.");
        }

        if (session.Status != PracticeStatus.AwaitingFeedback)
        {
            return (null, "Terminá la conversación antes de pedir el feedback.");
        }

        var (context, error) = await contexts.CreateForCvAsync(cancellationToken);
        if (context is null)
        {
            return (null, error);
        }

        var contextText = await ContextTextAsync(session, cancellationToken);
        var rubric = string.Join("\n", Rubrics.For(session.Mode).Select(c => $"- {c.Key}: {c.Description}"));
        var prompt = prompts.Get(LlmUseCases.PracticeFeedback).Render(new Dictionary<string, string>
        {
            ["mode"] = session.Mode == PracticeMode.Interview ? "mock interview" : "salary negotiation",
            ["rubric"] = rubric,
            ["cv"] = context.CvWithReferences(),
            ["context"] = contextText,
            ["transcript"] = Transcript(session),
        });
        return (new PreparedFeedback(session, context, contextText, prompt), null);
    }

    public async Task<(PracticeFeedback? Feedback, LlmError? Error)> RunFeedbackAsync(PreparedFeedback prepared, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        var result = await llm.GenerateAsync(new LlmRequest<PracticeFeedbackResponse>(prepared.Prompt)
        {
            Validate = response => ValidateFeedback(prepared, response),
            Review = response => ToFeedback(prepared, response).ModelAnswers.SelectMany(a => a.Problems.Select(p => $"Q{a.QuestionNumber}: {p}")).ToList(),
        }, cancellationToken);
        if (!result.IsSuccess)
        {
            return (null, result.Error);
        }

        var feedback = ToFeedback(prepared, result.Value!);
        var session = prepared.Session;
        session.Complete(feedback, time.GetUtcNow().UtcDateTime);
        await store.SaveAsync(session, cancellationToken);
        return (feedback, null);
    }

    internal static IReadOnlyList<string> ValidateFeedback(PreparedFeedback prepared, PracticeFeedbackResponse response)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentNullException.ThrowIfNull(response);
        var problems = Rubrics.Validate(Rubrics.For(prepared.Session.Mode), Scores(response)).ToList();
        var numbers = prepared.Session.Exchanges().Select(e => e.Number).ToHashSet();
        foreach (var answer in response.ModelAnswers)
        {
            if (!numbers.Contains(answer.QuestionNumber))
            {
                problems.Add($"There is no answered question number {answer.QuestionNumber}.");
            }

            foreach (var reference in answer.Sources.Where(r => prepared.Context.Index.KeyOf(r) is null))
            {
                problems.Add($"\"{reference}\" is not a reference in the CV.");
            }
        }

        if (response.Strengths.Count == 0 || response.Improvements.Count == 0)
        {
            problems.Add("Include strengths and improvements.");
        }

        return problems;
    }

    /// <summary>The overall score is computed by the app; model answers are checked against the CV.</summary>
    internal static PracticeFeedback ToFeedback(PreparedFeedback prepared, PracticeFeedbackResponse response)
    {
        var scores = Scores(response);
        var interview = prepared.Session.Mode == PracticeMode.Interview;
        var answers = response.ModelAnswers
            .GroupBy(a => a.QuestionNumber).Select(g => g.First())
            .OrderBy(a => a.QuestionNumber)
            .Select(a =>
            {
                var text = prepared.Context.ToTraceable(new TracedTextResponse(a.Text, a.Sources));
                // Interview answers speak about the candidate: they must cite the CV. Negotiation answers may only use offer facts.
                return new ModelAnswer(a.QuestionNumber, text, prepared.Context.Validator.Check(text, allowedExtraText: prepared.AllowedText, requireSources: interview));
            })
            .ToList();
        return new PracticeFeedback(scores, response.Strengths, response.Improvements, answers, Rubrics.Overall(scores));
    }

    internal static string Transcript(PracticeSession session)
    {
        var builder = new StringBuilder();
        foreach (var turn in session.Turns)
        {
            var who = turn.Speaker == PracticeSpeaker.Candidate ? "Candidate" : turn.Kind switch
            {
                TurnKind.Question => $"Q{turn.QuestionNumber}",
                TurnKind.FollowUp => $"Q{turn.QuestionNumber} follow-up",
                _ => "Closing",
            };
            builder.AppendLine($"{who}: {turn.Text}");
        }

        return builder.ToString().Trim();
    }

    private async Task<string> ContextTextAsync(PracticeSession session, CancellationToken cancellationToken)
    {
        if (session.Mode == PracticeMode.Negotiation && session.OfferId is { } offerId && await offers.GetAsync(offerId, cancellationToken) is { } offer)
        {
            return await offers.FactsAsync(offer, cancellationToken);
        }

        if (session.JobPostingId is { } postingId)
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            if (await db.JobPostings.AsNoTracking().FirstOrDefaultAsync(p => p.Id == postingId, cancellationToken) is { } posting)
            {
                return JobAnalysisHandler.PostingText(posting);
            }
        }

        return "(none)";
    }

    private static List<CriterionScore> Scores(PracticeFeedbackResponse response) =>
        response.Scores.Select(s => new CriterionScore(s.Criterion.Trim(), s.Score, s.Justification)).ToList();

    private static RenderedPrompt PromptOf(PracticeSession session)
    {
        // PromptId is "<use-case>.v<N>": the session keeps the exact text that was sent first.
        var separator = session.PromptId.LastIndexOf(".v", StringComparison.Ordinal);
        var useCase = session.PromptId[..separator];
        var version = int.Parse(session.PromptId[(separator + 2)..], CultureInfo.InvariantCulture);
        return new RenderedPrompt(session.PromptId, useCase, version, session.SystemInstruction, session.OpeningMessage);
    }

    /// <summary>Coach turns go back as the JSON the model produced; candidate answers as plain text.</summary>
    internal static IReadOnlyList<LlmTurn> History(PracticeSession session) =>
        session.Turns.Select(t => t.Speaker == PracticeSpeaker.Coach
                ? new LlmTurn(LlmRole.Model, JsonSerializer.Serialize(new CoachTurnResponse(ToMove(t.Kind), t.QuestionNumber, t.Text), TurnJson))
                : new LlmTurn(LlmRole.User, t.Text))
            .ToList();

    private static CoachTurnProposal ToProposal(CoachTurnResponse response) => new(
        response.Kind switch { CoachMove.Question => TurnKind.Question, CoachMove.FollowUp => TurnKind.FollowUp, _ => TurnKind.Closing },
        response.QuestionNumber,
        response.Message);

    private static CoachMove ToMove(TurnKind kind) => kind switch
    {
        TurnKind.Question => CoachMove.Question,
        TurnKind.FollowUp => CoachMove.FollowUp,
        _ => CoachMove.Closing,
    };
}
