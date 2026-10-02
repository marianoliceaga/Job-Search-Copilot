using JobPilot.Domain.Cv;
using JobPilot.Domain.Prompts;
using JobPilot.Web.Infrastructure.Llm;

namespace JobPilot.Web.Features.Profile.Import;

public sealed record PreparedCvImport(ExtractedDocument Document, RenderedPrompt Prompt);

public sealed record CvImportResult(LlmResult<ImportedCv> Llm, MasterCv? Cv, IReadOnlyList<CvImportFlag> Flags);

/// <summary>
/// Turns the text extracted from a CV into a <see cref="MasterCv"/> draft via the LLM, then flags every value that
/// cannot be found in the source text. The draft is only saved after the owner reviews it.
/// </summary>
public sealed class CvImportHandler(PromptLibrary prompts, ILlmClient llm)
{
    public PreparedCvImport Prepare(ExtractedDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var prompt = prompts.Get(LlmUseCases.CvImport).Render(new Dictionary<string, string> { ["cv_text"] = document.Text });
        return new PreparedCvImport(document, prompt);
    }

    public async Task<CvImportResult> RunAsync(PreparedCvImport import, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(import);
        var result = await llm.GenerateAsync(new LlmRequest<ImportedCv>(import.Prompt) { Validate = Validate }, cancellationToken);
        if (!result.IsSuccess)
        {
            return new CvImportResult(result, null, []);
        }

        var cv = ToMasterCv(result.Value!);
        return new CvImportResult(result, cv, CvImportVerifier.Verify(cv, import.Document.Text));
    }

    internal static IReadOnlyList<string> Validate(ImportedCv cv)
    {
        var problems = new List<string>();

        void CheckDate(string? value, string path)
        {
            if (value is not null && !PartialDate.TryParse(value, out _))
            {
                problems.Add($"{path} \"{value}\" must be \"YYYY-MM\", \"YYYY\" or null");
            }
        }

        for (var i = 0; i < cv.Experiences.Count; i++)
        {
            var experience = cv.Experiences[i];
            var path = $"experiences[{i}]";
            if (string.IsNullOrWhiteSpace(experience.Company))
            {
                problems.Add($"{path}.company is empty");
            }

            if (string.IsNullOrWhiteSpace(experience.Title))
            {
                problems.Add($"{path}.title is empty");
            }

            CheckDate(experience.Start, $"{path}.start");
            CheckDate(experience.End, $"{path}.end");
            if (experience.IsCurrent && experience.End is not null)
            {
                problems.Add($"{path}: isCurrent is true, so end must be null");
            }

            if (PartialDate.TryParse(experience.Start, out var start) && PartialDate.TryParse(experience.End, out var end) && end < start)
            {
                problems.Add($"{path}.end is before start");
            }
        }

        for (var i = 0; i < cv.Certifications.Count; i++)
        {
            CheckDate(cv.Certifications[i].Date, $"certifications[{i}].date");
        }

        for (var i = 0; i < cv.Education.Count; i++)
        {
            CheckDate(cv.Education[i].Start, $"education[{i}].start");
            CheckDate(cv.Education[i].End, $"education[{i}].end");
        }

        return problems;
    }

    internal static MasterCv ToMasterCv(ImportedCv imported)
    {
        var cv = new MasterCv
        {
            Contact = new CvContact
            {
                FullName = Clean(imported.Contact.FullName),
                Email = Clean(imported.Contact.Email),
                Phone = Clean(imported.Contact.Phone),
                Location = Clean(imported.Contact.Location),
                LinkedInUrl = Clean(imported.Contact.LinkedInUrl),
                GitHubUrl = Clean(imported.Contact.GitHubUrl),
                WebsiteUrl = Clean(imported.Contact.WebsiteUrl),
            },
            Headline = Clean(imported.Headline),
            Summary = Clean(imported.Summary),
            Experiences = imported.Experiences.Select(e => new CvExperience
            {
                Company = e.Company.Trim(),
                Title = e.Title.Trim(),
                Location = Clean(e.Location),
                Start = Date(e.Start),
                End = Date(e.End),
                IsCurrent = e.IsCurrent,
                Description = Clean(e.Description),
                Achievements = e.Achievements.Select(Clean).OfType<string>().Select(text => new CvAchievement { Text = text }).ToList(),
                Technologies = Distinct(e.Technologies),
            }).ToList(),
            Skills = imported.Skills.Where(s => !string.IsNullOrWhiteSpace(s.Name))
                .DistinctBy(s => s.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(s => new CvSkill { Name = s.Name.Trim(), Category = Clean(s.Category) }).ToList(),
            Certifications = imported.Certifications.Select(c => new CvCertification
            {
                Name = c.Name.Trim(),
                Issuer = Clean(c.Issuer),
                Date = Date(c.Date),
                CredentialUrl = Clean(c.CredentialUrl),
            }).ToList(),
            Languages = imported.Languages.Select(l => new CvLanguage { Name = l.Name.Trim(), Level = Clean(l.Level) }).ToList(),
            Education = imported.Education.Select(e => new CvEducation
            {
                Institution = e.Institution.Trim(),
                Degree = Clean(e.Degree),
                Field = Clean(e.Field),
                Start = Date(e.Start),
                End = Date(e.End),
            }).ToList(),
        };
        cv.EnsureIds();
        return cv;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static PartialDate? Date(string? value) => PartialDate.TryParse(value, out var date) ? date : null;

    private static List<string> Distinct(IEnumerable<string> values) =>
        values.Select(v => v.Trim()).Where(v => v.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}
