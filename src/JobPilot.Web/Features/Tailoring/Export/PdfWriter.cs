using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace JobPilot.Web.Features.Tailoring.Export;

/// <summary>
/// ATS-friendly PDF with QuestPDF (Community license): one column, real selectable text in reading order, the bundled
/// Lato font embedded (same output on every OS), no tables or images.
/// </summary>
public static class PdfWriter
{
    static PdfWriter()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        // Never depend on the fonts installed on the machine: always the font bundled with QuestPDF.
        QuestPDF.Settings.UseSystemFonts = false;
    }

    public static byte[] Resume(ResumeContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return Document.Create(document => document.Page(page =>
        {
            Setup(page);
            page.Content().Column(column =>
            {
                column.Spacing(4);
                column.Item().Text(content.Name).FontSize(20).Bold();
                column.Item().Text(content.ContactLine).FontSize(9.5f).FontColor(Colors.Grey.Darken2);
                if (content.Headline is not null)
                {
                    column.Item().PaddingTop(6).Text(content.Headline).FontSize(12).SemiBold();
                }

                if (content.Summary is not null)
                {
                    Heading(column, "SUMMARY");
                    column.Item().Text(content.Summary);
                }

                if (content.Experiences.Count > 0)
                {
                    Heading(column, "EXPERIENCE");
                    foreach (var experience in content.Experiences)
                    {
                        column.Item().PaddingTop(6).Text($"{experience.Title} — {experience.Company}").Bold();
                        column.Item().Text(string.Join(" · ", new[] { experience.Location, experience.Dates }.Where(s => !string.IsNullOrWhiteSpace(s))))
                            .FontSize(9.5f).FontColor(Colors.Grey.Darken2);
                        foreach (var bullet in experience.Bullets)
                        {
                            column.Item().PaddingLeft(10).Text(text =>
                            {
                                text.Span("• ");
                                text.Span(bullet);
                            });
                        }
                    }
                }

                Section(column, "SKILLS", content.Skills.Count == 0 ? [] : [string.Join(", ", content.Skills)]);
                Section(column, "CERTIFICATIONS", content.Certifications);
                Section(column, "LANGUAGES", content.Languages.Count == 0 ? [] : [string.Join(" · ", content.Languages)]);
                Section(column, "EDUCATION", content.Education);
            });
        })).GeneratePdf();
    }

    public static byte[] Letter(LetterContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return Document.Create(document => document.Page(page =>
        {
            Setup(page);
            page.Content().Column(column =>
            {
                column.Spacing(10);
                column.Item().Text(content.Name).FontSize(16).Bold();
                column.Item().Text(content.ContactLine).FontSize(9.5f).FontColor(Colors.Grey.Darken2);
                column.Item().PaddingTop(8).Text(content.Date);
                column.Item().Text(content.Greeting);
                foreach (var paragraph in content.Paragraphs)
                {
                    column.Item().Text(paragraph);
                }

                column.Item().Text(content.Closing);
                column.Item().Text(content.Name);
            });
        })).GeneratePdf();
    }

    private static void Setup(PageDescriptor page)
    {
        page.Size(PageSizes.Letter);
        page.Margin(0.7f, Unit.Inch);
        page.DefaultTextStyle(style => style.FontSize(10.5f).LineHeight(1.25f));
    }

    private static void Heading(ColumnDescriptor column, string title) =>
        column.Item().PaddingTop(10).Text(title).FontSize(11.5f).Bold();

    private static void Section(ColumnDescriptor column, string title, IReadOnlyList<string> lines)
    {
        if (lines.Count == 0)
        {
            return;
        }

        Heading(column, title);
        foreach (var line in lines)
        {
            column.Item().Text(line);
        }
    }
}
