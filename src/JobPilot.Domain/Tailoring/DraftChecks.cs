using System.Text.RegularExpressions;

namespace JobPilot.Domain.Tailoring;

/// <summary>Checks shared by generated drafts (follow-ups, outreach, LinkedIn texts).</summary>
public static partial class DraftChecks
{
    /// <summary>A template placeholder such as "[Name]", "{{date}}" or "&lt;Company&gt;", or null when there is none.</summary>
    public static string? FindPlaceholder(string? text) =>
        PlaceholderRegex().Match(text ?? string.Empty) is { Success: true } match ? match.Value : null;

    [GeneratedRegex(@"\[[^\]\n]{1,40}\]|\{\{?[^}\n]{1,40}\}\}?|<[A-Za-z][^>\n]{0,40}>")]
    private static partial Regex PlaceholderRegex();
}
