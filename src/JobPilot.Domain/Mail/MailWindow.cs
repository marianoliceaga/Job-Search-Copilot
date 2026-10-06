namespace JobPilot.Domain.Mail;

/// <summary>
/// How far back the inbox query reaches: since the last successful check, so a missed day is not lost, capped so a long
/// gap does not flood the brief; the last 24 hours the first time.
/// </summary>
public sealed record MailWindow(DateTimeOffset Since, bool FromLastCheck, string QueryFragment)
{
    public static MailWindow Compute(DateTimeOffset now, DateTimeOffset? lastSuccessfulCheck, int capDays)
    {
        if (lastSuccessfulCheck is null)
        {
            return new MailWindow(now.AddDays(-1), FromLastCheck: false, "newer_than:1d");
        }

        var cap = now.AddDays(-Math.Max(1, capDays));
        var since = lastSuccessfulCheck.Value < cap ? cap : lastSuccessfulCheck.Value;
        // Gmail's after: accepts epoch seconds, which avoids its date-only, Pacific-time reading.
        return new MailWindow(since, FromLastCheck: true, $"after:{since.ToUnixTimeSeconds()}");
    }

    /// <summary>The query with {since} replaced by this window.</summary>
    public string Apply(string query) => query.Replace("{since}", QueryFragment, StringComparison.Ordinal);
}
