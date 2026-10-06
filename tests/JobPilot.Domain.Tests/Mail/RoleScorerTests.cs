using JobPilot.Domain.Mail;

using static JobPilot.Domain.Tests.Mail.MailTestData;

namespace JobPilot.Domain.Tests.Mail;

public class RoleScorerTests
{
    private static RoleScorer Scorer()
    {
        var rules = Rules();
        return RoleScorer.Compile(rules.Scoring, new SetResolver(rules.Sets));
    }

    [Fact]
    public void Every_rule_that_fires_is_named_and_the_fit_follows_the_thresholds()
    {
        var result = Scorer().Score([Listing("1", "Senior .NET Engineer", pay: new AlertPay(4000, 5000, "USD", "month", "$4K–$5K/mo"))]);

        var role = Assert.Single(result.Shown);
        Assert.Equal(3 + 1 + 3 + 2, role.Score);
        Assert.Equal(RoleFit.Strong, role.Fit);
        Assert.Equal(["+.NET en el título", "+senior", "+remoto LatAm", "+pago hasta USD 5K/mes"], role.Why);
        Assert.Equal(5000m, role.PayMonthlyUsd);
    }

    [Fact]
    public void The_same_role_from_several_cities_and_alerts_is_one_role_represented_by_its_best_listing()
    {
        var result = Scorer().Score(
        [
            Listing("1", "Senior .NET Engineer", location: "Cordoba", workplace: "onsite"),
            Listing("2", "Senior .NET Engineer", location: "Argentina", workplace: "remote"),
            Listing("3", "Senior .NET  engineer", location: "Rosario", workplace: "hybrid"),
        ]);

        var role = Assert.Single(result.Shown);
        Assert.Equal("2", role.Best.JobId);
        Assert.Equal(3, role.Listings.Count);
        Assert.Equal(3, result.Listings);
    }

    [Fact]
    public void Filters_drop_roles_under_their_chip_unless_a_group_rescues_them()
    {
        var result = Scorer().Score(
        [
            Listing("1", "Java Developer", location: "Austin, TX", workplace: "remote"),
            Listing("2", ".NET Developer", location: "Cordoba", workplace: "onsite"),
            Listing("3", "Python Developer", location: "Cordoba", workplace: "onsite"),
            Listing("4", "Junior Java Developer", location: "Argentina", workplace: "remote"),
        ]);

        Assert.Equal("Remoto, solo EE. UU.", result.Filtered.Single(r => r.Best.JobId == "1").FilteredBy);
        var rescued = result.Shown.Single(r => r.Best.JobId == "2");
        Assert.Contains("se mantiene pese a: presencial fuera de tu ciudad", rescued.Why);
        Assert.Equal("Presencial fuera de tu ciudad", result.Filtered.Single(r => r.Best.JobId == "3").FilteredBy);
        Assert.Equal("Por debajo del nivel", result.Filtered.Single(r => r.Best.JobId == "4").FilteredBy);
    }

    [Fact]
    public void Pay_is_only_compared_when_stated_with_a_known_rate()
    {
        var result = Scorer().Score(
        [
            Listing("1", "Senior .NET Engineer A", pay: new AlertPay(20000, 24000, "USD", "year", "$20K–$24K/yr")),
            Listing("2", "Senior .NET Engineer B", pay: new AlertPay(30, 40, "ARS", "hour", "ARS 30–40/hr")),
            Listing("3", "Senior .NET Engineer C"),
        ]);

        Assert.Contains("-pago tope USD 2K/mes", result.Shown.Single(r => r.Best.JobId == "1").Why);
        Assert.Null(result.Shown.Single(r => r.Best.JobId == "2").PayMonthlyUsd);
        Assert.DoesNotContain(result.Shown.Single(r => r.Best.JobId == "3").Why, w => w.Contains("pago", StringComparison.Ordinal));
    }

    [Fact]
    public void Unknown_fields_and_pay_tests_are_rejected_when_compiling()
    {
        var rules = new RoleScoringRules { Rules = [new() { Id = "x", Why = "x", Group = "g", Weight = 1, When = [new() { Field = "salary" }] }] };

        var error = Assert.Throws<MailRulesException>(() => RoleScorer.Compile(rules, new SetResolver(new Dictionary<string, List<string>>())));
        Assert.Contains("scoring.rules[0] (x)", error.Message, StringComparison.Ordinal);
    }
}
