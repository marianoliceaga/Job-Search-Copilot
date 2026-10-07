namespace JobPilot.Mobile.Core.Infrastructure.Google;

/// <summary>
/// Google API endpoints (configurable so tests can use a fake server). There is no client id or secret on the phone:
/// Google identifies the app by its package name and signing certificate (an "Android" OAuth client in Google Cloud).
/// </summary>
public sealed class GoogleOptions
{
    public const string SectionName = "Google";

    public Uri RevocationEndpoint { get; set; } = new("https://oauth2.googleapis.com/revoke");

    public Uri GmailBaseUrl { get; set; } = new("https://gmail.googleapis.com/gmail/v1/");

    public Uri CalendarBaseUrl { get; set; } = new("https://www.googleapis.com/calendar/v3/");

    /// <summary>Name of the secondary calendar the app creates for interviews.</summary>
    public string CalendarName { get; set; } = "JobPilot";
}

/// <summary>
/// The only scopes the app requests, the same as the web app's (checked on 2026-10-02 against the Gmail and Calendar
/// discovery documents). gmail.compose is the narrowest scope that creates drafts; it also allows sending, which the app
/// never does (enforced by <c>NoSendingTests</c>).
/// </summary>
public static class GoogleScopes
{
    public const string GmailReadonly = "https://www.googleapis.com/auth/gmail.readonly";

    public const string GmailCompose = "https://www.googleapis.com/auth/gmail.compose";

    /// <summary>Secondary calendars created by the app and their events only; no access to the primary calendar.</summary>
    public const string CalendarAppCreated = "https://www.googleapis.com/auth/calendar.app.created";

    public static IReadOnlyList<string> All { get; } = [GmailReadonly, GmailCompose, CalendarAppCreated];

    /// <summary>
    /// Calendar only: for accounts where Gmail access is blocked, e.g. Google's Advanced Protection Program, which only
    /// lets verified third-party apps read sensitive data such as Gmail.
    /// </summary>
    public static IReadOnlyList<string> CalendarOnly { get; } = [CalendarAppCreated];

    public static IReadOnlyList<string> For(GoogleAccess access) => access == GoogleAccess.CalendarOnly ? CalendarOnly : All;
}

/// <summary>What the owner chooses to connect: everything, or only the JobPilot calendar.</summary>
public enum GoogleAccess
{
    Full,
    CalendarOnly,
}
