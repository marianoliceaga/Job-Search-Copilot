using JobPilot.Domain.Cv;
using JobPilot.Domain.Jobs;

namespace JobPilot.Domain.Tailoring;

/// <summary>
/// Deterministic "never invent" check for generated application content. A text passes when it cites existing master CV
/// elements and every number (amounts, percentages, years) and every known technology it mentions appears in the cited
/// elements, in the owner's documented skills/technologies or, for numbers only, in an allowed extra text (the posting,
/// for cover letters). Messages are user-facing (Spanish) and also sent to the LLM on retry.
/// </summary>
public sealed class TraceabilityValidator
{
    private readonly CvSourceIndex _index;
    private readonly KeywordMatcher[] _technologies;
    private readonly string[] _technologyNames;
    private readonly HashSet<string> _vocabulary;

    /// <param name="technologyCatalog">Technology names to detect (catalog file + owner vocabulary).</param>
    public TraceabilityValidator(CvSourceIndex index, IEnumerable<string> technologyCatalog)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(technologyCatalog);
        _index = index;
        _vocabulary = index.Vocabulary.Select(TextNormalizer.Normalize).ToHashSet(StringComparer.Ordinal);
        _technologyNames = technologyCatalog.Concat(index.Vocabulary)
            .Select(t => t.Trim()).Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        _technologies = _technologyNames.Select(t => new KeywordMatcher([t])).ToArray();
    }

    /// <param name="experienceId">For CV bullets: sources must belong to this experience.</param>
    /// <param name="allowedExtraText">Text whose numbers may also be used (e.g. the posting in a cover letter).</param>
    public IReadOnlyList<string> Check(TraceableText text, Guid? experienceId = null, string? allowedExtraText = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(text.Text))
        {
            return ["El texto está vacío."];
        }

        if (text.Sources.Count == 0)
        {
            problems.Add("No cita ningún elemento del CV maestro.");
        }

        foreach (var source in text.Sources.Where(s => !_index.Contains(s)))
        {
            problems.Add($"Cita un elemento que no existe en el CV maestro ({source}).");
        }

        if (experienceId is { } expected)
        {
            foreach (var source in text.Sources.Where(_index.Contains))
            {
                if (_index.ExperienceOf(source) is { } owner && owner != expected)
                {
                    problems.Add($"Cita un logro de otra experiencia ({_index.RefOf(source)}).");
                }
                else if (_index.ExperienceOf(source) is null && source != SourceKeys.Profile)
                {
                    problems.Add($"Una viñeta de experiencia solo puede citar esa experiencia o sus logros ({_index.RefOf(source)}).");
                }
            }
        }

        var sourceText = string.Join("\n", text.Sources.Where(_index.Contains).Select(_index.TextOf));
        var allowedNumbers = TextNormalizer.Numbers(sourceText).Concat(TextNormalizer.Numbers(allowedExtraText)).ToHashSet(StringComparer.Ordinal);
        var invented = TextNormalizer.Numbers(text.Text).Where(n => !allowedNumbers.Contains(n)).ToList();
        if (invented.Count > 0)
        {
            problems.Add($"Menciona números que no están en las fuentes citadas: {string.Join(", ", invented)}.");
        }

        var unknownTech = Enumerable.Range(0, _technologies.Length)
            .Where(i => _technologies[i].Matches(text.Text)
                        && !_vocabulary.Contains(TextNormalizer.Normalize(_technologyNames[i]))
                        && !_technologies[i].Matches(sourceText))
            .Select(i => _technologyNames[i])
            .ToList();
        if (unknownTech.Count > 0)
        {
            problems.Add($"Menciona tecnologías que no figuran en tu CV ni en las fuentes citadas: {string.Join(", ", unknownTech)}.");
        }

        return problems;
    }

    /// <summary>Problems of a whole tailored CV, keyed by the text they belong to.</summary>
    public IReadOnlyList<(TraceableText Text, IReadOnlyList<string> Problems)> Check(TailoredCv cv)
    {
        ArgumentNullException.ThrowIfNull(cv);
        var results = new List<(TraceableText, IReadOnlyList<string>)>
        {
            (cv.Headline, Check(cv.Headline)),
            (cv.Summary, Check(cv.Summary)),
        };
        foreach (var experience in cv.Experiences)
        {
            results.AddRange(experience.Bullets.Select(b => (b, Check(b, experience.ExperienceId))));
        }

        return results.Where(r => r.Item2.Count > 0).ToList();
    }

    /// <summary>Skills that are not documented in the master CV.</summary>
    public IReadOnlyList<string> UnknownSkills(IEnumerable<string> skills) =>
        skills.Where(s => !_vocabulary.Contains(TextNormalizer.Normalize(s))).ToList();
}
