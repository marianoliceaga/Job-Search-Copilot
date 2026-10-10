using JobPilot.Mobile.Core.Features.Profile;
using JobPilot.Mobile.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace JobPilot.Mobile.Tests.Features.Profile;

public class ProfileStoreTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task First_use_creates_one_empty_profile_marked_as_never_edited()
    {
        await using var database = await TestDatabase.CreateAsync();
        var store = new ProfileStore(database.Factory, TimeProvider.System);

        var profile = await store.GetAsync(Ct);
        await store.GetAsync(Ct);

        Assert.True(ProfileStore.IsBlank(profile));
        Assert.Equal(string.Empty, profile.Headline);
        Assert.Null(profile.YearsOfExperience);
        Assert.Empty(profile.Stack);
        Assert.Null(profile.WorkFromCountry);
        Assert.Empty(profile.ApplicationEmphases);
        Assert.Equal("en", profile.ApplicationLanguage);
        Assert.Equal(DateTime.MinValue, profile.UpdatedAtUtc);
        await using var db = await database.Factory.CreateDbContextAsync(Ct);
        Assert.Equal(1, await db.Profiles.CountAsync(Ct));
    }

    [Fact]
    public void The_app_does_not_embed_the_web_profile_seed()
    {
        var resources = typeof(ProfileStore).Assembly.GetManifestResourceNames();

        Assert.DoesNotContain(resources, name => name.Contains("profile.seed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Saving_cleans_lists_and_persists_changes()
    {
        await using var database = await TestDatabase.CreateAsync();
        var store = new ProfileStore(database.Factory, TimeProvider.System);
        var profile = await store.GetAsync(Ct);

        profile.Headline = "  Staff Engineer ";
        profile.Stack = ["C#", " c# ", "", "Go"];
        await store.SaveAsync(profile, Ct);

        var reloaded = await store.GetAsync(Ct);
        Assert.Equal("Staff Engineer", reloaded.Headline);
        Assert.Equal(["C#", "Go"], reloaded.Stack);
        Assert.False(ProfileStore.IsBlank(reloaded));
        Assert.NotEqual(DateTime.MinValue, reloaded.UpdatedAtUtc);
    }
}
