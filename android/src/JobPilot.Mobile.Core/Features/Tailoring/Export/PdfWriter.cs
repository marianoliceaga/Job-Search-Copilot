using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

namespace JobPilot.Mobile.Core.Features.Tailoring.Export;

/// <summary>
/// ATS-friendly PDF with PDFsharp (MIT, pure .NET: QuestPDF, used by the web app, has no Android binaries). Same layout
/// rules as the web app's PDF: US Letter, 0.7" margins, one column, the bundled Lato font embedded (never the phone's
/// fonts), no tables or images. Each line is drawn as one string, so the text keeps its real spaces and reading order.
/// </summary>
public static class PdfWriter
{
    private const string Font = "Lato";
    private const double Margin = 0.7 * 72;
    private const double LineHeight = 1.25;
    private static readonly XColor Black = XColors.Black;
    private static readonly XColor Grey = XColor.FromArgb(0x61, 0x61, 0x61);

    static PdfWriter()
    {
        // PDFsharp's platform-independent build has no access to system fonts: it asks this resolver for every font.
        GlobalFontSettings.FontResolver ??= new LatoFontResolver();
    }

    public static byte[] Resume(ResumeContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        using var writer = new Writer();
        writer.Text(content.Name, 20, bold: true);
        writer.Text(content.ContactLine, 9.5, grey: true);
        if (content.Headline is not null)
        {
            writer.Text(content.Headline, 12, bold: true, spaceBefore: 6);
        }

        if (content.Summary is not null)
        {
            writer.Heading("SUMMARY");
            writer.Text(content.Summary);
        }

        if (content.Experiences.Count > 0)
        {
            writer.Heading("EXPERIENCE");
            foreach (var experience in content.Experiences)
            {
                writer.Text($"{experience.Title} — {experience.Company}", bold: true, spaceBefore: 6);
                writer.Text(string.Join(" · ", new[] { experience.Location, experience.Dates }.Where(s => !string.IsNullOrWhiteSpace(s))), 9.5, grey: true);
                foreach (var bullet in experience.Bullets)
                {
                    writer.Bullet(bullet);
                }
            }
        }

        Section(writer, "SKILLS", content.Skills.Count == 0 ? [] : [string.Join(", ", content.Skills)]);
        Section(writer, "CERTIFICATIONS", content.Certifications);
        Section(writer, "LANGUAGES", content.Languages.Count == 0 ? [] : [string.Join(" · ", content.Languages)]);
        Section(writer, "EDUCATION", content.Education);
        return writer.Save();
    }

    public static byte[] Letter(LetterContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        using var writer = new Writer(spaceAfter: 10);
        writer.Text(content.Name, 16, bold: true);
        writer.Text(content.ContactLine, 9.5, grey: true);
        writer.Text(content.Date, spaceBefore: 8);
        writer.Text(content.Greeting);
        foreach (var paragraph in content.Paragraphs)
        {
            writer.Text(paragraph);
        }

        writer.Text(content.Closing);
        writer.Text(content.Name);
        return writer.Save();
    }

    private static void Section(Writer writer, string title, IReadOnlyList<string> lines)
    {
        if (lines.Count == 0)
        {
            return;
        }

        writer.Heading(title);
        foreach (var line in lines)
        {
            writer.Text(line);
        }
    }

    /// <summary>Lays out paragraphs top to bottom, wrapping by words and adding pages as needed.</summary>
    private sealed class Writer(double spaceAfter = 4) : IDisposable
    {
        private const double BulletIndent = 10;
        private const double BulletTextIndent = 20;

        private readonly PdfDocument _document = new();
        private XGraphics? _graphics;
        private double _y;
        private double _width;
        private double _bottom;

        public void Heading(string title) => Text(title, 11.5, bold: true, spaceBefore: 10);

        public void Text(string text, double size = 10.5, bool bold = false, bool grey = false, double spaceBefore = 0) =>
            Paragraph(text, size, bold, grey, spaceBefore, indent: 0, bullet: false);

