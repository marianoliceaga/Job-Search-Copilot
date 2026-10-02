using System.Globalization;
using JobPilot.Domain.Cv;

namespace JobPilot.Domain.Tailoring;

/// <summary>
/// The texts generated content may draw from, by source key, plus the owner's technology vocabulary. Also assigns the
/// short references (E1, E1.A2, SUM, ...) used in prompts, which are cheaper and more reliable than GUIDs.
/// </summary>
public sealed class CvSourceIndex
{
    private readonly Dictionary<string, string> _texts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Guid> _experienceOfAchievement = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _keyByRef = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _refByKey = new(StringComparer.Ordinal);

    public CvSourceIndex(MasterCv cv, string profileText)
    {
        ArgumentNullException.ThrowIfNull(cv);
        Cv = cv;
        Add(SourceKeys.Headline, "HEAD", cv.Headline ?? string.Empty);
        Add(SourceKeys.Summary, "SUM", cv.Summary ?? string.Empty);
        Add(SourceKeys.Profile, "PROF", profileText);

        for (var e = 0; e < cv.Experiences.Count; e++)
        {
            var experience = cv.Experiences[e];
            var experienceRef = "E" + (e + 1).ToString(CultureInfo.InvariantCulture);
            Add(experience.Id.ToString(), experienceRef, string.Join("\n",
                new[] { experience.Title, experience.Company, experience.Location, experience.Description, string.Join(", ", experience.Technologies) }
                    .Where(s => !string.IsNullOrWhiteSpace(s))));

            for (var a = 0; a < experience.Achievements.Count; a++)
            {
                var achievement = experience.Achievements[a];
                Add(achievement.Id.ToString(), $"{experienceRef}.A{(a + 1).ToString(CultureInfo.InvariantCulture)}", achievement.Text);
                _experienceOfAchievement[achievement.Id.ToString()] = experience.Id;
            }
        }

        Vocabulary = cv.Skills.Select(s => s.Name)
            .Concat(cv.Experiences.SelectMany(e => e.Technologies))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public MasterCv Cv { get; }

    /// <summary>Skills and technologies documented in the master CV.</summary>
    public IReadOnlyList<string> Vocabulary { get; }

    public bool Contains(string key) => _texts.ContainsKey(key);

    public string TextOf(string key) => _texts.TryGetValue(key, out var text) ? text : string.Empty;

    /// <summary>The experience an achievement belongs to; for an experience key, the experience itself.</summary>
    public Guid? ExperienceOf(string key) =>
        _experienceOfAchievement.TryGetValue(key, out var id) ? id
        : Guid.TryParse(key, out var experienceId) && Cv.Experiences.Any(e => e.Id == experienceId) ? experienceId
        : null;

    public string? KeyOf(string reference) => _keyByRef.TryGetValue(reference.Trim(), out var key) ? key : null;

    public string RefOf(string key) => _refByKey.TryGetValue(key, out var reference) ? reference : key;

    private void Add(string key, string reference, string text)
    {
        _texts[key] = text;
        _keyByRef[reference] = key;
        _refByKey[key] = reference;
    }
}
