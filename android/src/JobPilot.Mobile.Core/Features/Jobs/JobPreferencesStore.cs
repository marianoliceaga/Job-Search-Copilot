using JobPilot.Domain.Jobs;
using JobPilot.Mobile.Core.Infrastructure.Persistence;

namespace JobPilot.Mobile.Core.Features.Jobs;

/// <summary>
/// Search and ranking preferences. Until the owner saves them, the defaults apply (no keywords) and nothing is stored, so
/// preferences from an imported file are taken as they are.
/// </summary>
public sealed class JobPreferencesStore(SettingsDocumentStore documents)
{
    private const string Key = "job-search-preferences";

    public async Task<JobSearchPreferences> GetAsync(CancellationToken cancellationToken)
    {
        var preferences = await documents.GetAsync<JobSearchPreferences>(Key, cancellationToken);
        return Normalize(preferences ?? new JobSearchPreferences());
    }

    public Task SaveAsync(JobSearchPreferences preferences, CancellationToken cancellationToken) =>
        documents.SetAsync(Key, Normalize(preferences), cancellationToken);

    /// <summary>
    /// Searching needs at least one keyword: without one the filter keeps every offer, so a search would store hundreds of
    /// unrelated offers and spend the sources' request limits.
    /// </summary>
    public static bool CanSearch(JobSearchPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        return preferences.Keywords.Any(k => !string.IsNullOrWhiteSpace(k));
    }

    private static JobSearchPreferences Normalize(JobSearchPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        preferences.Keywords = Clean(preferences.Keywords);
        preferences.ExcludedKeywords = Clean(preferences.ExcludedKeywords);
        preferences.UsdPerUnit = preferences.UsdPerUnit
            .Where(rate => !string.IsNullOrWhiteSpace(rate.Key) && rate.Value > 0)
            .ToDictionary(rate => rate.Key.Trim().ToUpperInvariant(), rate => rate.Value, StringComparer.OrdinalIgnoreCase);
        preferences.UsdPerUnit["USD"] = 1m;
        preferences.MaxAgeDays = Math.Clamp(preferences.MaxAgeDays, 1, 365);
        return preferences;
    }

    private static List<string> Clean(List<string> values) =>
        values.Select(v => v.Trim()).Where(v => v.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}
