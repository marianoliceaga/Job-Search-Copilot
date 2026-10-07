using System.Globalization;
using System.Text;
using JobPilot.Domain.Jobs;
using JobPilot.Domain.Negotiation;
using JobPilot.Mobile.Core.Features.Jobs;
using JobPilot.Mobile.Core.Features.Transfer;
using JobPilot.Mobile.Core.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JobPilot.Mobile.Core.Features.Negotiation;

/// <summary>Offers entered by the owner, contractor costs, and their normalized comparison.</summary>
public sealed class OfferStore(IDbContextFactory<MobileDbContext> dbFactory, SettingsDocumentStore documents, JobPreferencesStore preferences, TimeProvider time)
{
    private const string CostsKey = "contractor-costs";

    public async Task<IReadOnlyList<CompensationOffer>> ListAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.CompensationOffers.AsNoTracking().OrderByDescending(o => o.CreatedAtUtc).ToListAsync(cancellationToken);
    }

    public async Task<CompensationOffer?> GetAsync(long id, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.CompensationOffers.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
    }

    /// <returns>Problems (Spanish); empty when saved.</returns>
    public async Task<IReadOnlyList<string>> SaveAsync(CompensationOffer offer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(offer);
        var problems = Problems(offer);
        if (problems.Count > 0)
        {
            return problems;
        }

        offer.Company = offer.Company.Trim();
        offer.Currency = offer.Currency.Trim().ToUpperInvariant();
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (offer.Id == 0)
        {
            offer.CreatedAtUtc = time.GetUtcNow().UtcDateTime;
            db.CompensationOffers.Add(offer);
        }
        else
        {
            db.CompensationOffers.Update(offer);
        }

        await db.SaveChangesAsync(cancellationToken);
        return [];
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await DeletionLog.DeleteAsync(db, TransferSchema.CompensationOffers, id, ct => db.CompensationOffers.Where(o => o.Id == id).ExecuteDeleteAsync(ct), time.GetUtcNow().UtcDateTime, cancellationToken);
    }

    public async Task<ContractorCosts> GetCostsAsync(CancellationToken cancellationToken) =>
        await documents.GetAsync<ContractorCosts>(CostsKey, cancellationToken) ?? new ContractorCosts();

    public async Task<IReadOnlyList<string>> SaveCostsAsync(ContractorCosts costs, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(costs);
        var problems = new List<string>();
        if (costs.MonthlyFixedUsd < 0 || costs.PercentOfGross is < 0 or > 90 || costs.UnpaidDaysOffPerYear is < 0 or > 200)
        {
            problems.Add("Revisá los costos: montos no negativos, porcentaje entre 0 y 90 y días entre 0 y 200.");
        }
        else
        {
            await documents.SetAsync(CostsKey, costs, cancellationToken);
        }

        return problems;
    }

    public async Task<IReadOnlyList<OfferComparisonRow>> CompareAsync(CancellationToken cancellationToken)
    {
        var offers = await ListAsync(cancellationToken);
        var rates = (await preferences.GetAsync(cancellationToken)).UsdPerUnit;
        return OfferComparator.Compare(offers, rates, await GetCostsAsync(cancellationToken));
    }

    /// <summary>
    /// The facts a negotiation prompt may use, in English with plain numbers (no thousands separators), so numbers in
    /// generated texts can be checked against them.
    /// </summary>
    public async Task<string> FactsAsync(CompensationOffer offer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(offer);
        var prefs = await preferences.GetAsync(cancellationToken);
        var rows = await CompareAsync(cancellationToken);
        var builder = new StringBuilder();
        builder.AppendLine("OFFER BEING NEGOTIATED");
        builder.AppendLine(Describe(offer));
        var row = rows.FirstOrDefault(r => r.Offer.Id == offer.Id) ?? OfferComparator.Normalize(offer, prefs.UsdPerUnit, await GetCostsAsync(cancellationToken));
        builder.AppendLine(row.ComparableAnnualUsd is { } value
            ? $"Comparable annual value (gross + extras - contractor costs): {Number(value)} USD"
            : "Comparable annual value: not computed (missing exchange rate)");
        var others = rows.Where(r => r.Offer.Id != offer.Id && r.ComparableAnnualUsd is not null).ToList();
        builder.AppendLine();
        builder.AppendLine("OTHER OFFERS THE CANDIDATE HAS (comparable annual USD)");
        builder.AppendLine(others.Count == 0 ? "(none)" : string.Join("\n", others.Select(r => $"- {r.Offer.Company}: {Number(r.ComparableAnnualUsd!.Value)} USD")));
        builder.AppendLine();
        builder.AppendLine("CANDIDATE TARGETS (annual USD)");
        builder.Append($"Floor: {Number(prefs.Compensation.FloorUsd)} USD; target: {Number(prefs.Compensation.TargetUsd)} USD");
        return builder.ToString();
    }

    public static string Describe(CompensationOffer offer)
    {
        ArgumentNullException.ThrowIfNull(offer);
        var period = offer.Period switch { SalaryPeriod.Month => "per month", SalaryPeriod.Hour => "per hour", _ => "per year" };
        var lines = new List<string>
        {
            $"Company: {offer.Company}" + (offer.Role is null ? string.Empty : $"; role: {offer.Role}"),
            $"Contract: {offer.Model}",
            $"Base: {Number(offer.BaseAmount)} {offer.Currency} {period}" + (offer.Period == SalaryPeriod.Hour ? $" ({Number(offer.HoursPerMonth)} hours per month)" : string.Empty),
        };
        if (offer.AnnualBonus > 0)
        {
            lines.Add($"Annual bonus: {Number(offer.AnnualBonus)} {offer.Currency}");
        }

        if (offer.AnnualBenefits > 0)
        {
            lines.Add($"Benefits (candidate's own yearly valuation): {Number(offer.AnnualBenefits)} {offer.Currency}");
        }

        if (offer.AnnualEquity > 0)
        {
            lines.Add($"Equity (candidate's own yearly valuation): {Number(offer.AnnualEquity)} {offer.Currency}");
        }

        if (offer.PaidTimeOffDays is { } days)
        {
            lines.Add($"Paid time off: {days} days");
        }

        return string.Join("\n", lines);
    }

    public static IReadOnlyList<string> Problems(CompensationOffer offer)
    {
        ArgumentNullException.ThrowIfNull(offer);
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(offer.Company))
        {
            problems.Add("Indicá la empresa.");
        }

        if (string.IsNullOrWhiteSpace(offer.Currency) || offer.Currency.Trim().Length != 3)
        {
            problems.Add("La moneda es un código de 3 letras (USD, EUR...).");
        }

        if (offer.BaseAmount <= 0)
        {
            problems.Add("El monto base tiene que ser mayor que cero.");
        }

        if (offer.Period is not (SalaryPeriod.Year or SalaryPeriod.Month or SalaryPeriod.Hour))
        {
            problems.Add("El período es anual, mensual o por hora.");
        }

        if (offer.Period == SalaryPeriod.Hour && offer.HoursPerMonth is <= 0 or > 400)
        {
            problems.Add("Las horas por mes tienen que estar entre 1 y 400.");
        }

        if (offer.AnnualBonus < 0 || offer.AnnualBenefits < 0 || offer.AnnualEquity < 0)
        {
            problems.Add("Bono, beneficios y equity no pueden ser negativos.");
        }

        return problems;
    }

    private static string Number(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
