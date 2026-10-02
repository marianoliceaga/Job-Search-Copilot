namespace JobPilot.Domain.Jobs;

/// <summary>Owner preferences for searching and ranking offers (edited in the UI, seeded from a file).</summary>
public sealed class JobSearchPreferences
{
    /// <summary>An offer is kept when it contains at least one of these (title, tags or description).</summary>
    public List<string> Keywords { get; set; } = [];

    /// <summary>An offer is dropped when it contains any of these.</summary>
    public List<string> ExcludedKeywords { get; set; } = [];

    /// <summary>Source keys turned off by the owner.</summary>
    public List<string> DisabledSources { get; set; } = [];

    public RankingWeights Weights { get; set; } = new();

    public CompensationTargets Compensation { get; set; } = new();

    /// <summary>USD per unit of each currency, provided by the owner (e.g. EUR → 1.08). USD is always 1.</summary>
    public Dictionary<string, decimal> UsdPerUnit { get; set; } = new(StringComparer.OrdinalIgnoreCase) { ["USD"] = 1m };

    /// <summary>Offers older than this are not imported.</summary>
    public int MaxAgeDays { get; set; } = 30;
}
