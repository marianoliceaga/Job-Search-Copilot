using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.Exceptions;

namespace JobPilot.Web.Features.Profile.Import;

public sealed record ExtractedDocument(string FileName, string Format, string Text);

public sealed record ExtractionResult(ExtractedDocument? Document, string? Error)
{
    public static ExtractionResult Fail(string error) => new(null, error);
}

/// <summary>
/// Extracts plain text from a CV locally (nothing is sent anywhere): PDF with PdfPig, DOCX with Open XML,
/// Markdown and plain text as-is.
/// </summary>
public static partial class CvTextExtractor
{
    public const long MaxFileBytes = 10 * 1024 * 1024;

    /// <summary>Upper bound for the text sent to the LLM (a long CV is ~20,000 characters).</summary>
    public const int MaxTextLength = 60_000;

    public static IReadOnlyList<string> SupportedExtensions { get; } = [".pdf", ".docx", ".md", ".markdown", ".txt"];

    public static ExtractionResult Extract(string fileName, byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.Length == 0)
        {
            return ExtractionResult.Fail("El archivo está vacío.");
        }

        if (content.Length > MaxFileBytes)
        {
            return ExtractionResult.Fail("El archivo supera los 10 MB.");
        }

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        string text;
        string format;
        try
        {
            (format, text) = extension switch
            {
                ".pdf" => ("PDF", FromPdf(content)),
                ".docx" => ("DOCX", FromDocx(content)),
                ".md" or ".markdown" => ("Markdown", FromPlainText(content)),
                ".txt" => ("Texto", FromPlainText(content)),
                _ => (string.Empty, string.Empty),
            };
        }
        catch (PdfDocumentEncryptedException)
        {
            return ExtractionResult.Fail("El PDF está protegido con contraseña. Exportalo sin protección y volvé a intentar.");
        }
        catch (Exception ex) when (ex is UglyToad.PdfPig.Core.PdfDocumentFormatException or InvalidDataException or IOException
            or DocumentFormat.OpenXml.Packaging.OpenXmlPackageException or FileFormatException)
        {
            return ExtractionResult.Fail($"No se pudo leer el archivo: {ex.Message}");
        }

        if (format.Length == 0)
        {
            return ExtractionResult.Fail($"Formato no soportado. Usá {string.Join(", ", SupportedExtensions)}.");
        }

        text = Clean(text);
        if (text.Length < 50)
        {
            return ExtractionResult.Fail(format == "PDF"
                ? "El PDF casi no tiene texto extraíble (¿es una imagen escaneada?). Probá con DOCX o Markdown."
                : "El documento casi no tiene texto.");
        }

        if (text.Length > MaxTextLength)
        {
            return ExtractionResult.Fail($"El texto extraído supera los {MaxTextLength:N0} caracteres; no parece un CV.");
        }

        return new ExtractionResult(new ExtractedDocument(Path.GetFileName(fileName), format, text), null);
    }

    private static string FromPdf(byte[] content)
    {
        using var document = PdfDocument.Open(content);
        return string.Join("\n\n", document.GetPages().Select(page =>
        {
            var text = ContentOrderTextExtractor.GetText(page);
            // Link targets (e.g. a "LinkedIn" label) are annotations, not text: list the ones not already visible.
            var links = page.GetHyperlinks()
                .Where(link => !string.IsNullOrWhiteSpace(link.Uri) && !text.Contains(link.Uri, StringComparison.OrdinalIgnoreCase))
                .Select(link => $"- {link.Text?.Trim()}: {link.Uri}")
                .Distinct()
                .ToList();
            return links.Count == 0 ? text : text + "\n\nLinks:\n" + string.Join("\n", links);
        }));
    }

    private static string FromDocx(byte[] content)
    {
        using var stream = new MemoryStream(content, writable: false);
        using var document = WordprocessingDocument.Open(stream, isEditable: false);
        var main = document.MainDocumentPart ?? throw new InvalidDataException("The document has no body.");
        var body = main.Document?.Body ?? throw new InvalidDataException("The document has no body.");

        var listStyles = ListStyleIds(main);
        var builder = new StringBuilder();
        foreach (var element in body.ChildElements)
        {
            switch (element)
            {
                case Paragraph paragraph:
                    AppendParagraph(builder, paragraph, main, listStyles);
                    break;
                case Table table:
                    foreach (var row in table.Elements<TableRow>())
                    {
                        var cells = row.Elements<TableCell>()
                            .Select(cell => string.Join(" ", cell.Elements<Paragraph>().Select(p => ParagraphText(p, main))).Trim())
                            .Where(cell => cell.Length > 0);
                        builder.AppendLine(string.Join(" | ", cells));
                    }

                    break;
            }
        }

        return builder.ToString();
    }

    private static void AppendParagraph(StringBuilder builder, Paragraph paragraph, MainDocumentPart main, HashSet<string> listStyles)
    {
        var text = ParagraphText(paragraph, main);
        var properties = paragraph.ParagraphProperties;
        var isListItem = properties?.NumberingProperties is not null
            || (properties?.ParagraphStyleId?.Val?.Value is { } styleId && listStyles.Contains(styleId));
        builder.AppendLine(isListItem && text.Length > 0 ? "- " + text : text);
    }

    /// <summary>Paragraph styles that apply list numbering themselves (e.g. Word's "List Bullet"), including inherited ones.</summary>
    private static HashSet<string> ListStyleIds(MainDocumentPart main)
    {
        var styles = main.StyleDefinitionsPart?.Styles?.Elements<Style>()
            .Where(s => s.StyleId?.Value is not null)
            .ToDictionary(s => s.StyleId!.Value!, StringComparer.Ordinal) ?? [];

        bool IsList(Style style, int depth) =>
            style.StyleParagraphProperties?.NumberingProperties is not null
            || (depth < 10 && style.BasedOn?.Val?.Value is { } parent && styles.TryGetValue(parent, out var parentStyle) && IsList(parentStyle, depth + 1));

        return styles.Values.Where(s => IsList(s, 0)).Select(s => s.StyleId!.Value!).ToHashSet(StringComparer.Ordinal);
    }

    private static string ParagraphText(Paragraph paragraph, MainDocumentPart main)
    {
        var builder = new StringBuilder();
        foreach (var element in paragraph.Descendants())
        {
            switch (element)
            {
                case Text text:
                    builder.Append(text.Text);
                    break;
                case TabChar:
                    builder.Append('\t');
                    break;
                case Break or CarriageReturn:
                    builder.Append('\n');
                    break;
            }
        }

        // Keep hyperlink targets (e.g. a "LinkedIn" label pointing to the profile URL).
        foreach (var hyperlink in paragraph.Descendants<Hyperlink>().Where(h => h.Id?.Value is not null))
        {
            var target = main.HyperlinkRelationships.FirstOrDefault(r => r.Id == hyperlink.Id!.Value)?.Uri.ToString();
            if (target is not null && !builder.ToString().Contains(target, StringComparison.OrdinalIgnoreCase))
            {
                builder.Append(" (").Append(target).Append(')');
            }
        }

        return builder.ToString();
    }

    private static string FromPlainText(byte[] content)
    {
        using var reader = new StreamReader(new MemoryStream(content), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static string Clean(string text)
    {
        var lines = text.ReplaceLineEndings("\n").Split('\n').Select(line => TrailingSpaceRegex().Replace(line, string.Empty));
        return ExtraBlankLinesRegex().Replace(string.Join("\n", lines), "\n\n").Trim();
    }

    [GeneratedRegex(@"[ \t]+$")]
    private static partial Regex TrailingSpaceRegex();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex ExtraBlankLinesRegex();
}
