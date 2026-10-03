namespace JobPilot.Domain.Practice;

public enum PracticeMode
{
    /// <summary>Mock interview: the model plays the interviewer.</summary>
    Interview,

    /// <summary>Negotiation roleplay: the model plays the recruiter.</summary>
    Negotiation,
}

public enum InterviewFocus
{
    Behavioral,
    Technical,
    Mixed,
}

public enum PracticeSpeaker
{
    /// <summary>The interviewer or recruiter played by the model.</summary>
    Coach,
    Candidate,
}

public enum TurnKind
{
    /// <summary>A new main question (or negotiation move), numbered from 1.</summary>
    Question,

    /// <summary>A follow-up on the current question.</summary>
    FollowUp,

    /// <summary>The coach ends the conversation.</summary>
    Closing,

    /// <summary>The candidate's answer.</summary>
    Answer,
}

public enum PracticeStatus
{
    InProgress,

    /// <summary>The conversation ended; the feedback has not been generated yet.</summary>
    AwaitingFeedback,

    Completed,
}

public sealed record PracticeTurn(PracticeSpeaker Speaker, TurnKind Kind, int QuestionNumber, string Text, DateTime AtUtc);

/// <summary>A turn proposed by the model before the app accepts it.</summary>
public sealed record CoachTurnProposal(TurnKind Kind, int QuestionNumber, string Text);

/// <summary>
/// A practice conversation. The app, not the model, enforces the rules: one question at a time, numbered questions up
/// to the planned count, a bounded number of follow-ups per question, and closing only after the last question.
/// </summary>
public sealed class PracticeSession
{
    public Guid Id { get; set; }

    public PracticeMode Mode { get; set; }

    public Guid? JobPostingId { get; set; }

    /// <summary>Negotiation: the owner-entered offer being negotiated.</summary>
    public long? OfferId { get; set; }

    public InterviewFocus Focus { get; set; }

    public int PlannedQuestions { get; set; }

    public int MaxFollowUps { get; set; }

    public PracticeStatus Status { get; set; }

    public DateTime StartedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public required string PromptId { get; set; }

    public string? Model { get; set; }

    /// <summary>System instruction sent with every turn (kept so later turns use the same context as the first one).</summary>
    public string SystemInstruction { get; set; } = string.Empty;

    /// <summary>First user message (CV, posting, offer...), sent before the turns.</summary>
    public string OpeningMessage { get; set; } = string.Empty;

    public List<PracticeTurn> Turns { get; set; } = [];

    public PracticeFeedback? Feedback { get; set; }

    /// <summary>0-100, from the rubric scores (copied here for the history chart).</summary>
    public int? OverallScore { get; set; }

    public static PracticeSession Start(PracticeMode mode, Guid? postingId, long? offerId, InterviewFocus focus, int plannedQuestions, int maxFollowUps, string promptId, DateTime nowUtc)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(plannedQuestions, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(plannedQuestions, 15);
        ArgumentOutOfRangeException.ThrowIfNegative(maxFollowUps);
        return new PracticeSession
        {
            Id = Guid.NewGuid(),
            Mode = mode,
            JobPostingId = postingId,
            OfferId = offerId,
            Focus = focus,
            PlannedQuestions = plannedQuestions,
            MaxFollowUps = maxFollowUps,
            Status = PracticeStatus.InProgress,
            StartedAtUtc = nowUtc,
            PromptId = promptId,
        };
    }

    public int CurrentQuestion => Turns.Where(t => t.Speaker == PracticeSpeaker.Coach && t.Kind == TurnKind.Question).Select(t => t.QuestionNumber).DefaultIfEmpty(0).Max();

    public int FollowUpsOnCurrent => Turns.Count(t => t.Kind == TurnKind.FollowUp && t.QuestionNumber == CurrentQuestion);

    /// <summary>True when the coach asked something and the candidate has not answered yet.</summary>
    public bool AwaitingAnswer => Status == PracticeStatus.InProgress && Turns.Count > 0 && Turns[^1].Speaker == PracticeSpeaker.Coach;

    /// <summary>True when the model must produce the next coach turn.</summary>
    public bool AwaitingCoach => Status == PracticeStatus.InProgress && (Turns.Count == 0 || Turns[^1].Speaker == PracticeSpeaker.Candidate);

