using System.Globalization;
using System.Text.RegularExpressions;

namespace JobPilot.Domain.Mail;

public enum RoleFit
{
    Weak,
    Maybe,
    Strong,
}

/// <summary>A distinct role (same title and company) with every listing that showed it, scored by the owner's rules.</summary>
public sealed record ScoredRole(
    string Key,
    AlertListing Best,
    IReadOnlyList<AlertListing> Listings,
    int Score,
    RoleFit Fit,
    IReadOnlyList<string> Why,
    decimal? PayMonthlyUsd,
    string? FilteredBy)
{
    public IReadOnlyList<string> Locations => Listings.Select(l => l.Location).Where(l => l.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}

public sealed record RolesResult(IReadOnlyList<ScoredRole> Shown, IReadOnlyList<ScoredRole> Filtered, int Listings)
{
    public static readonly RolesResult Empty = new([], [], 0);
}

/// <summary>
/// Collapses duplicate listings (same title and company across cities and alerts) and scores each role. Every rule that
/// fires is named in <see cref="ScoredRole.Why"/>, so a score always traces back to its rules.
/// </summary>
public sealed partial class RoleScorer
{
    private static readonly string[] KnownFields = ["title", "company", "location", "workplace", "flags", "alert"];
    private static readonly string[] KnownPay = ["atOrAboveFloor", "belowFloor", "unknown"];

    private readonly RoleScoringRules _rules;
    private readonly List<(ScoreRule Rule, Condition[] When)> _scoreRules;
    private readonly List<(RoleFilter Filter, Condition[] When)> _filters;

    private RoleScorer(RoleScoringRules rules, List<(ScoreRule, Condition[])> scoreRules, List<(RoleFilter, Condition[])> filters)
    {
        _rules = rules;
        _scoreRules = scoreRules;
        _filters = filters;
    }

    public static RoleScorer Compile(RoleScoringRules rules, SetResolver sets)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var scoreRules = rules.Rules.Select((r, i) =>
            (r, r.When.Select((c, j) => CompileCondition(c, sets, $"scoring.rules[{i}] ({r.Id}).when[{j}]")).ToArray())).ToList();
        var filters = rules.Filters.Select((f, i) =>
            (f, f.When.Select((c, j) => CompileCondition(c, sets, $"scoring.filters[{i}] ({f.Id}).when[{j}]")).ToArray())).ToList();
        return new RoleScorer(rules, scoreRules, filters);
    }

    /// <summary>Same title and company is the same role, whatever city or alert it came through.</summary>
    public static string DedupeKey(AlertListing listing) => $"{Squash(listing.Title)}|{Squash(listing.Company)}";

    public RolesResult Score(IReadOnlyList<AlertListing> listings)
    {
        var roles = listings
            .GroupBy(DedupeKey)
            .Select(group =>
            {
                var scored = group.Select(ScoreListing).ToList();
                // The listing that scores best represents the role: a remote copy beats an on-site one elsewhere.
                var best = scored.OrderByDescending(s => s.FilteredBy is null).ThenByDescending(s => s.Score).ThenBy(s => s.Listing.Received).First();
                var filteredBy = best.FilteredBy ?? (best.Score < _rules.Thresholds.Weak ? _rules.BelowLevelChip : null);
                return new ScoredRole(group.Key, best.Listing, group.ToList(), best.Score, FitFor(best.Score), best.Why, best.PayMonthly, filteredBy);
            })
            .ToList();

        var shown = roles.Where(r => r.FilteredBy is null)
            .OrderByDescending(r => r.Fit).ThenByDescending(r => r.Score).ThenByDescending(r => r.PayMonthlyUsd ?? 0).ThenBy(r => r.Best.Title, StringComparer.Ordinal)
            .ToList();
        var filtered = roles.Where(r => r.FilteredBy is not null).OrderBy(r => r.FilteredBy, StringComparer.Ordinal).ThenBy(r => r.Best.Title, StringComparer.Ordinal).ToList();
        return new RolesResult(shown, filtered, listings.Count);
    }

    public static string FormatMoney(decimal? usd) => usd switch
    {
        null => string.Empty,
        >= 1000m => "USD " + (usd.Value / 1000m).ToString("0.#", CultureInfo.InvariantCulture) + "K",
        _ => "USD " + usd.Value.ToString("0", CultureInfo.InvariantCulture),
    };

    private static Condition CompileCondition(RoleCondition c, SetResolver sets, string context)
    {
        var fields = (c.Field ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (c.Pay is null && fields.Length == 0)
        {
            throw new MailRulesException($"{context}: falta \"field\" o \"pay\".");
        }

        if (fields.FirstOrDefault(f => !KnownFields.Contains(f)) is { } unknown)
        {
            throw new MailRulesException($"{context}: campo desconocido \"{unknown}\" (usá {string.Join(", ", KnownFields)}).");
        }

        if (c.Pay is not null && !KnownPay.Contains(c.Pay))
        {
            throw new MailRulesException($"{context}: prueba de pago desconocida \"{c.Pay}\" (usá {string.Join(", ", KnownPay)}).");
        }

        return new Condition(fields, TermSet.Compile(sets.Expand(c.Any, context), context), TermSet.Compile(sets.Expand(c.None, context), context), c.Empty, c.Pay);
    }

    private ListingScore ScoreListing(AlertListing listing)
    {
        var payMonthly = MonthlyUsd(listing.Pay);
        var fields = FieldValues(listing);
        var groups = new Dictionary<string, int>(StringComparer.Ordinal);
        var why = new List<string>();
        var score = 0;
        foreach (var (rule, when) in _scoreRules)
        {
            if (!when.All(c => Holds(c, fields, payMonthly)))
            {
                continue;
            }

            score += rule.Weight;
            groups[rule.Group] = groups.GetValueOrDefault(rule.Group) + rule.Weight;
            why.Add(MailTemplate.Fill(rule.Why, new Dictionary<string, string>(StringComparer.Ordinal) { ["payMonthly"] = FormatMoney(payMonthly) }));
        }

        string? filteredBy = null;
        foreach (var (filter, when) in _filters)
        {
            if (!when.All(c => Holds(c, fields, payMonthly)))
            {
                continue;
            }

            if (filter.UnlessGroupAtLeast.Any(kv => groups.GetValueOrDefault(kv.Key) >= kv.Value))
            {
                why.Add($"se mantiene pese a: {filter.Chip.ToLowerInvariant()}");
                continue;
            }

            filteredBy = filter.Chip;
            break;
        }

        return new ListingScore(listing, score, why, payMonthly, filteredBy);
    }

    private bool Holds(Condition c, IReadOnlyDictionary<string, string> fields, decimal? payMonthly)
    {
        if (c.Pay is not null)
        {
            var floor = _rules.Pay.MonthlyFloorUsd;
            var payHolds = c.Pay switch
            {
                "atOrAboveFloor" => floor is not null && payMonthly >= floor,
                "belowFloor" => floor is not null && payMonthly < floor,
                _ => payMonthly is null,
            };
            if (!payHolds)
            {
                return false;
            }
        }

        if (c.Fields.Length == 0)
        {
            return true;
        }

        var text = string.Join(" | ", c.Fields.Select(f => fields[f]).Where(v => v.Length > 0));
        return (c.Empty is not { } empty || empty == (text.Length == 0))
            && (c.Any.IsEmpty || c.Any.IsMatch(text))
            && (c.None.IsEmpty || !c.None.IsMatch(text));
    }

    /// <summary>Top of the stated range per month in USD; null without a stated range, a known rate or a plausible value.</summary>
    private decimal? MonthlyUsd(AlertPay? pay)
    {
        if (pay is null || !_rules.Pay.UsdPerUnit.TryGetValue(pay.Currency, out var rate))
        {
            return null;
        }

        var monthly = pay.Period switch
        {
            "year" => pay.High / 12m,
            "month" => pay.High,
            _ => pay.High * _rules.Pay.HoursPerMonth,
        } * rate;
        return monthly > _rules.Pay.MaxPlausibleMonthlyUsd ? null : Math.Round(monthly, 0);
    }

    private RoleFit FitFor(int score) =>
        score >= _rules.Thresholds.Strong ? RoleFit.Strong
        : score >= _rules.Thresholds.Maybe ? RoleFit.Maybe
        : RoleFit.Weak;

    private static Dictionary<string, string> FieldValues(AlertListing l) => new(StringComparer.Ordinal)
    {
        ["title"] = MailText.Normalize(l.Title),
        ["company"] = MailText.Normalize(l.Company),
        ["location"] = MailText.Normalize(l.Location),
        ["workplace"] = l.Workplace,
        ["flags"] = MailText.Normalize(string.Join(" | ", l.Flags)),
        ["alert"] = MailText.Normalize(l.AlertName),
    };

    private static string Squash(string value) => NonWordRegex().Replace(MailText.Normalize(value), " ").Trim();

    [GeneratedRegex(@"[^\p{L}\p{N}#+.]+")]
    private static partial Regex NonWordRegex();

    private sealed record Condition(string[] Fields, TermSet Any, TermSet None, bool? Empty, string? Pay);

    private sealed record ListingScore(AlertListing Listing, int Score, List<string> Why, decimal? PayMonthly, string? FilteredBy);
}
