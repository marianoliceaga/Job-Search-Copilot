namespace JobPilot.Domain.Cv;

/// <summary>
/// The master CV: the single source of truth for everything generated for applications. Every element has a
/// stable <see cref="Guid"/> so that generated content (phase 4) can be traced back to it.
/// </summary>
public sealed class MasterCv
{
    public CvContact Contact { get; set; } = new();

    public string? Headline { get; set; }

    public string? Summary { get; set; }

    public List<CvExperience> Experiences { get; set; } = [];

    public List<CvSkill> Skills { get; set; } = [];

    public List<CvCertification> Certifications { get; set; } = [];

    public List<CvLanguage> Languages { get; set; } = [];

    public List<CvEducation> Education { get; set; } = [];

    /// <summary>Gives an identifier to every element that has none (new elements added in the editor).</summary>
    public void EnsureIds()
    {
        foreach (var experience in Experiences)
        {
            experience.Id = NewIfEmpty(experience.Id);
            foreach (var achievement in experience.Achievements)
            {
                achievement.Id = NewIfEmpty(achievement.Id);
            }
        }

        Skills.ForEach(s => s.Id = NewIfEmpty(s.Id));
        Certifications.ForEach(c => c.Id = NewIfEmpty(c.Id));
        Languages.ForEach(l => l.Id = NewIfEmpty(l.Id));
        Education.ForEach(e => e.Id = NewIfEmpty(e.Id));
    }

    /// <summary>All element identifiers, for traceability checks.</summary>
    public IEnumerable<Guid> AllElementIds() =>
        Experiences.SelectMany(e => e.Achievements.Select(a => a.Id).Prepend(e.Id))
            .Concat(Skills.Select(s => s.Id))
            .Concat(Certifications.Select(c => c.Id))
            .Concat(Languages.Select(l => l.Id))
            .Concat(Education.Select(e => e.Id));

    /// <summary>Returns the problems that prevent saving (empty when valid). Messages are user-facing (Spanish).</summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();

        foreach (var (experience, index) in Experiences.Select((e, i) => (e, i + 1)))
        {
            var label = $"Experiencia {index}";
            if (string.IsNullOrWhiteSpace(experience.Company))
            {
                problems.Add($"{label}: falta la empresa.");
            }

            if (string.IsNullOrWhiteSpace(experience.Title))
            {
                problems.Add($"{label}: falta el puesto.");
            }

            if (experience.End is { } end && experience.Start is { } start && end < start)
            {
                problems.Add($"{label}: la fecha de fin ({end}) es anterior a la de inicio ({start}).");
            }

            if (experience.IsCurrent && experience.End is not null)
            {
                problems.Add($"{label}: un trabajo actual no puede tener fecha de fin.");
            }

            if (experience.Achievements.Any(a => string.IsNullOrWhiteSpace(a.Text)))
            {
                problems.Add($"{label}: hay logros vacíos.");
            }
        }

        problems.AddRange(Skills.Where(s => string.IsNullOrWhiteSpace(s.Name)).Select(_ => "Hay skills sin nombre."));
        problems.AddRange(Certifications.Where(c => string.IsNullOrWhiteSpace(c.Name)).Select(_ => "Hay certificaciones sin nombre."));
        problems.AddRange(Languages.Where(l => string.IsNullOrWhiteSpace(l.Name)).Select(_ => "Hay idiomas sin nombre."));
        problems.AddRange(Education.Where(e => string.IsNullOrWhiteSpace(e.Institution)).Select(_ => "Hay estudios sin institución."));

        foreach (var education in Education)
        {
            if (education.End is { } end && education.Start is { } start && end < start)
            {
                problems.Add($"{education.Institution}: la fecha de fin es anterior a la de inicio.");
            }
        }

        var ids = AllElementIds().Where(id => id != Guid.Empty).ToList();
        if (ids.Count != ids.Distinct().Count())
        {
            problems.Add("Hay elementos con identificadores duplicados.");
        }

        return problems.Distinct().ToList();
    }

    private static Guid NewIfEmpty(Guid id) => id == Guid.Empty ? Guid.NewGuid() : id;
}

public sealed class CvContact
{
    public string? FullName { get; set; }

    public string? Email { get; set; }

    public string? Phone { get; set; }

    public string? Location { get; set; }

    public string? LinkedInUrl { get; set; }

    public string? GitHubUrl { get; set; }

    public string? WebsiteUrl { get; set; }
}

public sealed class CvExperience
{
    public Guid Id { get; set; }

    public string Company { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string? Location { get; set; }

    public PartialDate? Start { get; set; }

    /// <summary>End date; <see langword="null"/> when <see cref="IsCurrent"/> or unknown.</summary>
    public PartialDate? End { get; set; }

    public bool IsCurrent { get; set; }

    public string? Description { get; set; }

    public List<CvAchievement> Achievements { get; set; } = [];

    public List<string> Technologies { get; set; } = [];
}

public sealed class CvAchievement
{
    public Guid Id { get; set; }

    public string Text { get; set; } = string.Empty;
}

public sealed class CvSkill
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Category { get; set; }
}

public sealed class CvCertification
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Issuer { get; set; }

    public PartialDate? Date { get; set; }

    public string? CredentialUrl { get; set; }
}

public sealed class CvLanguage
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Level { get; set; }
}

public sealed class CvEducation
{
    public Guid Id { get; set; }

    public string Institution { get; set; } = string.Empty;

    public string? Degree { get; set; }

    public string? Field { get; set; }

    public PartialDate? Start { get; set; }

    public PartialDate? End { get; set; }
}
