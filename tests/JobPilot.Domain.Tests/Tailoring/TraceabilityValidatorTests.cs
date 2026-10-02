using JobPilot.Domain.Cv;
using JobPilot.Domain.Tailoring;

namespace JobPilot.Domain.Tests.Tailoring;

public class TraceabilityValidatorTests
{
    private static readonly string[] Catalog = ["Kubernetes", "Docker", "Azure", "C#", "Blazor", "React"];

    private readonly MasterCv _cv;
    private readonly CvSourceIndex _index;
    private readonly TraceabilityValidator _validator;

    public TraceabilityValidatorTests()
    {
        _cv = new MasterCv
        {
            Headline = "Senior Software Engineer",
            Summary = "Backend engineer with 15 years of experience.",
            Experiences =
            [
                new CvExperience
                {
                    Company = "Northwind Bank", Title = "Tech Lead", Start = new PartialDate(2019, 3), IsCurrent = true,
                    Technologies = ["C#", "Azure"],
                    Achievements = [new CvAchievement { Text = "Cut payment processing latency by 40% for 2,000,000 monthly transactions." }],
                },
                new CvExperience
                {
                    Company = "Contoso Retail", Title = "Senior Software Engineer",
                    Achievements = [new CvAchievement { Text = "Built the order management API used by 120 stores." }],
                },
            ],
            Skills = [new CvSkill { Name = "Blazor" }],
        };
        _cv.EnsureIds();
        _index = new CvSourceIndex(_cv, "Years of experience: 17\nMain stack: C#, Blazor");
        _validator = new TraceabilityValidator(_index, Catalog);
    }

    private string Achievement(int experience) => _cv.Experiences[experience].Achievements[0].Id.ToString();

    private Guid Experience(int experience) => _cv.Experiences[experience].Id;

    [Fact]
    public void A_faithful_rephrasing_passes()
    {
        var bullet = new TraceableText { Text = "Reduced latency of 2,000,000 monthly payments by 40% on Azure.", Sources = [Achievement(0), Experience(0).ToString()] };

        Assert.Empty(_validator.Check(bullet, Experience(0)));
    }

    [Fact]
    public void An_invented_metric_is_rejected()
    {
        var bullet = new TraceableText { Text = "Cut payment processing latency by 60% for 2,000,000 monthly transactions.", Sources = [Achievement(0)] };

        var problem = Assert.Single(_validator.Check(bullet, Experience(0)));
        Assert.Contains("60", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_technology_that_is_not_in_the_cv_is_rejected()
    {
        var bullet = new TraceableText { Text = "Migrated payments to Kubernetes and C#.", Sources = [Achievement(0)] };

        var problem = Assert.Single(_validator.Check(bullet, Experience(0)));
        Assert.Contains("Kubernetes", problem, StringComparison.Ordinal);
        Assert.DoesNotContain("C#", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void Texts_without_sources_or_with_unknown_sources_are_rejected()
    {
        Assert.Contains(_validator.Check(new TraceableText { Text = "Led things." }), p => p.Contains("No cita", StringComparison.Ordinal));
        Assert.Contains(_validator.Check(new TraceableText { Text = "Led things.", Sources = [Guid.NewGuid().ToString()] }), p => p.Contains("no existe", StringComparison.Ordinal));
    }

    [Fact]
    public void A_bullet_cannot_cite_another_experience()
    {
        var bullet = new TraceableText { Text = "Built an API used by 120 stores.", Sources = [Achievement(1)] };

        Assert.Contains(_validator.Check(bullet, Experience(0)), p => p.Contains("otra experiencia", StringComparison.Ordinal));
        Assert.Empty(_validator.Check(bullet, Experience(1)));
    }

    [Fact]
    public void The_summary_may_use_the_profile_and_letters_may_use_numbers_from_the_posting()
    {
        var summary = new TraceableText { Text = "Engineer with 17 years of C# and Blazor.", Sources = [SourceKeys.Profile] };
        var paragraph = new TraceableText { Text = "Your 300 stores would benefit from APIs like the one I built for 120 stores.", Sources = [Achievement(1)] };

        Assert.Empty(_validator.Check(summary));
        Assert.NotEmpty(_validator.Check(paragraph));
        Assert.Empty(_validator.Check(paragraph, allowedExtraText: "We operate 300 stores."));
    }

    [Fact]
    public void Checks_a_whole_cv_and_its_skills()
    {
        var tailored = new TailoredCv
        {
            Headline = new TraceableText { Text = "Senior Software Engineer", Sources = [SourceKeys.Headline] },
            Summary = new TraceableText { Text = "Backend engineer with 20 years of experience.", Sources = [SourceKeys.Summary] },
            Experiences = [new TailoredExperience { ExperienceId = Experience(0), Bullets = [new TraceableText { Text = "Cut latency by 40%.", Sources = [Achievement(0)] }] }],
            Skills = ["Blazor", "Azure", "Kubernetes"],
        };

        var problems = _validator.Check(tailored);

        Assert.Same(tailored.Summary, Assert.Single(problems).Text);
        Assert.Equal(["Kubernetes"], _validator.UnknownSkills(tailored.Skills));
    }

    [Fact]
    public void Short_references_map_to_element_keys()
    {
        Assert.Equal(Experience(0).ToString(), _index.KeyOf("E1"));
        Assert.Equal(Achievement(1), _index.KeyOf("e2.a1"));
        Assert.Equal(SourceKeys.Summary, _index.KeyOf("SUM"));
        Assert.Equal("E1.A1", _index.RefOf(Achievement(0)));
        Assert.Null(_index.KeyOf("E9"));
        Assert.Equal(["Blazor", "C#", "Azure"], _index.Vocabulary);
    }

    [Fact]
    public void Texts_without_sources_may_be_allowed_but_numbers_and_technologies_are_still_checked()
    {
        var line = new TraceableText { Text = "Following up on my application from 12 September." };
        var invented = new TraceableText { Text = "I have 9 years of Kubernetes experience." };

        Assert.Contains("No cita ningún elemento del CV maestro.", _validator.Check(line));
        Assert.Empty(_validator.Check(line, allowedExtraText: "Applied on 12 September", requireSources: false));
        var problems = _validator.Check(invented, requireSources: false);
        Assert.Equal(2, problems.Count);
        Assert.Contains(problems, p => p.Contains("Kubernetes", StringComparison.Ordinal));
    }
}
