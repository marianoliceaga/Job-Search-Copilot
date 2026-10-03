using System.Globalization;
using JobPilot.Domain.Jobs;

namespace JobPilot.Web.Features.Jobs;

/// <summary>UI helpers for offers.</summary>
public static class JobFormat
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("es-AR");

    public static string Compensation(JobListItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Compensation.Range is { } range)
        {
            return (range.Min, range.Max) switch
            {
                ({ } min, { } max) when min != max => string.Create(Culture, $"US$ {min:N0} – {max:N0} /año"),
                ({ } min, _) => string.Create(Culture, $"US$ {min:N0} /año"),
                (_, { } max) => string.Create(Culture, $"US$ {max:N0} /año"),
                _ => "No informada",
            };
        }

        return item.Posting.SalaryText ?? "No informada";
    }

    public static string RunStatus(Infrastructure.Persistence.SourceRunStatus? status) => status switch
    {
        Infrastructure.Persistence.SourceRunStatus.Succeeded => "correcta",
        Infrastructure.Persistence.SourceRunStatus.Failed => "con error",
        Infrastructure.Persistence.SourceRunStatus.Cancelled => "cancelada",
        Infrastructure.Persistence.SourceRunStatus.Skipped => "omitida",
        _ => string.Empty,
    };

    public static string Date(DateTime? utc) => utc?.ToLocalTime().ToString("dd/MM/yyyy", Culture) ?? "—";

    public static string Short(string? text, int length) =>
        string.IsNullOrEmpty(text) ? "—" : text.Length <= length ? text : text[..length].TrimEnd() + "…";

    public static MudBlazor.Color EligibilityColor(EligibilityStatus status) => status switch
    {
        EligibilityStatus.Likely => MudBlazor.Color.Success,
        EligibilityStatus.Unlikely => MudBlazor.Color.Error,
        _ => MudBlazor.Color.Default,
    };

    public static string EligibilityLabel(EligibilityStatus status, bool fromAnalysis) => (status, fromAnalysis) switch
    {
        (EligibilityStatus.Likely, true) => "Elegible",
        (EligibilityStatus.Unlikely, true) => "No elegible",
        (_, true) => "Incierta",
        (EligibilityStatus.Likely, false) => "Prob. elegible",
        (EligibilityStatus.Unlikely, false) => "Prob. no elegible",
        _ => "Sin datos",
    };
}
