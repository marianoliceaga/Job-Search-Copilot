using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace JobPilot.Web.Features.Tailoring.Export;

/// <summary>
/// ATS-friendly DOCX: one column, real paragraphs and list bullets, standard section headings; no tables, text boxes,
/// images, headers or footers (ATS parsers often skip them).
/// </summary>
public static class DocxWriter
{
    private const string BulletNumberingId = "1";

    public static byte[] Resume(ResumeContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return Build(body =>
        {
            body.Append(Para(content.Name, "Title"));
            body.Append(Para(content.ContactLine, "Contact"));
            if (content.Headline is not null)
            {
                body.Append(Para(content.Headline, "Subtitle"));
            }

            if (content.Summary is not null)
            {
                body.Append(Para("SUMMARY", "Heading1"));
                body.Append(Para(content.Summary));
            }

            if (content.Experiences.Count > 0)
            {
                body.Append(Para("EXPERIENCE", "Heading1"));
                foreach (var experience in content.Experiences)
                {
                    body.Append(Para($"{experience.Title} — {experience.Company}", "Heading2"));
                    body.Append(Para(string.Join(" · ", new[] { experience.Location, experience.Dates }.Where(s => !string.IsNullOrWhiteSpace(s))), "Meta"));
                    foreach (var bullet in experience.Bullets)
                    {
                        body.Append(Bullet(bullet));
                    }
                }
            }

            Section(body, "SKILLS", content.Skills.Count == 0 ? [] : [string.Join(", ", content.Skills)]);
            Section(body, "CERTIFICATIONS", content.Certifications);
            Section(body, "LANGUAGES", content.Languages.Count == 0 ? [] : [string.Join(" · ", content.Languages)]);
            Section(body, "EDUCATION", content.Education);
        });
    }

    public static byte[] Letter(LetterContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return Build(body =>
        {
            body.Append(Para(content.Name, "Title"));
            body.Append(Para(content.ContactLine, "Contact"));
            body.Append(Para(content.Date));
            body.Append(Para(content.Greeting));
            foreach (var paragraph in content.Paragraphs)
            {
                body.Append(Para(paragraph));
            }

            body.Append(Para(content.Closing));
            body.Append(Para(content.Name));
        });
    }

    private static void Section(Body body, string title, IReadOnlyList<string> lines)
    {
        if (lines.Count == 0)
        {
            return;
        }

        body.Append(Para(title, "Heading1"));
        foreach (var line in lines)
        {
            body.Append(Para(line));
        }
    }

    private static byte[] Build(Action<Body> fill)
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = document.AddMainDocumentPart();
            AddStyles(main);
            AddNumbering(main);
            var body = new Body();
            fill(body);
            body.Append(new SectionProperties(
                new PageSize { Width = 12240, Height = 15840 },
                new PageMargin { Top = 1008, Bottom = 1008, Left = 1080, Right = 1080, Header = 720, Footer = 720, Gutter = 0 }));
            main.Document = new Document(body);
            document.PackageProperties.Title = "Curriculum";
            document.PackageProperties.Creator = "JobSearch Copilot";
        }

        return stream.ToArray();
    }

    private static Paragraph Para(string text, string? style = null)
    {
        var paragraph = new Paragraph(new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve }));
        if (style is not null)
        {
            paragraph.PrependChild(new ParagraphProperties(new ParagraphStyleId { Val = style }));
        }

        return paragraph;
    }

    private static Paragraph Bullet(string text) =>
        new(
            new ParagraphProperties(
                new ParagraphStyleId { Val = "ListBullet" },
                new NumberingProperties(new NumberingLevelReference { Val = 0 }, new NumberingId { Val = int.Parse(BulletNumberingId, System.Globalization.CultureInfo.InvariantCulture) })),
            new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve }));

    private static void AddStyles(MainDocumentPart main)
    {
        static Style ParagraphStyle(string id, string name, int halfPoints, bool bold = false, int spaceBefore = 0, int spaceAfter = 80, string? color = null)
        {
            var run = new StyleRunProperties();
            if (bold)
            {
                run.Append(new Bold());
            }

            if (color is not null)
            {
                run.Append(new Color { Val = color });
            }

            run.Append(new FontSize { Val = halfPoints.ToString(System.Globalization.CultureInfo.InvariantCulture) });
            return new Style(
                new StyleName { Val = name },
                new BasedOn { Val = "Normal" },
                new PrimaryStyle(),
                new StyleParagraphProperties(new SpacingBetweenLines
                {
                    Before = spaceBefore.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    After = spaceAfter.ToString(System.Globalization.CultureInfo.InvariantCulture),
                }),
                run)
            { Type = StyleValues.Paragraph, StyleId = id };
        }

        var styles = new Styles(
            new DocDefaults(
                new RunPropertiesDefault(new RunPropertiesBaseStyle(
                    new RunFonts { Ascii = "Calibri", HighAnsi = "Calibri", ComplexScript = "Calibri", EastAsia = "Calibri" },
                    new FontSize { Val = "21" },
                    new Languages { Val = "en-US" })),
                new ParagraphPropertiesDefault(new ParagraphPropertiesBaseStyle(new SpacingBetweenLines { After = "80", Line = "264", LineRule = LineSpacingRuleValues.Auto }))),
            new Style(new StyleName { Val = "Normal" }, new PrimaryStyle()) { Type = StyleValues.Paragraph, StyleId = "Normal", Default = true },
            ParagraphStyle("Title", "Title", 36, bold: true, spaceAfter: 40),
            ParagraphStyle("Contact", "Contact", 19, spaceAfter: 160, color: "444444"),
            ParagraphStyle("Subtitle", "Subtitle", 23, bold: true, spaceAfter: 120),
            ParagraphStyle("Heading1", "heading 1", 23, bold: true, spaceBefore: 200, spaceAfter: 80),
            ParagraphStyle("Heading2", "heading 2", 21, bold: true, spaceBefore: 120, spaceAfter: 0),
            ParagraphStyle("Meta", "Meta", 19, spaceAfter: 60, color: "555555"),
            ParagraphStyle("ListBullet", "List Bullet", 21, spaceAfter: 40));

        var part = main.AddNewPart<StyleDefinitionsPart>();
        part.Styles = styles;
    }

    private static void AddNumbering(MainDocumentPart main)
    {
        var abstractNumber = new AbstractNum(
            new Level(
                new StartNumberingValue { Val = 1 },
                new NumberingFormat { Val = NumberFormatValues.Bullet },
                new LevelText { Val = "•" },
                new LevelJustification { Val = LevelJustificationValues.Left },
                new PreviousParagraphProperties(new Indentation { Left = "360", Hanging = "260" }))
            { LevelIndex = 0 })
        { AbstractNumberId = 1 };

        var part = main.AddNewPart<NumberingDefinitionsPart>();
        part.Numbering = new Numbering(abstractNumber, new NumberingInstance(new AbstractNumId { Val = 1 }) { NumberID = 1 });
    }
}
