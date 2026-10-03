using JobPilot.Domain.Jobs;
using JobPilot.Domain.Negotiation;
using JobPilot.Domain.Practice;

namespace JobPilot.Domain.Tests.Practice;

public class PracticeSessionTests
{
    private static readonly DateTime T0 = new(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc);

    private static PracticeSession Session(int questions = 2, int followUps = 1) =>
        PracticeSession.Start(PracticeMode.Interview, null, null, InterviewFocus.Behavioral, questions, followUps, "mock-interview.v1", T0);

    [Fact]
    public void Questions_are_asked_one_at_a_time_in_order()
    {
        var session = Session();

        Assert.Contains("number 1", Assert.Single(session.Check(new CoachTurnProposal(TurnKind.Question, 2, "Tell me about yourself."))), StringComparison.Ordinal);
        Assert.Contains("exactly one question", Assert.Single(session.Check(new CoachTurnProposal(TurnKind.Question, 1, "Who are you? Why us?"))), StringComparison.Ordinal);
        Assert.NotEmpty(session.Check(new CoachTurnProposal(TurnKind.FollowUp, 0, "Why?")));

        session.AddCoachTurn(new CoachTurnProposal(TurnKind.Question, 1, "Tell me about a hard bug."), T0);
        Assert.True(session.AwaitingAnswer);
        Assert.Contains("candidate's turn", Assert.Single(session.Check(new CoachTurnProposal(TurnKind.Question, 2, "Next?"))), StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => session.AddCoachTurn(new CoachTurnProposal(TurnKind.Question, 2, "Next?"), T0));
    }

    [Fact]
    public void Follow_ups_are_bounded_and_closing_needs_every_question()
    {
        var session = Session(questions: 2, followUps: 1);
        session.AddCoachTurn(new CoachTurnProposal(TurnKind.Question, 1, "Tell me about a conflict."), T0);
        session.AddAnswer("I disagreed with my lead about a design.", T0);
        Assert.NotEmpty(session.Check(new CoachTurnProposal(TurnKind.Closing, 1, "Thanks.")));

        session.AddCoachTurn(new CoachTurnProposal(TurnKind.FollowUp, 1, "What was the outcome?"), T0);
        session.AddAnswer("We shipped my version.", T0);
        Assert.Contains("already had 1 follow-up", Assert.Single(session.Check(new CoachTurnProposal(TurnKind.FollowUp, 1, "Anything else?"))), StringComparison.Ordinal);

        session.AddCoachTurn(new CoachTurnProposal(TurnKind.Question, 2, "Why this company?"), T0);
        session.AddAnswer("Payments.", T0);
        Assert.NotEmpty(session.Check(new CoachTurnProposal(TurnKind.Question, 3, "One more?")));
        session.AddCoachTurn(new CoachTurnProposal(TurnKind.Closing, 2, "Thanks, that is all. Do you have any questions? I will share feedback."), T0);

        Assert.Equal(PracticeStatus.AwaitingFeedback, session.Status);
        Assert.False(session.AwaitingCoach);
        Assert.Equal(
            [(1, "Tell me about a conflict.\nWhat was the outcome?", "I disagreed with my lead about a design.\nWe shipped my version."), (2, "Why this company?", "Payments.")],
            session.Exchanges());
    }

    [Fact]
    public void Answers_need_a_pending_question_and_finishing_early_needs_an_answer()
    {
        var session = Session();

        Assert.Throws<InvalidOperationException>(() => session.AddAnswer("Hello", T0));
        Assert.Throws<InvalidOperationException>(session.Finish);

        session.AddCoachTurn(new CoachTurnProposal(TurnKind.Question, 1, "Introduce yourself."), T0);
        session.AddAnswer("I am a backend engineer.", T0);
        session.Finish();
        Assert.Equal(PracticeStatus.AwaitingFeedback, session.Status);

        session.Complete(new PracticeFeedback([new CriterionScore("structure", 4, "ok")], [], [], [], 75), T0.AddMinutes(5));
        Assert.Equal((PracticeStatus.Completed, 75), (session.Status, session.OverallScore!.Value));
    }
}

public class RubricTests
{
    private static List<CriterionScore> Full(int score = 4) =>
        Rubrics.Interview.Select(c => new CriterionScore(c.Key, score, "Because.")).ToList();

    [Fact]
    public void A_complete_rubric_is_valid()
    {
        Assert.Empty(Rubrics.Validate(Rubrics.Interview, Full()));
        Assert.Equal(5, Rubrics.Negotiation.Count);
        Assert.Same(Rubrics.Negotiation, Rubrics.For(PracticeMode.Negotiation));
    }

