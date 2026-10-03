namespace JobPilot.Domain.Tailoring;

/// <summary>
/// A piece of generated text and the master CV elements it comes from. Source keys are element ids (as strings) or
/// one of <see cref="SourceKeys"/>.
/// </summary>
public sealed class TraceableText
{
    public string Text { get; set; } = string.Empty;

    public List<string> Sources { get; set; } = [];

    /// <summary>The owner reviewed a flagged text and vouches for it (recorded, never set by the app).</summary>
    public bool OwnerConfirmed { get; set; }
}

public static class SourceKeys
{
    public const string Headline = "headline";
    public const string Summary = "summary";

    /// <summary>The owner's profile (stack, years of experience, emphases).</summary>
    public const string Profile = "profile";
}

/// <summary>A CV variant for one offer: master CV content reordered, rephrased and prioritised, never invented.</summary>
public sealed class TailoredCv
{
    public TraceableText Headline { get; set; } = new();

    public TraceableText Summary { get; set; } = new();

    /// <summary>Included experiences; company, title, dates and location always come from the master CV.</summary>
    public List<TailoredExperience> Experiences { get; set; } = [];

    /// <summary>Skills to show, in priority order; each must exist in the master CV.</summary>
    public List<string> Skills { get; set; } = [];

    public bool IncludeCertifications { get; set; } = true;

    public bool IncludeLanguages { get; set; } = true;

    public bool IncludeEducation { get; set; } = true;

    public IEnumerable<TraceableText> AllTexts() =>
        new[] { Headline, Summary }.Concat(Experiences.SelectMany(e => e.Bullets));
}

public sealed class TailoredExperience
{
    public Guid ExperienceId { get; set; }

    public List<TraceableText> Bullets { get; set; } = [];
}

/// <summary>Body of a cover letter; greeting and sign-off are composed by the app.</summary>
public sealed class CoverLetter
{
    public List<TraceableText> Paragraphs { get; set; } = [];
}
