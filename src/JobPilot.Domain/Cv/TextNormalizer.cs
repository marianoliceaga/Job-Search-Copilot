using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace JobPilot.Domain.Cv;

/// <summary>Normalization used to compare generated text against source documents.</summary>
public static partial class TextNormalizer
{
    /// <summary>Lower case, no diacritics, unified dashes and quotes, single spaces, thousands separators removed.</summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(c switch
            {
                '\u2010' or '\u2011' or '\u2012' or '\u2013' or '\u2014' or '\u2212' => '-',
                '\u2018' or '\u2019' or '\u201B' => '\'',
                '\u201C' or '\u201D' => '"',
                '\u00A0' => ' ',
                _ => char.ToLowerInvariant(c),
            });
        }

        var collapsed = WhitespaceRegex().Replace(builder.ToString(), " ").Trim();
        return ThousandsSeparatorRegex().Replace(collapsed, string.Empty);
    }

    /// <summary>Numbers mentioned in the text (e.g. "40%", "2,000", "3.5"), normalized.</summary>
    public static IReadOnlyList<string> Numbers(string? text) =>
        NumberRegex().Matches(Normalize(text)).Select(m => m.Value).Distinct().ToList();

    /// <summary>Words of four or more letters, used to measure how close a text is to its source.</summary>
    public static IReadOnlyList<string> ContentWords(string? text) =>
        WordRegex().Matches(Normalize(text)).Select(m => m.Value).Where(w => w.Length >= 4).Distinct().ToList();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"(?<=\d),(?=\d{3}(?!\d))")]
    private static partial Regex ThousandsSeparatorRegex();

    [GeneratedRegex(@"\d+(?:\.\d+)?")]
    private static partial Regex NumberRegex();

    [GeneratedRegex(@"[a-z][a-z0-9+#.]*[a-z0-9+#]|[a-z]")]
    private static partial Regex WordRegex();
}
