using JobPilot.Mobile.Core.Features.Tailoring.Export;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace JobPilot.Mobile.Tests.Features.Tailoring;

/// <summary>
/// The Android PDF writer lays out text itself (QuestPDF did it in the web app): wrapping, hanging bullets, page breaks,
/// margins and real spaces in the text.
/// </summary>
public class AndroidPdfWriterTests
{
    private const double Margin = 0.7 * 72;

    private static ResumeContent LongResume(int experiences) => new(
        "Alex Rivera",
        "Córdoba, Argentina · alex.rivera@example.com · https://www.linkedin.com/in/alex-rivera-example-with-a-very-long-profile-address-that-cannot-fit",
        "Senior .NET Engineer",
        "Backend engineer focused on payments, ledgers and Blazor front ends, with a long summary that has to wrap over several lines of the page to check that words are never cut in the middle.",
        Enumerable.Range(1, experiences).Select(i => new ResumeExperience(
            $"Role {i}", $"Company {i}", "Remote", "Mar 2019 – Present",
            [$"Achievement {i}.1 that is long enough to wrap onto a second line so the hanging indent of the bullet can be checked by the test.", $"Achievement {i}.2."])).ToList(),
        ["C#", "Blazor", "Azure"], [], ["Spanish (Native)"], ["B.Sc. in Computer Science, Example State University, 2004–2009"]);

    [Fact]
    public void Long_content_wraps_inside_the_margins_and_flows_onto_new_pages_in_order()
    {
        using var pdf = PdfDocument.Open(PdfWriter.Resume(LongResume(experiences: 14)));

        Assert.True(pdf.NumberOfPages >= 2);
        var text = string.Join(" ", pdf.GetPages().Select(p => ContentOrderTextExtractor.GetText(p)));
        var positions = Enumerable.Range(1, 14).Select(i => text.IndexOf($"Achievement {i}.2.", StringComparison.Ordinal)).ToList();
        Assert.DoesNotContain(-1, positions);
        Assert.Equal(positions.Order(), positions);
        Assert.Contains("EDUCATION", pdf.GetPages().Last().Text, StringComparison.Ordinal);

        Assert.All(pdf.GetPages(), page => Assert.All(page.Letters, letter =>
        {
            Assert.InRange(letter.BoundingBox.Left, Margin - 1, page.Width - Margin + 1);
            Assert.InRange(letter.BoundingBox.Right, Margin - 1, page.Width - Margin + 1);
            Assert.InRange(letter.BoundingBox.Bottom, Margin - 6, page.Height - Margin + 1);
        }));
    }

    [Fact]
    public void Words_keep_their_spaces_and_are_never_split_except_overlong_ones()
    {
        using var pdf = PdfDocument.Open(PdfWriter.Resume(LongResume(experiences: 1)));
        var words = pdf.GetPage(1).GetWords().Select(w => w.Text).ToList();

        Assert.Contains("hanging", words);
        Assert.Contains("middle.", words);
        Assert.Contains(" ", pdf.GetPage(1).Letters.Select(l => l.Value));
        // The overlong address is split by characters but nothing is lost.
        Assert.Contains("cannot-fit", string.Concat(pdf.GetPage(1).Letters.Select(l => l.Value)), StringComparison.Ordinal);
    }

    [Fact]
    public void Wrapped_bullet_lines_align_with_the_text_not_with_the_bullet()
    {
        using var pdf = PdfDocument.Open(PdfWriter.Resume(LongResume(experiences: 1)));
        var page = pdf.GetPage(1);
        var bullet = page.Letters.First(l => l.Value == "•");
        var firstWord = page.GetWords().First(w => w.Text == "Achievement");
        var lines = page.Letters.Where(l => l.Value != " ").GroupBy(l => Math.Round(l.StartBaseLine.Y)).ToList();
        var secondLine = lines.SkipWhile(g => g.All(l => l.StartBaseLine.Y != firstWord.Letters[0].StartBaseLine.Y)).Skip(1).First();

        Assert.True(firstWord.BoundingBox.Left > bullet.BoundingBox.Right);
        Assert.Equal(firstWord.BoundingBox.Left, secondLine.Min(l => l.BoundingBox.Left), 0.5);
    }
}
