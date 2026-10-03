using System.Text.RegularExpressions;

namespace JobPilot.Domain.Tailoring;

public enum DiffKind
{
    Same,
    Added,
    Removed,
}

public sealed record DiffSegment(DiffKind Kind, string Text);

/// <summary>Word-level diff (longest common subsequence), to show how a generated text differs from its source.</summary>
public static partial class WordDiff
{
    public static IReadOnlyList<DiffSegment> Compute(string? original, string? revised)
    {
        var a = Tokens(original);
        var b = Tokens(revised);
        var lcs = new int[a.Length + 1, b.Length + 1];
        for (var i = a.Length - 1; i >= 0; i--)
        {
            for (var j = b.Length - 1; j >= 0; j--)
            {
                lcs[i, j] = Same(a[i], b[j]) ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            }
        }

        var segments = new List<DiffSegment>();
        int x = 0, y = 0;
        while (x < a.Length || y < b.Length)
        {
            if (x < a.Length && y < b.Length && Same(a[x], b[y]))
            {
                Append(segments, DiffKind.Same, b[y]);
                x++;
                y++;
            }
            else if (y < b.Length && (x == a.Length || lcs[x, y + 1] >= lcs[x + 1, y]))
            {
                Append(segments, DiffKind.Added, b[y++]);
            }
            else
            {
                Append(segments, DiffKind.Removed, a[x++]);
            }
        }

        return segments;
    }

    private static bool Same(string a, string b) =>
        string.Equals(a.TrimEnd('.', ',', ';', ':'), b.TrimEnd('.', ',', ';', ':'), StringComparison.OrdinalIgnoreCase);

    private static void Append(List<DiffSegment> segments, DiffKind kind, string word)
    {
        if (segments.Count > 0 && segments[^1].Kind == kind)
        {
            segments[^1] = segments[^1] with { Text = segments[^1].Text + " " + word };
        }
        else
        {
            segments.Add(new DiffSegment(kind, word));
        }
    }

    private static string[] Tokens(string? text) =>
        string.IsNullOrWhiteSpace(text) ? [] : SpaceRegex().Split(text.Trim());

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpaceRegex();
}
