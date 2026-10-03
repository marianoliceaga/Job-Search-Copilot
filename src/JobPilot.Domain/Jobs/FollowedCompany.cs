namespace JobPilot.Domain.Jobs;

public enum AtsKind
{
    Greenhouse,
    Lever,
    Ashby,
}

/// <summary>A company whose public job board (ATS) is checked on every search.</summary>
public sealed class FollowedCompany
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public AtsKind Ats { get; set; }

    /// <summary>Board identifier in the ATS (case-sensitive in Lever).</summary>
    public required string BoardToken { get; set; }

    public string? Notes { get; set; }

    public DateTime AddedAtUtc { get; set; }
}
