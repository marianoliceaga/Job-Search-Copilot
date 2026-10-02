using System.Globalization;
using System.Text.RegularExpressions;

namespace JobPilot.Domain.Cv;

/// <summary>A CV date: a year, or a year and month (CVs rarely state days). Text form: <c>YYYY</c> or <c>YYYY-MM</c>.</summary>
public readonly partial record struct PartialDate : IComparable<PartialDate>
{
    public PartialDate(int year, int? month = null)
    {
        if (year is < 1900 or > 2200)
        {
            throw new ArgumentOutOfRangeException(nameof(year), year, "Year must be between 1900 and 2200.");
        }

        if (month is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(month), month, "Month must be between 1 and 12.");
        }

        Year = year;
        Month = month;
    }

    public int Year { get; }

    public int? Month { get; }

    public static bool TryParse(string? text, out PartialDate date)
    {
        date = default;
        var match = text is null ? Match.Empty : FormatRegex().Match(text.Trim());
        if (!match.Success)
        {
            return false;
        }

        var year = int.Parse(match.Groups["year"].Value, CultureInfo.InvariantCulture);
        int? month = match.Groups["month"].Success ? int.Parse(match.Groups["month"].Value, CultureInfo.InvariantCulture) : null;
        if (year is < 1900 or > 2200 || month is < 1 or > 12)
        {
            return false;
        }

        date = new PartialDate(year, month);
        return true;
    }

    public static PartialDate Parse(string text) =>
        TryParse(text, out var date) ? date : throw new FormatException($"'{text}' is not a valid date (expected YYYY or YYYY-MM).");

    /// <summary>Orders by year, then month; a year-only date sorts before any month of that year.</summary>
    public int CompareTo(PartialDate other)
    {
        var byYear = Year.CompareTo(other.Year);
        return byYear != 0 ? byYear : (Month ?? 0).CompareTo(other.Month ?? 0);
    }

    public static bool operator <(PartialDate left, PartialDate right) => left.CompareTo(right) < 0;

    public static bool operator >(PartialDate left, PartialDate right) => left.CompareTo(right) > 0;

    public static bool operator <=(PartialDate left, PartialDate right) => left.CompareTo(right) <= 0;

    public static bool operator >=(PartialDate left, PartialDate right) => left.CompareTo(right) >= 0;

    public override string ToString() =>
        Month is { } month
            ? string.Create(CultureInfo.InvariantCulture, $"{Year:D4}-{month:D2}")
            : Year.ToString("D4", CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^(?<year>\d{4})(?:-(?<month>\d{2}))?$")]
    private static partial Regex FormatRegex();
}
