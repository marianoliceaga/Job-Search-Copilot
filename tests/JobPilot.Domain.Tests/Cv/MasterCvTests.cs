using JobPilot.Domain.Cv;

namespace JobPilot.Domain.Tests.Cv;

public class MasterCvTests
{
    [Fact]
    public void EnsureIds_assigns_ids_only_to_new_elements()
    {
        var existing = Guid.NewGuid();
        var cv = new MasterCv
        {
            Experiences = [new CvExperience { Id = existing, Company = "Acme", Title = "Dev", Achievements = [new CvAchievement { Text = "Did it" }] }],
            Skills = [new CvSkill { Name = "C#" }],
        };

        cv.EnsureIds();

        Assert.Equal(existing, cv.Experiences[0].Id);
        Assert.NotEqual(Guid.Empty, cv.Experiences[0].Achievements[0].Id);
        Assert.NotEqual(Guid.Empty, cv.Skills[0].Id);
        Assert.DoesNotContain(Guid.Empty, cv.AllElementIds());
    }

    [Fact]
    public void A_complete_cv_is_valid()
    {
        var cv = new MasterCv
        {
            Experiences =
            [
                new CvExperience
                {
                    Company = "Acme", Title = "Engineer", Start = new PartialDate(2020, 1), End = new PartialDate(2022, 6),
                    Achievements = [new CvAchievement { Text = "Shipped" }],
                },
                new CvExperience { Company = "Globex", Title = "Lead", Start = new PartialDate(2022, 7), IsCurrent = true },
            ],
        };
        cv.EnsureIds();

        Assert.Empty(cv.Validate());
    }

    [Fact]
    public void Reports_missing_fields_and_inconsistent_dates()
    {
        var cv = new MasterCv
        {
            Experiences =
            [
                new CvExperience { Company = "", Title = " ", Start = new PartialDate(2022), End = new PartialDate(2021) },
                new CvExperience { Company = "Acme", Title = "Dev", IsCurrent = true, End = new PartialDate(2023), Achievements = [new CvAchievement()] },
            ],
            Skills = [new CvSkill()],
        };
        cv.EnsureIds();

        var problems = cv.Validate();

        Assert.Contains("Experiencia 1: falta la empresa.", problems);
        Assert.Contains("Experiencia 1: falta el puesto.", problems);
        Assert.Contains(problems, p => p.StartsWith("Experiencia 1: la fecha de fin", StringComparison.Ordinal));
        Assert.Contains("Experiencia 2: un trabajo actual no puede tener fecha de fin.", problems);
        Assert.Contains("Experiencia 2: hay logros vacíos.", problems);
        Assert.Contains("Hay skills sin nombre.", problems);
    }

    [Fact]
    public void Reports_duplicate_ids()
    {
        var id = Guid.NewGuid();
        var cv = new MasterCv { Skills = [new CvSkill { Id = id, Name = "A" }, new CvSkill { Id = id, Name = "B" }] };

        Assert.Contains("Hay elementos con identificadores duplicados.", cv.Validate());
    }
}
