using System.Globalization;
using System.Text;
using JobPilot.Domain.Cv;
using JobPilot.Domain.Jobs;
using JobPilot.Domain.Tailoring;
using JobPilot.Web.Features.Jobs.Analysis;
using ProfileEntity = JobPilot.Domain.Profiles.Profile;

namespace JobPilot.Web.Features.Tailoring;

/// <summary>Everything needed to generate or check tailored content for one offer against one master CV version.</summary>
public sealed class TailoringContext
{
    public TailoringContext(JobPosting posting, MasterCv cv, int cvVersion, ProfileEntity profile, IEnumerable<string> technologyCatalog)
    {
        Posting = posting;
        Cv = cv;
        CvVersion = cvVersion;
        Profile = profile;
        ProfileText = BuildProfileText(profile);
        Index = new CvSourceIndex(cv, ProfileText);
        Validator = new TraceabilityValidator(Index, technologyCatalog);
    }

    public JobPosting Posting { get; }

    public MasterCv Cv { get; }

    public int CvVersion { get; }

    public ProfileEntity Profile { get; }

    public string ProfileText { get; }

    public CvSourceIndex Index { get; }

    public TraceabilityValidator Validator { get; }

    /// <summary>The master CV with the short references the model must cite.</summary>
    public string CvWithReferences()
    {
        var builder = new StringBuilder();
        builder.AppendLine($"HEAD: {Cv.Headline}");
        builder.AppendLine($"SUM: {Cv.Summary}");
        builder.AppendLine($"PROF: {ProfileText.ReplaceLineEndings(" | ")}");
        builder.AppendLine();
        foreach (var experience in Cv.Experiences)
        {
            var end = experience.IsCurrent ? "present" : experience.End?.ToString() ?? "?";
            builder.AppendLine($"{Index.RefOf(experience.Id.ToString())}: {experience.Title} — {experience.Company} ({experience.Start?.ToString() ?? "?"} – {end})");
            if (experience.Technologies.Count > 0)
            {
                builder.AppendLine($"  Technologies: {string.Join(", ", experience.Technologies)}");
            }

            if (!string.IsNullOrWhiteSpace(experience.Description))
            {
                builder.AppendLine($"  Description: {experience.Description}");
            }

            foreach (var achievement in experience.Achievements)
            {
                builder.AppendLine($"  {Index.RefOf(achievement.Id.ToString())}: {achievement.Text}");
            }
        }

        builder.AppendLine();
        builder.AppendLine($"SKILLS (names you may use): {string.Join(", ", Index.Vocabulary)}");
        return builder.ToString().Trim();
    }

    public string Emphases() =>
        Profile.ApplicationEmphases.Count == 0 ? "(none)" : string.Join("\n", Profile.ApplicationEmphases.Select(e => "- " + e));

    /// <summary>Maps the model's references to element keys; unknown references are kept so validation reports them.</summary>
    public TraceableText ToTraceable(TracedTextResponse response) => new()
    {
        Text = response.Text.Trim(),
        Sources = response.Sources.Select(r => Index.KeyOf(r) ?? r.Trim()).Distinct(StringComparer.Ordinal).ToList(),
    };

    public static string Describe(CvSourceIndex index, IEnumerable<string> sources) =>
        string.Join(", ", sources.Select(index.RefOf));

    private static string BuildProfileText(ProfileEntity profile)
    {
        var builder = new StringBuilder(CandidateText.Profile(profile));
        if (profile.ApplicationEmphases.Count > 0)
        {
            builder.AppendLine().Append(CultureInfo.InvariantCulture, $"Emphases: {string.Join("; ", profile.ApplicationEmphases)}");
        }

        return builder.ToString();
    }
}