        /// <summary>Hanging indent: wrapped lines align with the text, not with the bullet.</summary>
        public void Bullet(string text) => Paragraph(text, 10.5, bold: false, grey: false, spaceBefore: 0, indent: BulletTextIndent, bullet: true);

        public byte[] Save()
        {
            EnsurePage();
            _graphics?.Dispose();
            _graphics = null;
            using var stream = new MemoryStream();
            _document.Save(stream, closeStream: false);
            return stream.ToArray();
        }

        public void Dispose()
        {
            _graphics?.Dispose();
            _document.Dispose();
        }

        private void Paragraph(string text, double size, bool bold, bool grey, double spaceBefore, double indent, bool bullet)
        {
            EnsurePage();
            var font = new XFont(Font, size, bold ? XFontStyleEx.Bold : XFontStyleEx.Regular, XPdfFontOptions.UnicodeDefault);
            var brush = new XSolidBrush(grey ? Grey : Black);
            var lineHeight = size * LineHeight;
            var lines = Wrap(text, font, _width - indent);

            _y += spaceBefore;
            for (var i = 0; i < lines.Count; i++)
            {
                if (_y + lineHeight > _bottom)
                {
                    NewPage();
                }

                // Baseline inside the line box, as in a CSS line-height: half the extra leading above the ascent.
                var baseline = _y + ((lineHeight - size) / 2) + (size * 0.8);
                if (bullet && i == 0)
                {
                    _graphics!.DrawString("•", font, brush, Margin + BulletIndent, baseline);
                }

                _graphics!.DrawString(lines[i], font, brush, Margin + indent, baseline);
                _y += lineHeight;
            }

            _y += spaceAfter;
        }

        private List<string> Wrap(string text, XFont font, double width)
        {
            var lines = new List<string>();
            foreach (var paragraph in text.ReplaceLineEndings("\n").Split('\n'))
            {
                var line = string.Empty;
                foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    var candidate = line.Length == 0 ? word : line + " " + word;
                    if (Measure(candidate, font) <= width)
                    {
                        line = candidate;
                        continue;
                    }

                    if (line.Length > 0)
                    {
                        lines.Add(line);
                    }

                    line = word;
                    // A single word wider than the line (a long URL) is split by characters.
                    while (Measure(line, font) > width && line.Length > 1)
                    {
                        var fit = line.Length - 1;
                        while (fit > 1 && Measure(line[..fit], font) > width)
                        {
                            fit--;
                        }

                        lines.Add(line[..fit]);
                        line = line[fit..];
                    }
                }

                lines.Add(line);
            }

            return lines;
        }

        private double Measure(string text, XFont font) => _graphics!.MeasureString(text, font).Width;

        private void EnsurePage()
        {
            if (_graphics is null)
            {
                NewPage();
            }
        }

        private void NewPage()
        {
            _graphics?.Dispose();
            var page = _document.AddPage();
            page.Width = XUnit.FromPoint(612);
            page.Height = XUnit.FromPoint(792);
            _graphics = XGraphics.FromPdfPage(page);
            _width = page.Width.Point - (2 * Margin);
            _bottom = page.Height.Point - Margin;
            _y = Margin;
        }
    }

    /// <summary>Serves the embedded Lato faces; italic is simulated from the regular face.</summary>
    private sealed class LatoFontResolver : IFontResolver
    {
        private const string Regular = "Lato-Regular";
        private const string Bold = "Lato-Bold";

        public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic) =>
            new(bold ? Bold : Regular, mustSimulateBold: false, mustSimulateItalic: italic);

        public byte[]? GetFont(string faceName)
        {
            using var stream = typeof(PdfWriter).Assembly.GetManifestResourceStream($"Fonts/{faceName}.ttf")
                ?? throw new InvalidOperationException($"Embedded font '{faceName}' not found.");
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }
    }
}
