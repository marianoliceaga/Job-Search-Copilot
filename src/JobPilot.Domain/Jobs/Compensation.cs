namespace JobPilot.Domain.Jobs;

/// <summary>Annual amounts in USD.</summary>
public sealed record AnnualUsdRange(decimal? Min, decimal? Max)
{
    public decimal? Midpoint => (Min, Max) switch
    {
        ({ } min, { } max) => (min + max) / 2,
        ({ } min, null) => min,
        (null, { } max) => max,
        _ => null,
    };
}

public sealed record CompensationNormalization(AnnualUsdRange? Range, string? Problem);

public static class CompensationNormalizer
{
    private const decimal WorkHoursPerYear = 2080;
    private const decimal WorkDaysPerYear = 260;

    /// <summary>
    /// Converts a published salary to annual USD using owner-provided exchange rates (USD per unit of currency).
    /// Returns a problem instead of a value when something is missing: rates are never guessed.
    /// </summary>
    public static CompensationNormalization ToAnnualUsd(decimal? min, decimal? max, string? currency, SalaryPeriod? period,
        IReadOnlyDictionary<string, decimal> usdPerUnit)
    {
        ArgumentNullException.ThrowIfNull(usdPerUnit);
        if (min is null && max is null)
        {
            return new(null, "Compensación no informada.");
        }

        if (string.IsNullOrWhiteSpace(currency))
        {
            return new(null, "La compensación no indica la moneda.");
        }

        var code = currency.Trim().ToUpperInvariant();
        if (!usdPerUnit.TryGetValue(code, out var rate) || rate <= 0)
        {
            return new(null, $"Falta el tipo de cambio de {code} a USD (cargalo en Preferencias).");
        }

        if (period is null)
        {
            return new(null, "La compensación no indica el período (anual, mensual, por hora).");
        }

        var factor = period switch
        {
            SalaryPeriod.Year => 1m,
            SalaryPeriod.Month => 12m,
            SalaryPeriod.Week => 52m,
            SalaryPeriod.Day => WorkDaysPerYear,
            SalaryPeriod.Hour => WorkHoursPerYear,
            _ => throw new ArgumentOutOfRangeException(nameof(period)),
        };

        return new(new AnnualUsdRange(min * factor * rate, max * factor * rate), null);
    }
}
