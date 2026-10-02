using System.Globalization;
using JobPilot.Domain.Cv;
using JobPilot.Domain.Jobs;
using JobPilot.Domain.Tailoring;

namespace JobPilot.Web.Features.Tailoring.Export;

/// <summary>Format-independent content of an exported CV (rendered to DOCX and PDF the same way).</summary>
public sealed record ResumeContent(
    string Name,
    string ContactLine,
    string? Headline,
    string? Summary,
    IReadOnlyList<ResumeExperience> Experiences,
    IReadOnlyList<string> Skills,
    IReadOnlyList<string> Certifications,
    IReadOnlyList<string> Languages,
    IReadOnlyList<string> Education);

public sealed record ResumeExperience(string Title, string Company, string? Location, string Dates, IReadOnlyList<string> Bullets);

public sealed record LetterContent(string Name, string ContactLine, string Date, string Greeting, IReadOnlyList<string> Paragraphs, string Closing);

public static class DocumentContentBuilder
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    public static ResumeContent Resume(MasterCv master, TailoredCv tailored)
    {
        ArgumentNullException.ThrowIfNull(master);
        ArgumentNullException.ThrowIfNull(tailored);
        var experiences = tailored.Experiences
            .Select(t => (Tailored: t, Master: master.Experiences.FirstOrDefault(e => e.Id == t.ExperienceId)))
            .Where(x => x.Master is not null)
            .Select(x => new ResumeExperience(x.Master!.Title, x.Master.Company, x.Master.Location, Dates(x.Master.Start, x.Master.End, x.Master.IsCurrent),
                x.Tailored.Bullets.Select(b => b.Text).Where(t => !string.IsNullOrWhiteSpace(t)).ToList()))
            .ToList();

        return new ResumeContent(
            Name(master),
            ContactLine(master.Contact),
            NullIfEmpty(tailored.Headline.Text),
            NullIfEmpty(tailored.Summary.Text),
            experiences,
            tailored.Skills,
            tailored.IncludeCertifications
                ? master.Certifications.Select(c => Join(", ", c.Name, c.Issuer, c.Date?.Year.ToString(CultureInfo.InvariantCulture))).ToList()
                : [],
            tailored.IncludeLanguages ? master.Languages.Select(l => l.Level is null ? l.Name : $"{l.Name} ({l.Level})").ToList() : [],
            tailored.IncludeEducation
                ? master.Education.Select(e => Join(", ", Join(" in ", e.Degree, e.Field), e.Institution, YearRange(e.Start, e.End))).ToList()
                : []);
    }

    public static LetterContent Letter(MasterCv master, JobPosting posting, CoverLetter letter, DateTime today)
    {
        ArgumentNullException.ThrowIfNull(master);
        ArgumentNullException.ThrowIfNull(posting);
        ArgumentNullException.ThrowIfNull(letter);
        var name = Name(master);
        return new LetterContent(
            name,
            ContactLine(master.Contact),
            today.ToString("MMMM d, yyyy", English),
            $"Dear {posting.Company} hiring team,",
            letter.Paragraphs.Select(p => p.Text).Where(t => !string.IsNullOrWhiteSpace(t)).ToList(),
            "Sincerely,");
    }

    public static string FileName(string name, string kind, string company, string extension)
    {
        var raw = $"{name} - {kind} - {company}";
        var safe = new string(raw.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c is '/' or '\\' or ':' ? '-' : c).ToArray()).Trim();
        return $"{(safe.Length > 120 ? safe[..120] : safe)}.{extension}";
    }

    private static string Name(MasterCv master) => string.IsNullOrWhiteSpace(master.Contact.FullName) ? "Candidate" : master.Contact.FullName.Trim();

    private static string ContactLine(CvContact contact) =>
        string.Join(" · ", new[] { contact.Location, contact.Email, contact.Phone, contact.LinkedInUrl, contact.GitHubUrl, contact.WebsiteUrl }
            .Where(s => !string.IsNullOrWhiteSpace(s)));

    private static string Dates(PartialDate? start, PartialDate? end, bool isCurrent) =>
        $"{Format(start)} – {(isCurrent ? "Present" : Format(end))}".Trim(' ', '–');

    private static string? YearRange(PartialDate? start, PartialDate? end) =>
        start is null && end is null ? null : $"{start?.Year.ToString(CultureInfo.InvariantCulture)}–{end?.Year.ToString(CultureInfo.InvariantCulture)}".Trim('–');

    private static string Format(PartialDate? date) => date switch
    {
        null => string.Empty,
        { Month: { } month } d => new DateTime(d.Year, month, 1).ToString("MMM yyyy", English),
        { } d => d.Year.ToString(CultureInfo.InvariantCulture),
    };

    private static string Join(string separator, params string?[] parts) =>
        string.Join(separator, parts.Where(p => !string.IsNullOrWhiteSpace(p)));

    private static string? NullIfEmpty(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