    public bool HasAnswers => Turns.Any(t => t.Speaker == PracticeSpeaker.Candidate);

    /// <summary>Problems with a proposed coach turn (English: they are sent back to the model on retry).</summary>
    public IReadOnlyList<string> Check(CoachTurnProposal proposal)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        var problems = new List<string>();
        if (!AwaitingCoach)
        {
            problems.Add("It is the candidate's turn.");
            return problems;
        }

        if (string.IsNullOrWhiteSpace(proposal.Text))
        {
            problems.Add("The message is empty.");
        }

        var questionMarks = proposal.Text?.Count(c => c == '?') ?? 0;
        if (proposal.Kind != TurnKind.Closing && questionMarks > 1)
        {
            problems.Add("Ask exactly one question per message.");
        }

        var current = CurrentQuestion;
        switch (proposal.Kind)
        {
            case TurnKind.Question when current >= PlannedQuestions:
                problems.Add($"All {PlannedQuestions} questions were asked: ask a follow-up or close the conversation.");
                break;
            case TurnKind.Question when proposal.QuestionNumber != current + 1:
                problems.Add($"The next question is number {current + 1}.");
                break;
            case TurnKind.FollowUp when current == 0:
                problems.Add("Ask the first question before any follow-up.");
                break;
            case TurnKind.FollowUp when FollowUpsOnCurrent >= MaxFollowUps:
                problems.Add($"Question {current} already had {MaxFollowUps} follow-up(s): move on to the next question or close.");
                break;
            case TurnKind.FollowUp when proposal.QuestionNumber != current:
                problems.Add($"A follow-up belongs to the current question, number {current}.");
                break;
            case TurnKind.Closing when current < PlannedQuestions:
                problems.Add($"Only {current} of {PlannedQuestions} questions were asked: ask question {current + 1}.");
                break;
            case TurnKind.Answer:
                problems.Add("The coach cannot answer for the candidate.");
                break;
        }

        return problems;
    }

    public void AddCoachTurn(CoachTurnProposal proposal, DateTime nowUtc)
    {
        if (Check(proposal) is { Count: > 0 } problems)
        {
            throw new InvalidOperationException(string.Join(" ", problems));
        }

        Turns.Add(new PracticeTurn(PracticeSpeaker.Coach, proposal.Kind, proposal.Kind == TurnKind.Closing ? CurrentQuestion : proposal.QuestionNumber, proposal.Text.Trim(), nowUtc));
        if (proposal.Kind == TurnKind.Closing)
        {
            Status = PracticeStatus.AwaitingFeedback;
        }
    }

    public void AddAnswer(string text, DateTime nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        if (!AwaitingAnswer)
        {
            throw new InvalidOperationException("No hay una pregunta pendiente de respuesta.");
        }

        Turns.Add(new PracticeTurn(PracticeSpeaker.Candidate, TurnKind.Answer, CurrentQuestion, text.Trim(), nowUtc));
    }

    /// <summary>The owner ends the conversation early; feedback covers the answers given so far.</summary>
    public void Finish()
    {
        if (!HasAnswers)
        {
            throw new InvalidOperationException("Respondé al menos una pregunta antes de pedir el feedback.");
        }

        if (Status == PracticeStatus.InProgress)
        {
            Status = PracticeStatus.AwaitingFeedback;
        }
    }

    public void Complete(PracticeFeedback feedback, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(feedback);
        Feedback = feedback;
        OverallScore = feedback.Overall;
        Status = PracticeStatus.Completed;
        CompletedAtUtc = nowUtc;
    }

    /// <summary>The questions asked, with the candidate's answers (follow-ups included), for feedback.</summary>
    public IReadOnlyList<(int Number, string Question, string Answers)> Exchanges() =>
        Turns.Where(t => t.QuestionNumber > 0)
            .GroupBy(t => t.QuestionNumber)
            .Select(g => (
                g.Key,
                string.Join("\n", g.Where(t => t.Speaker == PracticeSpeaker.Coach && t.Kind != TurnKind.Closing).Select(t => t.Text)),
                string.Join("\n", g.Where(t => t.Speaker == PracticeSpeaker.Candidate).Select(t => t.Text))))
            .Where(e => e.Item3.Length > 0)
            .ToList();
}
