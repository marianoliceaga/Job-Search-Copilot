using JobPilot.Domain.Cv;

namespace JobPilot.Domain.Tests.Cv;

public class CvImportVerifierTests
{
    private const string Source = """
        ALEX RIVERA — Senior Software Engineer
        alex.rivera@example.com · +1 (555) 0100 · Córdoba, Argentina

        EXPERIENCE
        Northwind Bank — Tech Lead (Mar 2019 – Present)
        • Cut payment processing latency by 40% for 2,000,000 monthly transactions.
        • Migrated the core ledger to ASP.NET Core and Azure.

        SKILLS: C#, .NET, Blazor, EF Core
        LANGUAGES: Spanish (native), English (C1)
        """;

    [Fact]
    public void Values_present_in_the_source_are_not_flagged()
    {
        var cv = Cv(
            new CvExperience
            {
                Company = "Northwind Bank",
                Title = "Tech Lead",
                Start = new PartialDate(2019, 3),
                IsCurrent = true,
                Technologies = ["ASP.NET Core", "Azure"],
                Achievements =
                [
                    new CvAchievement { Text = "Cut payment processing latency by 40% for 2,000,000 monthly transactions." },
                    new CvAchievement { Text = "Migrated the core ledger to ASP.NET Core and Azure." },
                ],
            });
        cv.Contact = new CvContact { FullName = "Alex Rivera", Email = "alex.rivera@example.com", Phone = "+1 555 0100", Location = "Cordoba, Argentina" };
        cv.Skills = [new CvSkill { Name = "Blazor" }, new CvSkill { Name = "EF Core" }];
        cv.Languages = [new CvLanguage { Name = "English", Level = "C1" }];
        cv.EnsureIds();

        Assert.Empty(CvImportVerifier.Verify(cv, Source));
    }

    [Fact]
    public void Flags_invented_numbers_companies_skills_and_years()
    {
        var experience = new CvExperience
        {
            Company = "Contoso",
            Title = "Tech Lead",
            Start = new PartialDate(2015),
            Achievements = [new CvAchievement { Text = "Cut payment processing latency by 60% for 2,000,000 monthly transactions." }],
        };
        var cv = Cv(experience);
        cv.Skills = [new CvSkill { Name = "Kubernetes" }];
        cv.EnsureIds();

        var flags = CvImportVerifier.Verify(cv, Source);

        Assert.Contains(flags, f => f.ElementKey == experience.Id.ToString() && f.Message.Contains("Contoso", StringComparison.Ordinal));
        Assert.Contains(flags, f => f.ElementKey == experience.Id.ToString() && f.Message.Contains("2015", StringComparison.Ordinal));
        Assert.Contains(flags, f => f.ElementKey == experience.Achievements[0].Id.ToString() && f.Message.Contains("60", StringComparison.Ordinal));
        Assert.Contains(flags, f => f.ElementKey == cv.Skills[0].Id.ToString());
    }

    [Fact]
    public void Flags_achievements_reworded_away_from_the_source()
    {
        var cv = Cv(new CvExperience
        {
            Company = "Northwind Bank",
            Title = "Tech Lead",
            Achievements = [new CvAchievement { Text = "Spearheaded enterprise-wide cloud transformation strategy delivering outstanding results." }],
        });
        cv.EnsureIds();

        var flag = Assert.Single(CvImportVerifier.Verify(cv, Source));
        Assert.Equal(cv.Experiences[0].Achievements[0].Id.ToString(), flag.ElementKey);
    }

    [Fact]
    public void Normalization_ignores_case_accents_dashes_and_thousands_separators()
    {
        Assert.Equal("cordoba - 2000000 \"x\"", TextNormalizer.Normalize("CÓRDOBA –  2,000,000 “x”"));
        Assert.Equal(["40", "3.5"], TextNormalizer.Numbers("40% and 3.5x"));
    }

    private static MasterCv Cv(CvExperience experience) => new() { Experiences = [experience] };
}
