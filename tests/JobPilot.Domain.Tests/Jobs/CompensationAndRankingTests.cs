using JobPilot.Domain.Jobs;

namespace JobPilot.Domain.Tests.Jobs;

public class CompensationAndRankingTests
{
    private static readonly Dictionary<string, decimal> Rates = new(StringComparer.OrdinalIgnoreCase) { ["USD"] = 1m, ["EUR"] = 1.1m };

    [Fact]
    public void Converts_published_salaries_to_annual_usd()
    {
        Assert.Equal(new AnnualUsdRange(120_000, 150_000), CompensationNormalizer.ToAnnualUsd(120_000, 150_000, "usd", SalaryPeriod.Year, Rates).Range);
        Assert.Equal(new AnnualUsdRange(79_200, null), CompensationNormalizer.ToAnnualUsd(6_000, null, "EUR", SalaryPeriod.Month, Rates).Range);
        Assert.Equal(new AnnualUsdRange(104_000, 104_000), CompensationNormalizer.ToAnnualUsd(50, 50, "USD", SalaryPeriod.Hour, Rates).Range);
    }

    [Theory]
    [InlineData(null, "USD", SalaryPeriod.Year, "no informada")]
    [InlineData(5000.0, null, SalaryPeriod.Month, "moneda")]
    [InlineData(5000.0, "BRL", SalaryPeriod.Month, "tipo de cambio de BRL")]
    [InlineData(5000.0, "USD", null, "período")]
    public void Never_guesses_missing_data(double? min, string? currency, SalaryPeriod? period, string problem)
    {
        var result = CompensationNormalizer.ToAnnualUsd((decimal?)min, null, currency, period, Rates);

        Assert.Null(result.Range);
        Assert.Contains(problem, result.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public void Ranking_weights_compensation_most_by_default()
    {
        var weights = new RankingWeights();
        var targets = new CompensationTargets();

        var wellPaid = JobRanker.Rank(new RankInputs(60, 160_000m, EligibilityStatus.Likely, 0), weights, targets);
        var betterFit = JobRanker.Rank(new RankInputs(95, 70_000m, EligibilityStatus.Likely, 0), weights, targets);

        Assert.True(wellPaid.Score > betterFit.Score);
        Assert.False(wellPaid.IsPreliminary);
        Assert.Equal(88.0, wellPaid.Score);
    }

    [Fact]
    public void Unknown_compensation_gets_the_configured_neutral_value_and_no_estimate()
    {
        var result = JobRanker.Rank(new RankInputs(100, null, EligibilityStatus.Likely, 0), new RankingWeights(), new CompensationTargets(UnknownScore: 0.4));

        var compensation = result.Components.Single(c => c.Name == "Compensación");
        Assert.Equal(0.4, compensation.Value);
        Assert.Contains("No informada", compensation.Explanation, StringComparison.Ordinal);
        Assert.Equal(76.0, result.Score);
    }

    [Fact]
    public void Without_analysis_the_rank_is_preliminary()
    {
        var result = JobRanker.Rank(new RankInputs(null, null, EligibilityStatus.Unknown, null), new RankingWeights(), new CompensationTargets());

        Assert.True(result.IsPreliminary);
    }

    [Fact]
    public void Ineligible_offers_and_red_flags_lower_the_score()
    {
        var weights = new RankingWeights();
        var targets = new CompensationTargets();
        var clean = JobRanker.Rank(new RankInputs(80, 150_000m, EligibilityStatus.Likely, 0), weights, targets);

        Assert.True(JobRanker.Rank(new RankInputs(80, 150_000m, EligibilityStatus.Unlikely, 0), weights, targets).Score < clean.Score);
        Assert.True(JobRanker.Rank(new RankInputs(80, 150_000m, EligibilityStatus.Likely, 3), weights, targets).Score < clean.Score);
    }

    [Fact]
    public void Weights_are_relative()
    {
        var inputs = new RankInputs(50, 150_000m, EligibilityStatus.Likely, 0);

        Assert.Equal(
            JobRanker.Rank(inputs, new RankingWeights(4, 3, 2, 1), new CompensationTargets()).Score,
            JobRanker.Rank(inputs, new RankingWeights(40, 30, 20, 10), new CompensationTargets()).Score);
    }
}
