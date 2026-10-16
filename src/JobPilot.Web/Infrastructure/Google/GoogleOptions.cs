namespace JobPilot.Web.Infrastructure.Google;

/// <summary>Google OAuth client (from user-secrets) and API endpoints (configurable so tests can use a fake server).</summary>
public sealed class GoogleOptions
{
    public const string SectionName = "Google";

    /// <summary>OAuth client of type "Web application" created by the owner in Google Cloud. Secret: never committed.</summary>
    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    public Uri AuthorizationEndpoint { get; set; } = new("https://accounts.google.com/o/oauth2/v2/auth");

    public Uri TokenEndpoint { get; set; } = new("https://oauth2.googleapis.com/token");

    public Uri RevocationEndpoint { get; set; } = new("https://oauth2.googleapis.com/revoke");

    public Uri GmailBaseUrl { get; set; } = new("https://gmail.googleapis.com/gmail/v1/");

    public Uri CalendarBaseUrl { get; set; } = new("https://www.googleapis.com/calendar/v3/");

    /// <summary>Name of the secondary calendar the app creates for interviews.</summary>
    public string CalendarName { get; set; } = "JobSearch Copilot";

    public const string CallbackPath = "/google/callback";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}

/// <summary>
/// The only scopes the app requests (checked on 2026-10-02 against the Gmail and Calendar discovery documents).
/// gmail.compose is the narrowest scope that creates drafts; it also allows sending, which the app never does
/// (enforced by <c>NoSendingTests</c>).
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

/// <summary>What the owner chooses to connect: everything, or only the JobSearch Copilot calendar.</summary>
public enum GoogleAccess
{
    Full,
    CalendarOnly,
}
