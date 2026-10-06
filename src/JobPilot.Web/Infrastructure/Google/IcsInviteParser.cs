using Ical.Net;
using Ical.Net.DataTypes;
using JobPilot.Domain.Mail;

namespace JobPilot.Web.Infrastructure.Google;

/// <summary>Reads the VEVENTs of an .ics invite found in a message. Invalid calendars yield nothing (the thread is still sorted).</summary>
public static class IcsInviteParser
{
    /// <param name="ownEmail">The owner's address, to read their PARTSTAT; null when unknown.</param>
    /// <param name="zone">Floating times (no zone) are read in the owner's zone.</param>
    public static IReadOnlyList<MailInvite> Parse(string ics, string? ownEmail, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        Calendar? calendar;
        try
        {
            calendar = Calendar.Load(ics);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return [];
        }

        if (calendar is null)
        {
            return [];
        }

        var method = (calendar.Method ?? string.Empty).ToUpperInvariant();
        return calendar.Events.Select(ev =>
        {
            var me = ownEmail is null ? null : ev.Attendees.FirstOrDefault(a => string.Equals(Mailto(a.Value), ownEmail, StringComparison.OrdinalIgnoreCase));
            var start = ToOffset(ev.DtStart, zone);
            var end = ToOffset(ev.DtEnd, zone) ?? (start is { } s && ev.Duration is { } d ? s + d.ToTimeSpanUnspecified() : null);
            return new MailInvite(
                method,
                ev.Uid ?? string.Empty,
                ev.Sequence,
                ev.Summary,
                start,
                end,
                ev.IsAllDay,
                ev.Organizer is { } o ? new MailPerson(o.CommonName ?? string.Empty, Mailto(o.Value)) : null,
                me?.ParticipationStatus?.ToUpperInvariant());
        }).ToList();
    }

    private static DateTimeOffset? ToOffset(CalDateTime? value, TimeZoneInfo zone)
    {
        if (value is null)
        {
            return null;
        }

        if (value.IsFloating)
        {
            var local = value.Value;
            return new DateTimeOffset(local, zone.GetUtcOffset(local));
        }

        return new DateTimeOffset(DateTime.SpecifyKind(value.AsUtc, DateTimeKind.Utc));
    }

    private static string Mailto(Uri? uri)
    {
        var text = Uri.UnescapeDataString(uri?.OriginalString ?? string.Empty);
        return text.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ? text[7..] : text;
    }
}
