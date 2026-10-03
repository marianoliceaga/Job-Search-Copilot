using System.Text;
using JobPilot.Domain.Cv;
using ProfileEntity = JobPilot.Domain.Profiles.Profile;

namespace JobPilot.Web.Features.Jobs.Analysis;

/// <summary>Plain-text renderings of the owner's data sent to the LLM (shown in the payload preview).</summary>
public static class CandidateText
{
    public static string Profile(ProfileEntity profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var builder = new StringBuilder();
        builder.AppendLine($"Headline: {profile.Headline}");
        if (profile.YearsOfExperience is { } years)
        {
            builder.AppendLine($"Years of experience: {years}");
        }

        AppendList(builder, "Roles", profile.Roles);
        AppendList(builder, "Main stack", profile.Stack);
        AppendList(builder, "Business domains", profile.Domains);
        builder.AppendLine($"Works remotely from: {profile.WorkFromCountry ?? "not stated"}");
        builder.AppendLine($"Looking for: {profile.WorkModality ?? "not stated"}");
        if (!string.IsNullOrWhiteSpace(profile.Priorities))
        {
            builder.AppendLine($"Priorities: {profile.Priorities}");
        }

        return builder.ToString().Trim();
    }

    public static string Cv(MasterCv cv)
    {
        ArgumentNullException.ThrowIfNull(cv);
        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(cv.Headline))
        {
            builder.AppendLine(cv.Headline);
        }

        if (!string.IsNullOrWhiteSpace(cv.Summary))
        {
            builder.AppendLine(cv.Summary).AppendLine();
        }

        builder.AppendLine("EXPERIENCE");
        foreach (var experience in cv.Experiences)
        {
            var end = experience.IsCurrent ? "present" : experience.End?.ToString() ?? "?";
            builder.AppendLine($"- {experience.Title} at {experience.Company} ({experience.Start?.ToString() ?? "?"} – {end})");
            if (experience.Technologies.Count > 0)
            {
                builder.AppendLine($"  Technologies: {string.Join(", ", experience.Technologies)}");
            }

            if (!string.IsNullOrWhiteSpace(experience.Description))
            {
                builder.AppendLine($"  {experience.Description}");
            }

            foreach (var achievement in experience.Achievements)
            {
                builder.AppendLine($"  * {achievement.Text}");
            }
        }

        AppendList(builder, "\nSKILLS", cv.Skills.Select(s => s.Name));
        AppendList(builder, "CERTIFICATIONS", cv.Certifications.Select(c => c.Name));
        AppendList(builder, "LANGUAGES", cv.Languages.Select(l => l.Level is null ? l.Name : $"{l.Name} ({l.Level})"));
        AppendList(builder, "EDUCATION", cv.Education.Select(e => string.Join(", ", new[] { e.Degree, e.Field, e.Institution }.Where(s => !string.IsNullOrWhiteSpace(s)))));
        return builder.ToString().Trim();
    }

    private static void AppendList(StringBuilder builder, string label, IEnumerable<string> values)
    {
        var list = values.Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
        if (list.Count > 0)
        {
            builder.AppendLine($"{label}: {string.Join(", ", list)}");
        }
    }
}
