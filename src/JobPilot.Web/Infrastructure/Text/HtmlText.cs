using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace JobPilot.Web.Infrastructure.Text;

/// <summary>
/// Converts third-party HTML (job descriptions) to plain text. The app never renders source HTML: it would be an
/// injection vector, and plain text is what the LLM and the quote checks need.
/// </summary>
public static partial class HtmlText
{
    private static readonly HashSet<string> BlockElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "P", "DIV", "SECTION", "ARTICLE", "HEADER", "FOOTER", "UL", "OL", "TABLE", "TR", "BLOCKQUOTE", "PRE",
        "H1", "H2", "H3", "H4", "H5", "H6", "DL", "DT", "DD",
    };

    private static readonly HashSet<string> SkippedElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "SCRIPT", "STYLE", "NOSCRIPT", "IMG", "SVG", "IFRAME", "OBJECT", "HEAD",
    };

    public static string ToPlainText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var document = new HtmlParser().ParseDocument(html);
        var builder = new StringBuilder();
        Append(document.Body ?? (INode)document.DocumentElement, builder);
        return Clean(builder.ToString());
    }

    private static void Append(INode node, StringBuilder builder)
    {
        foreach (var child in node.ChildNodes)
        {
            switch (child)
            {
                case IText text:
                    builder.Append(WhitespaceRegex().Replace(text.Data, " "));
                    break;
                case IElement element when SkippedElements.Contains(element.TagName):
                    break;
                case IElement element when element.TagName == "BR":
                    builder.Append('\n');
                    break;
                case IElement element when element.TagName == "LI":
                    builder.Append("\n- ");
                    Append(element, builder);
                    builder.Append('\n');
                    break;
                case IElement element when element.TagName is "TD" or "TH":
                    Append(element, builder);
                    builder.Append(" | ");
                    break;
                case IElement element when BlockElements.Contains(element.TagName):
                    builder.Append('\n');
                    Append(element, builder);
                    builder.Append('\n');
                    break;
                case IElement element:
                    Append(element, builder);
                    break;
            }
        }
    }

    private static string Clean(string text)
    {
        var lines = text.Split('\n').Select(line => SpacesRegex().Replace(line, " ").Trim().TrimEnd('|').Trim());
        return BlankLinesRegex().Replace(string.Join("\n", lines), "\n\n").Trim();
    }

    [GeneratedRegex(@"[\r\n\t]+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"[ \u00A0]+")]
    private static partial Regex SpacesRegex();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex BlankLinesRegex();
}
