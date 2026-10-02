using System.Text.RegularExpressions;
using JobPilot.Domain.Cv;

namespace JobPilot.Domain.Jobs;

/// <summary>Case- and accent-insensitive keyword search with word boundaries (".net" does not match "planet").</summary>
public sealed class KeywordMatcher
{
    private readonly Regex? _include;
    private readonly Regex? _exclude;

    public KeywordMatcher(IEnumerable<string> includeAny, IEnumerable<string>? excludeAny = null)
    {
        _include = Build(includeAny);
        _exclude = Build(excludeAny ?? []);
    }

    /// <summary>True when the text contains at least one included keyword (or none are configured) and no excluded one.</summary>
    public bool Matches(string? text)
    {
        var normalized = TextNormalizer.Normalize(text);
        return (_include is null || _include.IsMatch(normalized)) && (_exclude is null || !_exclude.IsMatch(normalized));
    }

    private static Regex? Build(IEnumerable<string> keywords)
    {
        var parts = keywords
            .Select(TextNormalizer.Normalize)
            .Where(k => k.Length > 0)
            .Distinct()
            .Select(k => @"(?<![a-z0-9])" + Regex.Escape(k) + @"(?![a-z0-9#+])")
            .ToList();
        return parts.Count == 0 ? null : new Regex(string.Join("|", parts), RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }
}
