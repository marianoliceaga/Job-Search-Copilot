using System.Text.Json;
using JobPilot.Domain.Jobs;
using JobPilot.Mobile.Core.Infrastructure.Persistence;

namespace JobPilot.Mobile.Core.Features.Jobs;

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

/// <summary>
/// Seed files with initial preferences and followed companies: the web app's <c>Seed/job-search.seed.json</c> and
/// <c>Seed/followed-companies.seed.json</c>, embedded at build time (data, not code).
/// </summary>
public sealed class JobSeedFiles
{
    private readonly Func<string, Stream?> _open;

    public JobSeedFiles()
        : this(name => typeof(JobSeedFiles).Assembly.GetManifestResourceStream("Seed/" + name))
    {
    }

    /// <param name="open">Opens a seed by file name, or returns null when it does not exist (tests use a folder).</param>
    internal JobSeedFiles(Func<string, Stream?> open) => _open = open;

    public JobSearchPreferences LoadPreferences() =>
        Read<JobSearchPreferences>("job-search.seed.json") ?? new JobSearchPreferences();

    public IReadOnlyList<FollowedCompany> LoadFollowedCompanies() =>
        Read<List<FollowedCompany>>("followed-companies.seed.json") ?? [];

    private T? Read<T>(string fileName)
        where T : class
    {
        using var stream = _open(fileName);
        return stream is null ? null : JsonSerializer.Deserialize<T>(stream, DomainJson.Options);
    }
}
