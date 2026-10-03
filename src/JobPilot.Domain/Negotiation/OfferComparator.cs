using JobPilot.Domain.Jobs;

namespace JobPilot.Domain.Negotiation;

public enum EmploymentModel
{
    Employee,
    Contractor,
    Eor,
}

/// <summary>An offer entered by the owner. Every amount is the owner's own data: nothing is estimated.</summary>
public sealed class CompensationOffer
{
    public long Id { get; set; }

    public required string Company { get; set; }

    public string? Role { get; set; }

    public Guid? JobPostingId { get; set; }

    public required string Currency { get; set; }

    public decimal BaseAmount { get; set; }

    /// <summary>Year, Month or Hour.</summary>
    public SalaryPeriod Period { get; set; } = SalaryPeriod.Year;

    /// <summary>Hours billed per month for hourly offers.</summary>
    public decimal HoursPerMonth { get; set; } = 160;

    public decimal AnnualBonus { get; set; }

    /// <summary>The owner's own yearly value of benefits (health, equipment, learning...).</summary>
    public decimal AnnualBenefits { get; set; }

    /// <summary>The owner's own yearly value of equity, if they want to count it.</summary>
    public decimal AnnualEquity { get; set; }

    public EmploymentModel Model { get; set; }

    public int? PaidTimeOffDays { get; set; }

    public string? Notes { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>Owner-provided costs of working as a contractor (accountant, taxes/fees as a share of gross, unpaid days off).</summary>
/// <param name="UnpaidDaysOffPerYear">Days off a contractor paid by the hour does not bill (8 hours each).</param>
public sealed record ContractorCosts(decimal MonthlyFixedUsd = 0, decimal PercentOfGross = 0, int UnpaidDaysOffPerYear = 0);

/// <param name="ComparableAnnualUsd">Gross + bonus + benefits + equity − contractor costs, in annual USD; null when it cannot be computed.</param>
/// <param name="Problem">Why it cannot be computed (Spanish), e.g. a missing exchange rate.</param>
public sealed record OfferComparisonRow(
    CompensationOffer Offer,
    decimal? GrossAnnualUsd,
    decimal? ExtrasAnnualUsd,
    decimal? ContractorCostsUsd,
    decimal? ComparableAnnualUsd,
    string? Problem,
    IReadOnlyList<string> Breakdown);

/// <summary>Normalizes owner-entered offers to comparable annual USD with owner-provided exchange rates and costs.</summary>
public static class OfferComparator
{
    public const decimal HoursPerDay = 8;

    public static IReadOnlyList<OfferComparisonRow> Compare(IEnumerable<CompensationOffer> offers, IReadOnlyDictionary<string, decimal> usdPerUnit, ContractorCosts costs)
    {
        ArgumentNullException.ThrowIfNull(offers);
        return offers.Select(o => Normalize(o, usdPerUnit, costs))
            .OrderByDescending(r => r.ComparableAnnualUsd.HasValue)
            .ThenByDescending(r => r.ComparableAnnualUsd)
            .ToList();
    }

    public static OfferComparisonRow Normalize(CompensationOffer offer, IReadOnlyDictionary<string, decimal> usdPerUnit, ContractorCosts costs)
    {
        ArgumentNullException.ThrowIfNull(offer);
        ArgumentNullException.ThrowIfNull(usdPerUnit);
        ArgumentNullException.ThrowIfNull(costs);
        var code = offer.Currency.Trim().ToUpperInvariant();
        if (!usdPerUnit.TryGetValue(code, out var rate) || rate <= 0)
        {
            return new OfferComparisonRow(offer, null, null, null, null, $"Falta el tipo de cambio de {code} a USD (cargalo en Ofertas → Preferencias).", []);
        }

        if (offer.BaseAmount <= 0)
        {
            return new OfferComparisonRow(offer, null, null, null, null, "El monto base tiene que ser mayor que cero.", []);
        }

        var breakdown = new List<string>();
        decimal grossLocal;
        switch (offer.Period)
        {
            case SalaryPeriod.Year:
                grossLocal = offer.BaseAmount;
                break;
            case SalaryPeriod.Month:
                grossLocal = offer.BaseAmount * 12;
                breakdown.Add($"{offer.BaseAmount:0.##} {code}/mes × 12");
                break;
            case SalaryPeriod.Hour:
                var hours = offer.HoursPerMonth * 12;
                if (offer.Model == EmploymentModel.Contractor && costs.UnpaidDaysOffPerYear > 0)
                {
                    hours -= costs.UnpaidDaysOffPerYear * HoursPerDay;
                    breakdown.Add($"{costs.UnpaidDaysOffPerYear} días sin facturar");
                }

                if (hours <= 0)
                {
                    return new OfferComparisonRow(offer, null, null, null, null, "Las horas facturables por año dan cero o menos.", []);
                }

                grossLocal = offer.BaseAmount * hours;
                breakdown.Add($"{offer.BaseAmount:0.##} {code}/hora × {hours:0.##} horas/año");
                break;
            default:
                return new OfferComparisonRow(offer, null, null, null, null, "Usá un período anual, mensual o por hora.", []);
        }

        var gross = grossLocal * rate;
        var extras = (offer.AnnualBonus + offer.AnnualBenefits + offer.AnnualEquity) * rate;
        if (rate != 1)
        {
            breakdown.Add($"1 {code} = {rate:0.####} USD");
        }

        decimal? contractorCosts = null;
        if (offer.Model == EmploymentModel.Contractor)
        {
            contractorCosts = costs.MonthlyFixedUsd * 12 + gross * costs.PercentOfGross / 100;
            breakdown.Add($"costos de contractor: {costs.MonthlyFixedUsd:0.##} USD/mes + {costs.PercentOfGross:0.##}% del bruto");
        }

        var comparable = gross + extras - (contractorCosts ?? 0);
        return new OfferComparisonRow(offer, Round(gross), Round(extras), contractorCosts is null ? null : Round(contractorCosts.Value), Round(comparable), null, breakdown);
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
