using JobPilot.Mobile.Core.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using ProfileEntity = JobPilot.Domain.Profiles.Profile;

namespace JobPilot.Mobile.Core.Features.Profile;

/// <summary>
/// The owner's profile (single row). Created empty on first use, then filled in by the owner: the phone app can be installed
/// by other people, so it starts without anyone's professional data.
/// </summary>
public sealed class ProfileStore(IDbContextFactory<MobileDbContext> dbFactory, TimeProvider time)
{
    public async Task<ProfileEntity> GetAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var profile = await db.Profiles.AsNoTracking().OrderBy(p => p.Id).FirstOrDefaultAsync(cancellationToken);
        if (profile is not null)
        {
            return profile;
        }

        // Never edited: the minimum date lets a profile from an imported file ("Combinar", newer wins) replace it.
        profile = new ProfileEntity { UpdatedAtUtc = DateTime.MinValue };
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

    /// <summary>Whether the owner has not filled in the profile yet (shown as a first step on the Panel).</summary>
    public static bool IsBlank(ProfileEntity profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return string.IsNullOrWhiteSpace(profile.Headline) && profile.YearsOfExperience is null && profile.Roles.Count == 0
            && profile.Stack.Count == 0 && profile.Domains.Count == 0 && string.IsNullOrWhiteSpace(profile.WorkFromCountry);
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
