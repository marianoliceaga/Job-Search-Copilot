using System.IO.Compression;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using JobPilot.Domain.Cv;
using JobPilot.Domain.Jobs;
using JobPilot.Domain.Tailoring;
using JobPilot.Web.Features.Tailoring.Export;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace JobPilot.Web.Tests.Features.Tailoring;

public class ExportTests
{
    private static readonly DocumentExporter Exporter = new(TimeProvider.System);

    internal static (MasterCv Master, TailoredCv Tailored, JobPosting Posting) Sample()
    {
        var master = new MasterCv
        {
            Contact = new CvContact { FullName = "Alex Rivera", Email = "alex.rivera@example.com", Location = "Córdoba, Argentina", LinkedInUrl = "https://www.linkedin.com/in/alex-rivera-example" },
            Experiences =
            [
                new CvExperience { Company = "Northwind Bank", Title = "Tech Lead", Start = new PartialDate(2019, 3), IsCurrent = true, Location = "Remote" },
                new CvExperience { Company = "Contoso Retail", Title = "Senior Software Engineer", Start = new PartialDate(2014, 6), End = new PartialDate(2019, 2) },
            ],
            Certifications = [new CvCertification { Name = "Azure Developer Associate", Issuer = "Microsoft", Date = new PartialDate(2021) }],
            Languages = [new CvLanguage { Name = "Spanish", Level = "Native" }, new CvLanguage { Name = "English", Level = "C1" }],
            Education = [new CvEducation { Institution = "Example State University", Degree = "B.Sc.", Field = "Computer Science", Start = new PartialDate(2004), End = new PartialDate(2009) }],
        };
        master.EnsureIds();
        var tailored = new TailoredCv
        {
            Headline = new TraceableText { Text = "Senior .NET Engineer" },
            Summary = new TraceableText { Text = "Backend engineer focused on payments and Blazor." },
            Experiences =
            [
                new TailoredExperience { ExperienceId = master.Experiences[0].Id, Bullets = [new TraceableText { Text = "Cut payment latency by 40%." }, new TraceableText { Text = "Migrated the ledger to Azure." }] },
                new TailoredExperience { ExperienceId = master.Experiences[1].Id, Bullets = [new TraceableText { Text = "Built the order API used by 120 stores." }] },
            ],
            Skills = ["C#", "Blazor", "Azure"],
        };
        var posting = new JobPosting { SourceKey = "manual", SourceName = "m", Title = "Senior .NET Engineer", Company = "Contoso Cloud", Url = "u", UrlKey = "u", Fingerprint = "f" };
        return (master, tailored, posting);
    }

    [Fact]
    public void Docx_is_valid_single_column_text_without_tables_images_or_text_boxes()
    {
        var (master, tailored, posting) = Sample();

        var file = Exporter.ExportCv(master, tailored, posting, ExportFormat.Docx);

        Assert.Equal("Alex Rivera - CV - Contoso Cloud.docx", file.FileName);
        using var stream = new MemoryStream(file.Content);
        using (var document = WordprocessingDocument.Open(stream, false))
        {
            Assert.Empty(new OpenXmlValidator().Validate(document, TestContext.Current.CancellationToken));
            var body = document.MainDocumentPart!.Document!.Body!;
            Assert.Empty(body.Descendants<DocumentFormat.OpenXml.Wordprocessing.Table>());
            Assert.Empty(body.Descendants<DocumentFormat.OpenXml.Wordprocessing.Drawing>());
            Assert.Empty(body.Descendants<DocumentFormat.OpenXml.Wordprocessing.Picture>());
            Assert.Empty(body.Descendants<DocumentFormat.OpenXml.Wordprocessing.TextBoxContent>());
            Assert.Empty(document.MainDocumentPart.HeaderParts);
            Assert.Single(body.Descendants<DocumentFormat.OpenXml.Wordprocessing.SectionProperties>());
            Assert.Null(body.Descendants<DocumentFormat.OpenXml.Wordprocessing.Columns>().FirstOrDefault(c => c.ColumnCount?.Value > 1));

            var text = string.Join("\n", body.Elements<DocumentFormat.OpenXml.Wordprocessing.Paragraph>().Select(p => p.InnerText));
            AssertInOrder(text, "Alex Rivera", "Senior .NET Engineer", "SUMMARY", "EXPERIENCE", "Tech Lead — Northwind Bank", "Remote · Mar 2019 – Present",
                "Cut payment latency by 40%.", "Senior Software Engineer — Contoso Retail", "Jun 2014 – Feb 2019", "SKILLS", "C#, Blazor, Azure",
                "CERTIFICATIONS", "Azure Developer Associate, Microsoft, 2021", "LANGUAGES", "Spanish (Native) · English (C1)", "EDUCATION",
                "B.Sc. in Computer Science, Example State University, 2004–2009");
            var bullets = body.Descendants<DocumentFormat.OpenXml.Wordprocessing.NumberingProperties>().Count();
            Assert.Equal(3, bullets);
        }

        using var zip = new ZipArchive(new MemoryStream(file.Content));
        Assert.DoesNotContain(zip.Entries, e => e.FullName.StartsWith("word/media/", StringComparison.Ordinal));
    }

