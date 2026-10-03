using System.Globalization;
using System.Text.Json;
using JobPilot.Domain.Jobs;

namespace JobPilot.Web.Features.Jobs.Sources;

/// <summary>Helpers for reading loosely-typed source payloads.</summary>
internal static class SourceParsing
{
    public static string? String(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!.Trim()
            : null;

    public static decimal? Number(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.GetDecimal() > 0 => value.GetDecimal(),
            JsonValueKind.String when decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) && parsed > 0 => parsed,
            _ => null,
        };
    }

    public static IReadOnlyList<string> Strings(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!.Trim()).Where(v => v.Length > 0).ToList()
            : value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()) ? [value.GetString()!.Trim()] : [];

    public static DateTime? Date(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String when DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date) => date.UtcDateTime,
            JsonValueKind.Number when value.TryGetInt64(out var epoch) => epoch > 100_000_000_000
                ? DateTimeOffset.FromUnixTimeMilliseconds(epoch).UtcDateTime
                : DateTimeOffset.FromUnixTimeSeconds(epoch).UtcDateTime,
            _ => null,
        };
    }

    public static SalaryPeriod? Period(string? text) => text?.Trim().ToLowerInvariant() switch
    {
        null or "" => null,
        "year" or "yearly" or "annual" or "annually" or "per-year-salary" or "1 year" or "per year" => SalaryPeriod.Year,
        "month" or "monthly" or "per-month-salary" or "1 month" or "per month" => SalaryPeriod.Month,
        "week" or "weekly" or "per-week-salary" or "1 week" => SalaryPeriod.Week,
        "day" or "daily" or "per-day-salary" or "1 day" => SalaryPeriod.Day,
        "hour" or "hourly" or "per-hour-wage" or "per-hour-salary" or "1 hour" => SalaryPeriod.Hour,
        _ => null,
    };

    public static string? SalaryText(decimal? min, decimal? max, string? currency, SalaryPeriod? period)
    {
        if (min is null && max is null)
        {
            return null;
        }

        var range = (min, max) switch
        {
            ({ } a, { } b) when a != b => $"{a:N0} – {b:N0}",
            ({ } a, _) => $"{a:N0}",
            (_, { } b) => $"{b:N0}",
            _ => string.Empty,
        };
        return string.Join(' ', new[] { currency, range, period is null ? null : "/ " + period.ToString()!.ToLowerInvariant() }.Where(s => !string.IsNullOrEmpty(s)));
    }
}
