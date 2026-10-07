using System.Text.RegularExpressions;

namespace JobPilot.Mobile.Core.Infrastructure.Notifications;

/// <summary>
/// Pages the host asks the UI to open (a tapped notification). Only app routes of an application are accepted: the
/// launcher activity is exported, so the value may come from another app.
/// </summary>
public sealed partial class AppNavigationRequests
{
    private readonly Lock _lock = new();
    private string? _pending;

    /// <summary>Raised when a route is requested while the UI is running.</summary>
    public event Action? Requested;

    public static bool IsAllowed(string? route) => route is not null && AllowedRoute().IsMatch(route);

    public void Request(string? route)
    {
        if (!IsAllowed(route))
        {
            return;
        }

        lock (_lock)
        {
            _pending = route;
        }

        Requested?.Invoke();
    }

    /// <summary>Returns the pending route once.</summary>
    public string? Take()
    {
        lock (_lock)
        {
            var route = _pending;
            _pending = null;
            return route;
        }
    }

    [GeneratedRegex("^/pipeline/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$")]
    private static partial Regex AllowedRoute();
}
