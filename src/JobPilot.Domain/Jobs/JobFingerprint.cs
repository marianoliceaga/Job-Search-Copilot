using System.Text.RegularExpressions;
using JobPilot.Domain.Cv;

namespace JobPilot.Domain.Jobs;

/// <summary>Normalized "company|title" used to recognise the same offer published by several sources.</summary>
public static partial class JobFingerprint
{
    private static readonly HashSet<string> CompanySuffixes =
        ["inc", "llc", "ltd", "limited", "corp", "corporation", "co", "gmbh", "sa", "s.a", "srl", "bv", "ag", "plc", "pty"];

    public static string Compute(string company, string title)
    {
        var cleanTitle = JobCodeRegex().Replace(TextNormalizer.Normalize(title), " ");
        cleanTitle = RemoteMarkerRegex().Replace(cleanTitle, " ");
        return CompanyKey(company) + "|" + string.Join(' ', Words(cleanTitle));
    }

    /// <summary>Normalized company name without legal suffixes ("Contoso, Inc." → "contoso").</summary>
    public static string CompanyKey(string company) =>
        string.Join(' ', Words(company).Where(w => !CompanySuffixes.Contains(w)));

    private static IEnumerable<string> Words(string text) =>
        WordRegex().Matches(TextNormalizer.Normalize(text)).Select(m => m.Value.TrimEnd('.'));

    [GeneratedRegex(@"[a-z0-9#+.]+")]
    private static partial Regex WordRegex();

    // "[Job - 30532]", "(577)", "#1234"
    [GeneratedRegex(@"\[[^\]]*\]|\(\s*\d+\s*\)|#\d+")]
    private static partial Regex JobCodeRegex();

    // "(Remote)", "- Remote", "100% remote", "remote - us"
    [GeneratedRegex(@"\(?\b(?:100% )?remote\b(?:\s*[-–]\s*[a-z ]+)?\)?")]
    private static partial Regex RemoteMarkerRegex();
}
