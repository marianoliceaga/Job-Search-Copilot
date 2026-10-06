using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace JobPilot.Domain.Mail;

/// <summary>A mail rule that cannot be compiled (unknown set, bad pattern, unknown field...). The message names the rule.</summary>
public sealed class MailRulesException(string message, Exception? inner = null) : Exception(message, inner);

public static class MailText
{
    /// <summary>Lower-case, accents removed, whitespace collapsed: the form every term is matched against.</summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var lastWasSpace = false;
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace && builder.Length > 0)
                {
                    builder.Append(' ');
                }

                lastWasSpace = true;
                continue;
            }

            builder.Append(char.ToLowerInvariant(c));
            lastWasSpace = false;
        }

        return builder.ToString().TrimEnd();
    }
}

/// <summary>Expands "$name" references against the rules' named sets.</summary>
public sealed class SetResolver(IReadOnlyDictionary<string, List<string>> sets)
{
    public IReadOnlyList<string> Expand(IEnumerable<string>? terms, string context) => Expand(terms, context, []);

    private List<string> Expand(IEnumerable<string>? terms, string context, HashSet<string> visiting)
    {
        var result = new List<string>();
        foreach (var term in terms ?? [])
        {
            if (!term.StartsWith('$'))
            {
                result.Add(term);
                continue;
            }

            var name = term[1..];
            if (!sets.TryGetValue(name, out var members))
            {
                throw new MailRulesException($"{context}: \"{term}\" no es una lista definida en \"sets\".");
            }

            if (!visiting.Add(name))
            {
                throw new MailRulesException($"{context}: la lista \"{name}\" se incluye a sí misma.");
            }

            result.AddRange(Expand(members, $"sets.{name}", visiting));
            visiting.Remove(name);
        }

        return result;
    }
}

/// <summary>
/// A compiled list of words, phrases and regular expressions. Plain terms match whole words or phrases on normalized
/// text (case and accents ignored); "re:..." is a regular expression on normalized text. An empty set matches nothing.
/// </summary>
public sealed class TermSet
{
    public static readonly TermSet Nothing = new(null, []);

    private readonly Regex? _regex;

    private TermSet(Regex? regex, IReadOnlyList<string> terms)
    {
        _regex = regex;
        Terms = terms;
    }

    public IReadOnlyList<string> Terms { get; }

    public bool IsEmpty => _regex is null;

    public static TermSet Compile(IReadOnlyList<string> terms, string context)
    {
        if (terms.Count == 0)
        {
            return Nothing;
        }

        try
        {
            var regex = new Regex(string.Join('|', terms.Select(t => $"(?:{Pattern(t)})")),
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
            return new TermSet(regex, terms);
        }
        catch (ArgumentException ex)
        {
            throw new MailRulesException($"{context}: patrón inválido ({ex.Message}).", ex);
        }
    }

    /// <summary>Matches against text already passed through <see cref="MailText.Normalize"/>.</summary>
    public bool IsMatch(string normalized) => _regex?.IsMatch(normalized) ?? false;

    private static string Pattern(string term) =>
        term.StartsWith("re:", StringComparison.Ordinal)
            ? term[3..]
            : $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(MailText.Normalize(term))}(?![\p{{L}}\p{{N}}])";
}

/// <summary>A compiled sender list: exact addresses ("name@domain"), domains with their subdomains, and "re:" patterns.</summary>
public sealed class SenderSet
{
    public static readonly SenderSet Nothing = new([], [], TermSet.Nothing);

    private readonly HashSet<string> _addresses;
    private readonly List<string> _domains;
    private readonly TermSet _patterns;

    private SenderSet(HashSet<string> addresses, List<string> domains, TermSet patterns)
    {
        _addresses = addresses;
        _domains = domains;
        _patterns = patterns;
    }

    public static SenderSet Compile(IReadOnlyList<string> entries, string context)
    {
        if (entries.Count == 0)
        {
            return Nothing;
        }

        var addresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var domains = new List<string>();
        var patterns = new List<string>();
        foreach (var entry in entries)
        {
            if (entry.StartsWith("re:", StringComparison.Ordinal))
            {
                patterns.Add(entry);
            }
            else if (entry.Contains('@', StringComparison.Ordinal))
            {
                addresses.Add(entry.Trim());
            }
            else
            {
                domains.Add(entry.Trim().TrimStart('.').ToLowerInvariant());
            }
        }

        return new SenderSet(addresses, domains, TermSet.Compile(patterns, context));
    }

    public bool IsMatch(string address)
    {
        if (string.IsNullOrEmpty(address))
        {
            return false;
        }

        if (_addresses.Contains(address))
        {
            return true;
        }

        var at = address.LastIndexOf('@');
        var domain = at >= 0 ? address[(at + 1)..].ToLowerInvariant() : string.Empty;
        return _domains.Any(d => domain == d || domain.EndsWith("." + d, StringComparison.Ordinal))
            || _patterns.IsMatch(address.ToLowerInvariant());
    }
}

/// <summary>
/// Fills {placeholders}; unknown ones stay visible so a typo shows up. "{n|cosa|cosas}" renders a count with the right noun.
/// </summary>
public static partial class MailTemplate
{
    public static string Fill(string template, IReadOnlyDictionary<string, string> values)
    {
        var counted = CountRegex().Replace(template, m =>
            values.TryGetValue(m.Groups[1].Value, out var v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
                ? $"{n} {(n == 1 ? m.Groups[2].Value : m.Groups[3].Value)}"
                : m.Value);
        return PlaceholderRegex().Replace(counted, m => values.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);
    }

    [GeneratedRegex(@"\{(\w+)\|([^|{}]*)\|([^{}]*)\}")]
    private static partial Regex CountRegex();

    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex PlaceholderRegex();
}
