namespace JobPilot.Domain.Jobs;

/// <summary>Relative weights of the ranking components (any positive numbers; they are normalized).</summary>
public sealed record RankingWeights(double Compensation = 40, double Fit = 30, double Eligibility = 20, double RedFlags = 10);

/// <param name="FloorUsd">Annual USD at or below which the compensation component is 0.</param>
/// <param name="TargetUsd">Annual USD at or above which the compensation component is 1.</param>
/// <param name="UnknownScore">Component value (0–1) when compensation is not informed: neither rewarded nor estimated.</param>
public sealed record CompensationTargets(decimal FloorUsd = 60_000, decimal TargetUsd = 150_000, double UnknownScore = 0.4);

/// <param name="FitScore">LLM fit 0–100; null before the analysis.</param>
/// <param name="AnnualUsdMidpoint">Normalized published compensation; null when not informed or not convertible.</param>
/// <param name="RedFlagCount">Null before the analysis.</param>
public sealed record RankInputs(double? FitScore, decimal? AnnualUsdMidpoint, EligibilityStatus Eligibility, int? RedFlagCount);

public sealed record RankComponent(string Name, double Value, double Weight, string Explanation);

/// <param name="IsPreliminary">True when the fit or red flags are not known yet (no analysis).</param>
public sealed record RankResult(double Score, bool IsPreliminary, IReadOnlyList<RankComponent> Components);

public static class JobRanker
{
    private const double NeutralWhenUnknown = 0.5;
    private const double PenaltyPerRedFlag = 0.25;

    public static RankResult Rank(RankInputs inputs, RankingWeights weights, CompensationTargets targets)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(weights);
        ArgumentNullException.ThrowIfNull(targets);

        var compensation = inputs.AnnualUsdMidpoint is { } usd
            ? new RankComponent("Compensación", Scale(usd, targets), weights.Compensation, $"US$ {usd:N0} anuales (punto medio)")
            : new RankComponent("Compensación", targets.UnknownScore, weights.Compensation, "No informada: valor neutro configurable, sin estimar");

        var fit = inputs.FitScore is { } score
            ? new RankComponent("Encaje", Math.Clamp(score / 100, 0, 1), weights.Fit, $"{score:0}/100 según el análisis")
            : new RankComponent("Encaje", NeutralWhenUnknown, weights.Fit, "Sin analizar");

        var eligibility = inputs.Eligibility switch
        {
            EligibilityStatus.Likely => new RankComponent("Elegibilidad", 1, weights.Eligibility, "Probablemente elegible"),
            EligibilityStatus.Unlikely => new RankComponent("Elegibilidad", 0, weights.Eligibility, "Probablemente no elegible"),
            _ => new RankComponent("Elegibilidad", NeutralWhenUnknown, weights.Eligibility, "Incierta"),
        };

        var redFlags = inputs.RedFlagCount is { } count
            ? new RankComponent("Señales de alerta", Math.Max(0, 1 - (count * PenaltyPerRedFlag)), weights.RedFlags, $"{count} señales")
            : new RankComponent("Señales de alerta", NeutralWhenUnknown, weights.RedFlags, "Sin analizar");

        RankComponent[] components = [compensation, fit, eligibility, redFlags];
        var totalWeight = components.Sum(c => Math.Max(0, c.Weight));
        var score100 = totalWeight <= 0 ? 0 : components.Sum(c => c.Value * Math.Max(0, c.Weight)) / totalWeight * 100;

        return new RankResult(Math.Round(score100, 1), inputs.FitScore is null || inputs.RedFlagCount is null, components);
    }

    private static double Scale(decimal usd, CompensationTargets targets)
    {
        if (targets.TargetUsd <= targets.FloorUsd)
        {
            return usd >= targets.TargetUsd ? 1 : 0;
        }

        return (double)Math.Clamp((usd - targets.FloorUsd) / (targets.TargetUsd - targets.FloorUsd), 0, 1);
    }
}
