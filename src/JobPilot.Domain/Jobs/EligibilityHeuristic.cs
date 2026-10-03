using System.Text.RegularExpressions;
using JobPilot.Domain.Cv;

namespace JobPilot.Domain.Jobs;

public enum EligibilityStatus
{
    Unknown,
    Likely,
    Unlikely,
}

public sealed record EligibilityAssessment(EligibilityStatus Status, string Reason);

/// <summary>
/// Quick, rule-based guess of whether someone working from a given country can apply, based only on the published
/// location text. It is a hint for filtering before the LLM analysis, never a conclusion.
/// </summary>
public static partial class EligibilityHeuristic
{
    private static readonly string[] OpenTerms =
    [
        "worldwide", "anywhere", "global", "international", "any location", "all countries", "remote - all",
        "latam", "latin america", "south america", "americas", "lat am",
    ];

    public static EligibilityAssessment Assess(string? location, string workFromCountry)
    {
        var text = TextNormalizer.Normalize(location);
        var country = TextNormalizer.Normalize(workFromCountry);

        if (text.Length == 0)
        {
            return new(EligibilityStatus.Unknown, "La oferta no indica restricciones de ubicación.");
        }

        if (country.Length > 0 && text.Contains(country, StringComparison.Ordinal))
        {
            return new(EligibilityStatus.Likely, $"La ubicación menciona {workFromCountry}.");
        }

        var open = OpenTerms.FirstOrDefault(term => Regex.IsMatch(text, @"(?<![a-z])" + Regex.Escape(term) + @"(?![a-z])"));
        if (open is not null)
        {
            return new(EligibilityStatus.Likely, $"La ubicación dice \"{location!.Trim()}\".");
        }

        if (OnlyRemoteRegex().IsMatch(text))
        {
            return new(EligibilityStatus.Unknown, "Dice remoto sin indicar países.");
        }

        return new(EligibilityStatus.Unlikely, $"Restringida a \"{location!.Trim()}\".");
    }

    // Texts that only say "remote" (optionally with "full time", "100%", "job"...).
    [GeneratedRegex(@"^(?:100% )?(?:fully )?remote(?: job| work| first| ok| friendly)?$|^(?:all cities|n/?a|-)$")]
    private static partial Regex OnlyRemoteRegex();
}