    [Fact]
    public void Pdf_text_is_extractable_in_reading_order_with_an_embedded_font()
    {
        var (master, tailored, posting) = Sample();

        var file = Exporter.ExportCv(master, tailored, posting, ExportFormat.Pdf);

        Assert.Equal("Alex Rivera - CV - Contoso Cloud.pdf", file.FileName);
        using var pdf = PdfDocument.Open(file.Content);
        var text = string.Join("\n", pdf.GetPages().Select(page => ContentOrderTextExtractor.GetText(page)));
        AssertInOrder(Collapse(text), "Alex Rivera", "Senior .NET Engineer", "SUMMARY", "EXPERIENCE", "Tech Lead — Northwind Bank",
            "• Cut payment latency by 40%.", "• Migrated the ledger to Azure.", "Senior Software Engineer — Contoso Retail", "SKILLS", "C#, Blazor, Azure", "EDUCATION");
        Assert.All(pdf.GetPages().SelectMany(p => p.Letters), letter => Assert.Contains("Lato", letter.FontName, StringComparison.OrdinalIgnoreCase));
        Assert.Empty(pdf.GetPages().SelectMany(p => p.GetImages()));
    }

    [Fact]
    public void Excluded_sections_are_not_exported()
    {
        var (master, tailored, posting) = Sample();
        tailored.IncludeCertifications = false;
        tailored.IncludeLanguages = false;

        var file = Exporter.ExportCv(master, tailored, posting, ExportFormat.Docx);

        using var document = WordprocessingDocument.Open(new MemoryStream(file.Content), false);
        var text = document.MainDocumentPart!.Document!.Body!.InnerText;
        Assert.DoesNotContain("CERTIFICATIONS", text, StringComparison.Ordinal);
        Assert.DoesNotContain("LANGUAGES", text, StringComparison.Ordinal);
        Assert.Contains("EDUCATION", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Letters_get_greeting_and_sign_off_from_the_app()
    {
        var (master, _, posting) = Sample();
        var letter = new CoverLetter { Paragraphs = [new TraceableText { Text = "First paragraph." }, new TraceableText { Text = "Second paragraph." }] };

        var docx = Exporter.ExportLetter(master, letter, posting, ExportFormat.Docx);
        var pdf = Exporter.ExportLetter(master, letter, posting, ExportFormat.Pdf);

        using var document = WordprocessingDocument.Open(new MemoryStream(docx.Content), false);
        Assert.Empty(new OpenXmlValidator().Validate(document, TestContext.Current.CancellationToken));
        var docxText = string.Join("\n", document.MainDocumentPart!.Document!.Body!.Elements<DocumentFormat.OpenXml.Wordprocessing.Paragraph>().Select(p => p.InnerText));
        AssertInOrder(docxText, "Alex Rivera", "Dear Contoso Cloud hiring team,", "First paragraph.", "Second paragraph.", "Sincerely,");
        using var reader = PdfDocument.Open(pdf.Content);
        AssertInOrder(Collapse(string.Join("\n", reader.GetPages().Select(p => ContentOrderTextExtractor.GetText(p)))), "Dear Contoso Cloud hiring team,", "First paragraph.", "Sincerely,");
        Assert.Equal("Alex Rivera - Cover letter - Contoso Cloud.pdf", pdf.FileName);
    }

    private static void AssertInOrder(string text, params string[] fragments)
    {
        var position = 0;
        foreach (var fragment in fragments)
        {
            var index = text.IndexOf(fragment, position, StringComparison.Ordinal);
            Assert.True(index >= 0, $"\"{fragment}\" not found after position {position} in:\n{text}");
            position = index + fragment.Length;
        }
    }

    private static string Collapse(string text) => string.Join(' ', text.Split((char[])[' ', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries));
}
