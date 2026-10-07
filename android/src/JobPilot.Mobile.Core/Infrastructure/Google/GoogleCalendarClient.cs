using JobPilot.Mobile.Core.Infrastructure.Persistence;
using Microsoft.Extensions.Options;

namespace JobPilot.Mobile.Core.Infrastructure.Google;

/// <param name="StartUtc">Interview start; end = start + duration.</param>
public sealed record CalendarEventRequest(string Summary, string? Description, DateTime StartUtc, DateTime EndUtc);

/// <summary>
/// Google Calendar REST (v3) with the calendar.app.created scope: the app creates its own "JobPilot" calendar and only
/// touches events there. Events are created after the owner confirms them in the UI.
/// </summary>
public sealed class GoogleCalendarClient(HttpClient http, GoogleAuthService auth, SettingsDocumentStore documents, IOptionsMonitor<GoogleOptions> options, TimeProvider time)
    : GoogleApiClient(http, auth)
{
    private const string CalendarKey = "google-calendar";

    /// <summary>The id of the JobPilot calendar, created on first use (and again if it was deleted in Google).</summary>
    public async Task<GoogleResult<string>> EnsureCalendarAsync(CancellationToken cancellationToken)
    {
        if (await documents.GetAsync<StoredCalendar>(CalendarKey, cancellationToken) is { } stored)
        {
            var existing = await SendAsync<CalendarResource>(HttpMethod.Get, $"calendars/{Uri.EscapeDataString(stored.Id)}", null, GoogleScopes.CalendarAppCreated, cancellationToken);
            if (existing.IsSuccess)
            {
                return GoogleResult<string>.Ok(stored.Id);
            }

            if (existing.Error != NotFound)
            {
                return GoogleResult<string>.Fail(existing.Error!);
            }
        }

        var created = await SendAsync<CalendarResource>(HttpMethod.Post, "calendars",
            new { summary = options.CurrentValue.CalendarName, description = "Entrevistas creadas por JobPilot", timeZone = IanaTimeZone(time.LocalTimeZone) },
            GoogleScopes.CalendarAppCreated, cancellationToken);
        if (created.Value is not { } calendar)
        {
            return GoogleResult<string>.Fail(created.Error!);
        }

        await documents.SetAsync(CalendarKey, new StoredCalendar(calendar.Id), cancellationToken);
        return GoogleResult<string>.Ok(calendar.Id);
    }

    /// <returns>The Google event id.</returns>
    public async Task<GoogleResult<string>> CreateEventAsync(CalendarEventRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var calendar = await EnsureCalendarAsync(cancellationToken);
        if (calendar.Value is not { } calendarId)
        {
            return GoogleResult<string>.Fail(calendar.Error!);
        }

        var body = new
        {
            summary = request.Summary,
            description = request.Description,
            start = new { dateTime = request.StartUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture) },
            end = new { dateTime = request.EndUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture) },
        };
        var created = await SendAsync<EventResource>(HttpMethod.Post, $"calendars/{Uri.EscapeDataString(calendarId)}/events", body, GoogleScopes.CalendarAppCreated, cancellationToken);
        return created.Value is { } resource ? GoogleResult<string>.Ok(resource.Id) : GoogleResult<string>.Fail(created.Error!);
    }

    public async Task<GoogleResult<bool>> DeleteEventAsync(string eventId, CancellationToken cancellationToken)
    {
        if (await documents.GetAsync<StoredCalendar>(CalendarKey, cancellationToken) is not { } stored)
        {
            return GoogleResult<bool>.Ok(true);
        }

        var result = await SendAsync<bool>(HttpMethod.Delete, $"calendars/{Uri.EscapeDataString(stored.Id)}/events/{Uri.EscapeDataString(eventId)}", null, GoogleScopes.CalendarAppCreated, cancellationToken);
        // Already deleted in Google counts as done.
        return result.Error == NotFound ? GoogleResult<bool>.Ok(true) : result;
    }

    internal static string IanaTimeZone(TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        if (zone.HasIanaId)
        {
            return zone.Id;
        }

        return TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out var iana) ? iana : "UTC";
    }

    private sealed record StoredCalendar(string Id);

    private sealed record CalendarResource(string Id);

    private sealed record EventResource(string Id);
}
