using Ical.Net;
using Ical.Net.DataTypes;
using JobPilot.Domain.Mail;

namespace JobPilot.Mobile.Core.Infrastructure.Google;

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
            var me = ownEmail is null ? null : ev.Attendees.FirstOrDefault(a => string.Equals(Address(a.Value), ownEmail, StringComparison.OrdinalIgnoreCase));
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
                ev.Organizer is { } o ? new MailPerson(o.CommonName ?? string.Empty, Address(o.Value)) : null,
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

    /// <summary>The address of an attendee or organizer URI (read only; the app never opens or creates such links).</summary>
    private static string Address(Uri? uri)
    {
        if (uri is null)
        {
            return string.Empty;
        }

        return uri.IsAbsoluteUri && uri.Scheme == Uri.UriSchemeMailto && uri.UserInfo.Length > 0
            ? Uri.UnescapeDataString($"{uri.UserInfo}@{uri.Host}")
            : Uri.UnescapeDataString(uri.OriginalString);
    }
}
