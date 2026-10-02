using JobPilot.Domain.Tailoring;

namespace JobPilot.Domain.Practice;

/// <param name="Key">Identifier the model must use.</param>
/// <param name="Name">Spanish label shown in the UI.</param>
/// <param name="Description">What is evaluated (English, sent to the model).</param>
public sealed record RubricCriterion(string Key, string Name, string Description);

public sealed record CriterionScore(string Criterion, int Score, string Justification);

/// <summary>A suggested answer built from the master CV; problems come from the traceability check.</summary>
public sealed record ModelAnswer(int QuestionNumber, TraceableText Answer, IReadOnlyList<string> Problems);

public sealed record PracticeFeedback(
    IReadOnlyList<CriterionScore> Scores,
    IReadOnlyList<string> Strengths,
    IReadOnlyList<string> Improvements,
    IReadOnlyList<ModelAnswer> ModelAnswers,
    int Overall);

/// <summary>Fixed rubrics: the model scores each criterion 1-5; the overall score is computed by the app.</summary>
public static class Rubrics
{
    public const int MinScore = 1;
    public const int MaxScore = 5;

    public static IReadOnlyList<RubricCriterion> Interview { get; } =
    [
        new("structure", "Estructura (STAR)", "Situation, task, action and result are clear and in order; the answer has a point."),
        new("relevance", "Relevancia", "The answer addresses the question and connects to the role."),
        new("evidence", "Evidencia concreta", "Specific examples, own contribution (I, not we), technical detail where it matters."),
        new("impact", "Impacto y resultados", "Outcomes, metrics or lessons are stated, without exaggeration."),
        new("communication", "Comunicación", "Clear, concise, confident English; appropriate length."),
    ];

    public static IReadOnlyList<RubricCriterion> Negotiation { get; } =
    [
        new("anchoring", "Anclaje", "States a clear, well-placed number or range early and does not undercut it."),
        new("justification", "Justificación", "Supports the ask with market data the candidate has and concrete value from experience."),
        new("objections", "Manejo de objeciones", "Handles pushback calmly, asks questions, trades instead of conceding."),
        new("collaboration", "Tono colaborativo", "Keeps a positive, professional relationship with the recruiter."),
        new("closing", "Cierre", "Summarises agreements, asks for the offer in writing and clear next steps."),
    ];

    public static IReadOnlyList<RubricCriterion> For(PracticeMode mode) => mode == PracticeMode.Interview ? Interview : Negotiation;

    /// <summary>Every criterion exactly once, no unknown criteria, scores 1-5 and a justification (English, for retries).</summary>
    public static IReadOnlyList<string> Validate(IReadOnlyList<RubricCriterion> rubric, IReadOnlyList<CriterionScore> scores)
    {
        ArgumentNullException.ThrowIfNull(rubric);
        ArgumentNullException.ThrowIfNull(scores);
        var problems = new List<string>();
        var keys = rubric.Select(c => c.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var unknown in scores.Select(s => s.Criterion).Where(k => !keys.Contains(k)).Distinct())
        {
            problems.Add($"\"{unknown}\" is not a rubric criterion.");
        }

        foreach (var criterion in rubric)
        {
            var count = scores.Count(s => s.Criterion == criterion.Key);
            if (count != 1)
            {
                problems.Add($"Score \"{criterion.Key}\" exactly once (found {count}).");
            }
        }

        foreach (var score in scores)
        {
            if (score.Score is < MinScore or > MaxScore)
            {
                problems.Add($"Score for \"{score.Criterion}\" must be between {MinScore} and {MaxScore}.");
            }

            if (string.IsNullOrWhiteSpace(score.Justification))
            {
                problems.Add($"Justify the score for \"{score.Criterion}\".");
            }
        }

        return problems;
    }

    /// <summary>Average of the criteria mapped to 0-100 (1 → 0, 5 → 100).</summary>
    public static int Overall(IReadOnlyCollection<CriterionScore> scores)
    {
        ArgumentNullException.ThrowIfNull(scores);
        if (scores.Count == 0)
        {
            return 0;
        }

        var average = scores.Average(s => Math.Clamp(s.Score, MinScore, MaxScore));
        return (int)Math.Round((average - MinScore) / (MaxScore - MinScore) * 100, MidpointRounding.AwayFromZero);
    }
}
