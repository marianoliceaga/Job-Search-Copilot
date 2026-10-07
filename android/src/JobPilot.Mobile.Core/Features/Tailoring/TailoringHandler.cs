using JobPilot.Domain.Tailoring;
using JobPilot.Domain.Prompts;
using JobPilot.Mobile.Core.Features.Jobs.Analysis;
using JobPilot.Mobile.Core.Infrastructure.Llm;
using JobPilot.Mobile.Core.Infrastructure.Persistence;

namespace JobPilot.Mobile.Core.Features.Tailoring;

public sealed record PreparedTailoring(TailoringContext Context, TailoredKind Kind, RenderedPrompt Prompt);

/// <param name="DocumentId">The stored draft, when generation succeeded.</param>
public sealed record TailoringResult(LlmError? Error, long? DocumentId, IReadOnlyList<string> Warnings);

/// <summary>
/// Generates a tailored CV or a cover letter from the master CV for one offer. The model must cite the CV elements of
/// every text; untraceable texts get one retry with the reasons and are flagged if they persist. The result is a draft.
/// </summary>
public sealed class TailoringHandler(
    TailoringContextFactory contexts,
    TailoredDocumentStore store,
    JobAnalysisReader analyses,
    PromptLibrary prompts,
    ILlmClient llm)
{
    public async Task<(PreparedTailoring? Prepared, string? Error)> PrepareAsync(Guid postingId, TailoredKind kind, CancellationToken cancellationToken)
    {
        var (context, error) = await contexts.CreateAsync(postingId, cvVersion: null, cancellationToken);
        if (context is null)
        {
            return (null, error);
        }

        var analysis = await analyses.LatestSummaryAsync(postingId, cancellationToken);
        var useCase = kind == TailoredKind.Cv ? LlmUseCases.CvTailoring : LlmUseCases.CoverLetter;
        var prompt = prompts.Get(useCase).Render(new Dictionary<string, string>
        {
            ["cv"] = context.CvWithReferences(),
            ["emphases"] = context.Emphases(),
            ["posting"] = JobAnalysisHandler.PostingText(context.Posting),
            ["analysis"] = analysis ?? "(no analysis yet)",
        });
        return (new PreparedTailoring(context, kind, prompt), null);
    }

    public async Task<TailoringResult> RunAsync(PreparedTailoring prepared, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        var context = prepared.Context;

        if (prepared.Kind == TailoredKind.Cv)
        {
            var result = await llm.GenerateAsync(new LlmRequest<TailoredCvResponse>(prepared.Prompt)
            {
                Validate = response => ValidateCv(context, response),
                Review = response => Review(context, ToCv(context, response)),
            }, cancellationToken);
            if (!result.IsSuccess)
            {
                return new TailoringResult(result.Error, null, []);
            }

            var id = await store.AddDraftAsync(context.Posting.Id, TailoredKind.Cv, context.CvVersion, ToCv(context, result.Value!),
                prepared.Prompt.PromptId, result.Model, result.Value!.Notes, cancellationToken);
            return new TailoringResult(null, id, result.Warnings);
        }

        var letter = await llm.GenerateAsync(new LlmRequest<CoverLetterResponse>(prepared.Prompt)
        {
            Validate = response => ValidateLetter(context, response),
            Review = response => Review(context, ToLetter(context, response)),
        }, cancellationToken);
        if (!letter.IsSuccess)
        {
            return new TailoringResult(letter.Error, null, []);
        }

        var letterId = await store.AddDraftAsync(context.Posting.Id, TailoredKind.CoverLetter, context.CvVersion, ToLetter(context, letter.Value!),
            prepared.Prompt.PromptId, letter.Model, letter.Value!.Notes, cancellationToken);
        return new TailoringResult(null, letterId, letter.Warnings);
    }

    /// <summary>Structural problems (unknown references, empty texts): always retried, never accepted.</summary>
    internal static IReadOnlyList<string> ValidateCv(TailoringContext context, TailoredCvResponse response)
    {
        var problems = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var experience in response.Experiences)
        {
            var key = context.Index.KeyOf(experience.Experience);
            if (key is null || context.Index.ExperienceOf(key) is not { } id || id.ToString() != key)
            {
                problems.Add($"\"{experience.Experience}\" is not an experience reference (use E1, E2, ...).");
            }
            else if (!seen.Add(key))
            {
                problems.Add($"Experience {experience.Experience} appears twice.");
            }

            if (experience.Bullets.Count == 0)
            {
                problems.Add($"Experience {experience.Experience} has no bullets; omit it instead.");
            }
        }

        if (response.Experiences.Count == 0)
        {
            problems.Add("Include at least one experience.");
        }

        problems.AddRange(UnknownReferences(context, response.Experiences.SelectMany(e => e.Bullets).Append(response.Headline).Append(response.Summary)));
        return problems;
    }

    internal static IReadOnlyList<string> ValidateLetter(TailoringContext context, CoverLetterResponse response)
    {
        var problems = new List<string>();
        if (response.Paragraphs.Count == 0)
        {
            problems.Add("The letter has no paragraphs.");
        }

        problems.AddRange(UnknownReferences(context, response.Paragraphs));
        return problems;
    }

    internal static TailoredCv ToCv(TailoringContext context, TailoredCvResponse response)
    {
        var order = context.Cv.Experiences.Select((e, i) => (e.Id, i)).ToDictionary(x => x.Id, x => x.i);
        var experiences = response.Experiences
            .Select(e => (Key: context.Index.KeyOf(e.Experience), e.Bullets))
            .Where(e => e.Key is not null && Guid.TryParse(e.Key, out var id) && order.ContainsKey(id))
            .DistinctBy(e => e.Key)
            .Select(e => new TailoredExperience { ExperienceId = Guid.Parse(e.Key!), Bullets = e.Bullets.Select(context.ToTraceable).ToList() })
            // Reverse-chronological order of the master CV is kept: ATS and readers expect it.
            .OrderBy(e => order[e.ExperienceId])
            .ToList();

        return new TailoredCv
        {
            Headline = context.ToTraceable(response.Headline),
            Summary = context.ToTraceable(response.Summary),
            Experiences = experiences,
            Skills = response.Skills.Select(s => s.Trim()).Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
        };
    }

    internal static CoverLetter ToLetter(TailoringContext context, CoverLetterResponse response) =>
        new() { Paragraphs = response.Paragraphs.Select(context.ToTraceable).ToList() };

    /// <summary>Traceability problems (soft): one retry with the reasons, then flagged in the UI.</summary>
    internal static IReadOnlyList<string> Review(TailoringContext context, TailoredCv cv)
    {
        var check = TailoredDocumentStore.Check(context, cv);
        return check.Flags.Select(f => $"\"{Short(f.Text.Text)}\": {string.Join(" ", f.Problems)}")
            .Concat(check.UnknownSkills.Select(s => $"Skill \"{s}\" is not in the CV."))
            .ToList();
    }

    internal static IReadOnlyList<string> Review(TailoringContext context, CoverLetter letter) =>
        TailoredDocumentStore.Check(context, letter).Flags.Select(f => $"\"{Short(f.Text.Text)}\": {string.Join(" ", f.Problems)}").ToList();

    private static IEnumerable<string> UnknownReferences(TailoringContext context, IEnumerable<TracedTextResponse> texts)
    {
        foreach (var text in texts)
        {
            if (string.IsNullOrWhiteSpace(text.Text))
            {
                yield return "A text is empty.";
            }

            if (text.Sources.Count == 0)
            {
                yield return $"\"{Short(text.Text)}\" cites no CV element.";
            }

            foreach (var reference in text.Sources.Where(r => context.Index.KeyOf(r) is null))
            {
                yield return $"\"{Short(text.Text)}\" cites \"{reference}\", which is not a reference in the CV.";
            }
        }
    }

    private static string Short(string text) => text.Length <= 60 ? text : text[..60] + "…";
}
