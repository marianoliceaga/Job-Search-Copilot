namespace JobPilot.Domain.Profiles;

/// <summary>
/// The job seeker's profile and search preferences. Single row (single-user app); edited from the UI
/// (phase 2), never hardcoded.
/// </summary>
public sealed class Profile
{
    public int Id { get; set; }

    public string Headline { get; set; } = string.Empty;

    public int? YearsOfExperience { get; set; }

    public List<string> Roles { get; set; } = [];

    public List<string> Stack { get; set; } = [];

    public List<string> Domains { get; set; } = [];

    /// <summary>Country the owner works from, used for eligibility checks.</summary>
    public string? WorkFromCountry { get; set; }

    public string? WorkModality { get; set; }

    /// <summary>Free-text priorities, e.g. "compensation over role".</summary>
    public string? Priorities { get; set; }

    /// <summary>ISO 639-1 language used for applications.</summary>
    public string ApplicationLanguage { get; set; } = "en";

    /// <summary>Points to emphasise in applications when the offer justifies it.</summary>
    public List<string> ApplicationEmphases { get; set; } = [];

    public DateTime UpdatedAtUtc { get; set; }
}
