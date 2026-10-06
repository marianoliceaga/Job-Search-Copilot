using JobPilot.Domain.Cv;
using JobPilot.Mobile.Core.Features.Profile;
using JobPilot.Mobile.Tests.TestSupport;

namespace JobPilot.Mobile.Tests.Features.Profile;

public class MasterCvStoreTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Saving_creates_a_new_version_with_a_snapshot()
    {
        await using var database = await TestDatabase.CreateAsync();
        var store = new MasterCvStore(database.Factory, TimeProvider.System);

        Assert.Null(await store.GetAsync(Ct));

        var cv = SampleCv();
        var first = await store.SaveAsync(cv, "import: cv.pdf", Ct);
        cv.Experiences[0].Achievements.Add(new CvAchievement { Text = "Second achievement" });
        var second = await store.SaveAsync(cv, "edit", Ct);

        Assert.Equal(1, first.Version);
        Assert.Equal(2, second.Version);

        var stored = await store.GetAsync(Ct);
        Assert.Equal(2, stored!.Version);
        Assert.Equal(2, stored.Cv.Experiences[0].Achievements.Count);
        Assert.Equal(new PartialDate(2019, 3), stored.Cv.Experiences[0].Start);
        Assert.Equal(cv.Experiences[0].Id, stored.Cv.Experiences[0].Id);

        var versions = await store.GetVersionsAsync(Ct);
        Assert.Equal([2, 1], versions.Select(v => v.Version));
        Assert.Equal(["edit", "import: cv.pdf"], versions.Select(v => v.Source));
        Assert.Single((await store.GetVersionAsync(1, Ct))!.Experiences[0].Achievements);
    }

    [Fact]
    public async Task Element_ids_are_assigned_on_save_and_kept_afterwards()
    {
        await using var database = await TestDatabase.CreateAsync();
        var store = new MasterCvStore(database.Factory, TimeProvider.System);
        var cv = new MasterCv { Skills = [new CvSkill { Name = "C#" }] };

        await store.SaveAsync(cv, "edit", Ct);
        var id = (await store.GetAsync(Ct))!.Cv.Skills[0].Id;
        var reloaded = (await store.GetAsync(Ct))!.Cv;
        await store.SaveAsync(reloaded, "edit", Ct);

        Assert.NotEqual(Guid.Empty, id);
        Assert.Equal(id, (await store.GetAsync(Ct))!.Cv.Skills[0].Id);
    }

    [Fact]
    public async Task Invalid_cvs_are_not_saved()
    {
        await using var database = await TestDatabase.CreateAsync();
        var store = new MasterCvStore(database.Factory, TimeProvider.System);

        var result = await store.SaveAsync(new MasterCv { Experiences = [new CvExperience()] }, "edit", Ct);

        Assert.False(result.IsSuccess);
        Assert.Contains("Experiencia 1: falta la empresa.", result.Problems);
        Assert.Null(await store.GetAsync(Ct));
        Assert.Empty(await store.GetVersionsAsync(Ct));
    }

    [Fact]
    public async Task Restoring_saves_the_old_snapshot_as_a_new_version()
    {
        await using var database = await TestDatabase.CreateAsync();
        var store = new MasterCvStore(database.Factory, TimeProvider.System);
        var cv = SampleCv();
        await store.SaveAsync(cv, "import: cv.pdf", Ct);
        cv.Headline = "Changed";
        await store.SaveAsync(cv, "edit", Ct);

        var result = await store.RestoreAsync(1, Ct);

        Assert.Equal(3, result.Version);
        var stored = await store.GetAsync(Ct);
        Assert.Equal("Senior Software Engineer", stored!.Cv.Headline);
        Assert.Equal("restore: v1", (await store.GetVersionsAsync(Ct))[0].Source);
    }

    private static MasterCv SampleCv() => new()
    {
        Headline = "Senior Software Engineer",
        Experiences =
        [
            new CvExperience
            {
                Company = "Northwind Bank",
                Title = "Tech Lead",
                Start = new PartialDate(2019, 3),
                IsCurrent = true,
                Achievements = [new CvAchievement { Text = "Cut latency by 40%." }],
            },
        ],
    };
}
