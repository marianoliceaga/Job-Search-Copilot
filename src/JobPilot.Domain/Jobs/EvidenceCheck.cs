using System.Globalization;
using System.Text.RegularExpressions;
using JobPilot.Domain.Cv;

namespace JobPilot.Domain.Jobs;

/// <summary>
/// Deterministic checks that a value attributed to a posting really appears in it. Used to reject LLM output that
/// cannot be traced to the source (e.g. a salary the posting does not state).
/// </summary>
public static partial class EvidenceCheck
{
    /// <summary>True when the quote appears in the text (ignoring case, accents, spacing, dash/quote styles).</summary>
    public static bool QuoteFound(string? quote, string? text)
    {
        var normalizedQuote = TextNormalizer.Normalize(quote);
        return normalizedQuote.Length > 0 && TextNormalizer.Normalize(text).Contains(normalizedQuote, StringComparison.Ordinal);
    }

    /// <summary>True when the amount is written in the quote, as a number or abbreviated with "k" (120000 ↔ "120k").</summary>
    public static bool AmountInQuote(decimal amount, string? quote)
    {
        foreach (Match match in AmountRegex().Matches(TextNormalizer.Normalize(quote)))
        {
            var value = decimal.Parse(match.Groups["number"].Value, CultureInfo.InvariantCulture);
            if (match.Groups["k"].Success)
            {
                value *= 1000;
            }

            if (value == amount)
            {
                return true;
            }
        }

        return false;
    }

    [GeneratedRegex(@"(?<number>\d+(?:\.\d+)?)\s*(?<k>k\b)?")]
    private static partial Regex AmountRegex();
}
