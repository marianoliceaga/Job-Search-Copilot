using System.Text.Json;
using JobPilot.Web.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using ProfileEntity = JobPilot.Domain.Profiles.Profile;

namespace JobPilot.Web.Features.Profile;

/// <summary>The owner's profile (single row). Created on first use from the seed file, then edited in the UI.</summary>
public sealed class ProfileStore(IDbContextFactory<JobPilotDbContext> dbFactory, TimeProvider time, ProfileSeed seed)
{
    public async Task<ProfileEntity> GetAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var profile = await db.Profiles.AsNoTracking().OrderBy(p => p.Id).FirstOrDefaultAsync(cancellationToken);
        if (profile is not null)
        {
            return profile;
        }

        profile = seed.Load();
        profile.UpdatedAtUtc = time.GetUtcNow().UtcDateTime;
        db.Profiles.Add(profile);
        await db.SaveChangesAsync(cancellationToken);
        return profile;
    }

    public async Task SaveAsync(ProfileEntity profile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        Clean(profile);
        profile.UpdatedAtUtc = time.GetUtcNow().UtcDateTime;

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.Profiles.Update(profile);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static void Clean(ProfileEntity profile)
    {
        profile.Headline = profile.Headline.Trim();
        profile.Roles = CleanList(profile.Roles);
        profile.Stack = CleanList(profile.Stack);
        profile.Domains = CleanList(profile.Domains);
        profile.ApplicationEmphases = CleanList(profile.ApplicationEmphases);
    }

    private static List<string> CleanList(List<string> values) =>
        values.Select(v => v.Trim()).Where(v => v.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}

/// <summary>Initial profile values, read from <c>Seed/profile.seed.json</c> (editable data, not code).</summary>
public sealed class ProfileSeed(string path)
{
    public ProfileEntity Load()
    {
        if (!File.Exists(path))
        {
            return new ProfileEntity();
        }

        return JsonSerializer.Deserialize<ProfileEntity>(File.ReadAllText(path), DomainJson.Options) ?? new ProfileEntity();
    }
}
