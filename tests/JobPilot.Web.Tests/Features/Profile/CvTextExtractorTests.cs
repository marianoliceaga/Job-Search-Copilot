using JobPilot.Web.Features.Profile.Import;

namespace JobPilot.Web.Tests.Features.Profile;

public class CvTextExtractorTests
{
    private static readonly string FixturesDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Cv");

    // Content present in the three fictional fixtures.
    private static readonly string[] ExpectedFragments =
    [
        "Alex Rivera",
        "alex.rivera@example.com",
        "Córdoba, Argentina",
        "Northwind Bank",
        "Cut payment processing latency by 40% for 2,000,000 monthly transactions.",
        "Contoso Retail",
        "Fabrikam Logistics",
        "ASP.NET Core, Blazor, EF Core",
        "Microsoft Certified: Azure Developer Associate (2021)",
        "English: C1",
        "Example State University",
    ];

    [Theory]
    [InlineData("fictional-cv.md", "Markdown")]
    [InlineData("fictional-cv.docx", "DOCX")]
    [InlineData("fictional-cv.pdf", "PDF")]
    public void Extracts_the_full_text_of_each_format(string fileName, string format)
    {
        var result = CvTextExtractor.Extract(fileName, File.ReadAllBytes(Path.Combine(FixturesDirectory, fileName)));

        Assert.Null(result.Error);
        var document = Assert.IsType<ExtractedDocument>(result.Document);
        Assert.Equal(format, document.Format);
        Assert.Equal(fileName, document.FileName);
        Assert.All(ExpectedFragments, fragment => Assert.Contains(fragment, Normalize(document.Text), StringComparison.Ordinal));
    }

    [Fact]
    public void Docx_keeps_bullets_tables_and_hyperlink_targets()
    {
        var text = Extract("fictional-cv.docx");

        Assert.Contains("- Cut payment processing latency", text, StringComparison.Ordinal);
        Assert.Contains("Frameworks | ASP.NET Core, Blazor, EF Core", text, StringComparison.Ordinal);
        Assert.Contains("LinkedIn (https://www.linkedin.com/in/alex-rivera-example)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Pdf_keeps_hyperlink_targets()
    {
        Assert.Contains("- LinkedIn: https://www.linkedin.com/in/alex-rivera-example", Extract("fictional-cv.pdf"), StringComparison.Ordinal);
    }

    [Fact]
    public void Pdf_text_keeps_reading_order()
    {
        var text = Normalize(Extract("fictional-cv.pdf"));

        var positions = new[] { "Summary", "Northwind Bank", "Contoso Retail", "Fabrikam Logistics", "Skills", "Education" }
            .Select(fragment => text.IndexOf(fragment, StringComparison.Ordinal))
            .ToList();
        Assert.DoesNotContain(-1, positions);
        Assert.Equal(positions.Order(), positions);
    }

    [Theory]
    [InlineData("cv.exe", "Formato no soportado")]
    [InlineData("cv.doc", "Formato no soportado")]
    public void Rejects_unsupported_formats(string fileName, string error)
    {
        var result = CvTextExtractor.Extract(fileName, "some content that is long enough to be a document"u8.ToArray());

        Assert.Null(result.Document);
        Assert.StartsWith(error, result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_empty_and_corrupt_files()
    {
        Assert.Equal("El archivo está vacío.", CvTextExtractor.Extract("cv.pdf", []).Error);
        Assert.StartsWith("No se pudo leer el archivo", CvTextExtractor.Extract("cv.pdf", "not a pdf at all"u8.ToArray()).Error, StringComparison.Ordinal);
        Assert.StartsWith("No se pudo leer el archivo", CvTextExtractor.Extract("cv.docx", "not a zip"u8.ToArray()).Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_documents_without_text()
    {
        Assert.Equal("El documento casi no tiene texto.", CvTextExtractor.Extract("cv.md", "# Hi"u8.ToArray()).Error);
    }

    private static string Extract(string fileName) =>
        CvTextExtractor.Extract(fileName, File.ReadAllBytes(Path.Combine(FixturesDirectory, fileName))).Document!.Text;

    // PDF text may break lines differently; compare with collapsed whitespace.
    private static string Normalize(string text) => string.Join(' ', text.Split((char[])[' ', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries));
}
