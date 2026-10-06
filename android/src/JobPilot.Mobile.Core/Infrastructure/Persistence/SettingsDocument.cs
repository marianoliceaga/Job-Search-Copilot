namespace JobPilot.Mobile.Core.Infrastructure.Persistence;

/// <summary>A JSON document stored by key (preferences, rules, last reviews), as in the web app.</summary>
public sealed class SettingsDocument
{
    public required string Key { get; set; }

    public required string Json { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
