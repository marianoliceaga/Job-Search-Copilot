using JobPilot.Web.Features.Profile;
using JobPilot.Web.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace JobPilot.Web.Tests.Features.Profile;

public class ProfileStoreTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ProfileSeed Seed => new(Path.Combine(AppContext.BaseDirectory, "Seed", "profile.seed.json"));

    [Fact]
    public async Task First_use_creates_the_profile_from_the_seed_file_once()
    {
        await using var database = await TestDatabase.CreateAsync();
        var store = new ProfileStore(database.Factory, TimeProvider.System, Seed);

        var profile = await store.GetAsync(Ct);
        await store.GetAsync(Ct);

        Assert.Equal("Senior .NET / Backend Software Engineer", profile.Headline);
        Assert.Equal(17, profile.YearsOfExperience);
        Assert.Contains("Blazor", profile.Stack);
        Assert.Equal("Argentina", profile.WorkFromCountry);
        Assert.Equal("en", profile.ApplicationLanguage);
        Assert.Equal(3, profile.ApplicationEmphases.Count);
        await using var db = await database.Factory.CreateDbContextAsync(Ct);
        Assert.Equal(1, await db.Profiles.CountAsync(Ct));
    }

    [Fact]
    public async Task Saving_cleans_lists_and_persists_changes()
    {
        await using var database = await TestDatabase.CreateAsync();
        var store = new ProfileStore(database.Factory, TimeProvider.System, Seed);
        var profile = await store.GetAsync(Ct);

        profile.Headline = "  Staff Engineer ";
        profile.Stack = ["C#", " c# ", "", "Go"];
        await store.SaveAsync(profile, Ct);

        var reloaded = await store.GetAsync(Ct);
        Assert.Equal("Staff Engineer", reloaded.Headline);
        Assert.Equal(["C#", "Go"], reloaded.Stack);
    }

    [Fact]
    public void A_missing_seed_file_gives_an_empty_profile()
    {
        var profile = new ProfileSeed("/does/not/exist.json").Load();

        Assert.Equal(string.Empty, profile.Headline);
        Assert.Empty(profile.Stack);
    }
}