    [Fact]
    public void Missing_duplicated_unknown_out_of_range_and_unjustified_scores_are_rejected()
    {
        var scores = Full();
        scores.RemoveAll(s => s.Criterion == "impact");
        scores.Add(new CriterionScore("structure", 3, "Again."));
        scores.Add(new CriterionScore("charisma", 5, "Nice."));
        scores[1] = scores[1] with { Score = 6 };
        scores[2] = scores[2] with { Justification = " " };

        var problems = Rubrics.Validate(Rubrics.Interview, scores);

        Assert.Contains("\"charisma\" is not a rubric criterion.", problems);
        Assert.Contains("Score \"impact\" exactly once (found 0).", problems);
        Assert.Contains("Score \"structure\" exactly once (found 2).", problems);
        Assert.Contains(problems, p => p.Contains("between 1 and 5", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.StartsWith("Justify", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(3, 50)]
    [InlineData(5, 100)]
    public void Overall_maps_the_average_to_0_100(int score, int expected) => Assert.Equal(expected, Rubrics.Overall(Full(score)));

    [Fact]
    public void Overall_rounds_mixed_scores()
    {
        // (4+4+4+4+3)/5 = 3.8 → (3.8-1)/4 = 70
        var scores = Full();
        scores[^1] = scores[^1] with { Score = 3 };
        Assert.Equal(70, Rubrics.Overall(scores));
        Assert.Equal(0, Rubrics.Overall([]));
    }
}

public class OfferComparatorTests
{
    private static readonly Dictionary<string, decimal> Rates = new(StringComparer.OrdinalIgnoreCase) { ["USD"] = 1m, ["EUR"] = 1.1m };

    private static CompensationOffer Offer(string company, decimal amount, SalaryPeriod period, string currency = "USD", EmploymentModel model = EmploymentModel.Employee) =>
        new() { Company = company, BaseAmount = amount, Period = period, Currency = currency, Model = model };

    [Fact]
    public void Monthly_hourly_and_yearly_offers_become_annual_usd()
    {
        var monthly = OfferComparator.Normalize(Offer("A", 8_000, SalaryPeriod.Month), Rates, new ContractorCosts());
        var hourly = OfferComparator.Normalize(Offer("B", 50, SalaryPeriod.Hour) , Rates, new ContractorCosts());
        var euros = OfferComparator.Normalize(Offer("C", 100_000, SalaryPeriod.Year, "eur"), Rates, new ContractorCosts());

        Assert.Equal(96_000m, monthly.ComparableAnnualUsd);
        Assert.Equal(96_000m, hourly.GrossAnnualUsd);
        Assert.Equal(110_000m, euros.ComparableAnnualUsd);
        Assert.Contains(euros.Breakdown, b => b.StartsWith("1 EUR = 1", StringComparison.Ordinal));
    }

    [Fact]
    public void Contractor_costs_extras_and_unpaid_days_are_applied()
    {
        var offer = Offer("D", 60, SalaryPeriod.Hour, model: EmploymentModel.Contractor);
        offer.AnnualBonus = 1_000;
        offer.AnnualBenefits = 500;
        var costs = new ContractorCosts(MonthlyFixedUsd: 100, PercentOfGross: 10, UnpaidDaysOffPerYear: 20);

        var row = OfferComparator.Normalize(offer, Rates, costs);

        // Hours: 160 × 12 − 20 × 8 = 1760 → gross 105,600; costs 1,200 + 10,560 = 11,760; extras 1,500.
        Assert.Equal(105_600m, row.GrossAnnualUsd);
        Assert.Equal(11_760m, row.ContractorCostsUsd);
        Assert.Equal(1_500m, row.ExtrasAnnualUsd);
        Assert.Equal(95_340m, row.ComparableAnnualUsd);
    }

    [Fact]
    public void Employees_have_no_contractor_costs_and_missing_rates_are_not_guessed()
    {
        var employee = OfferComparator.Normalize(Offer("E", 100_000, SalaryPeriod.Year), Rates, new ContractorCosts(500, 30));
        var unknown = OfferComparator.Normalize(Offer("F", 1_000_000, SalaryPeriod.Year, "ARS"), Rates, new ContractorCosts());

        Assert.Null(employee.ContractorCostsUsd);
        Assert.Equal(100_000m, employee.ComparableAnnualUsd);
        Assert.Null(unknown.ComparableAnnualUsd);
        Assert.Contains("ARS", unknown.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public void Comparison_ranks_by_comparable_value_with_unknown_last()
    {
        var rows = OfferComparator.Compare(
            [Offer("Low", 5_000, SalaryPeriod.Month), Offer("Unknown", 1, SalaryPeriod.Year, "ARS"), Offer("High", 120_000, SalaryPeriod.Year)],
            Rates, new ContractorCosts());

        Assert.Equal(["High", "Low", "Unknown"], rows.Select(r => r.Offer.Company));
    }
}
