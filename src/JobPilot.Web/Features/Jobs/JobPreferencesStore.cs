using System.Text.Json;
using JobPilot.Domain.Jobs;
using JobPilot.Web.Infrastructure.Persistence;

namespace JobPilot.Web.Features.Jobs;

/// <summary>Search and ranking preferences; created on first use from <c>Seed/job-search.seed.json</c>.</summary>
public sealed class JobPreferencesStore(SettingsDocumentStore documents, JobSeedFiles seeds)
{
    private const string Key = "job-search-preferences";

    public async Task<JobSearchPreferences> GetAsync(CancellationToken cancellationToken)
    {
        var preferences = await documents.GetAsync<JobSearchPreferences>(Key, cancellationToken);
        if (preferences is null)
        {
            preferences = seeds.LoadPreferences();
            await SaveAsync(preferences, cancellationToken);
        }

        return Normalize(preferences);
    }

    public Task SaveAsync(JobSearchPreferences preferences, CancellationToken cancellationToken) =>
        documents.SetAsync(Key, Normalize(preferences), cancellationToken);

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

/// <summary>Seed files with initial preferences and followed companies (data, not code).</summary>
public sealed class JobSeedFiles(string directory)
{
    public JobSearchPreferences LoadPreferences()
    {
        var path = Path.Combine(directory, "job-search.seed.json");
        return File.Exists(path)
            ? JsonSerializer.Deserialize<JobSearchPreferences>(File.ReadAllText(path), DomainJson.Options) ?? new JobSearchPreferences()
            : new JobSearchPreferences();
    }

    public IReadOnlyList<FollowedCompany> LoadFollowedCompanies()
    {
        var path = Path.Combine(directory, "followed-companies.seed.json");
        return File.Exists(path)
            ? JsonSerializer.Deserialize<List<FollowedCompany>>(File.ReadAllText(path), DomainJson.Options) ?? []
            : [];
    }
}
