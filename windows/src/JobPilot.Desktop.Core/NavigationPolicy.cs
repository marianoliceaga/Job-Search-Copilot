namespace JobPilot.Desktop.Core;

public enum NavigationAction
{
    /// <summary>Stays in the window (pages of the app, downloads of exported files).</summary>
    Allow,

    /// <summary>Opens in the owner's default browser instead of the window.</summary>
    OpenInBrowser,

    /// <summary>Ignored (other schemes, such as <c>file:</c> or <c>javascript:</c>).</summary>
    Block,
}

/// <summary>
/// What the window does with a navigation or a new-window request. Only the app's own pages load in the window: third-party
/// pages open in the browser, and so does the Google connection, because Google does not allow signing in from embedded
/// browsers (WebView2 is one).
/// </summary>
public static class NavigationPolicy
{
    /// <summary>The web app's endpoint that starts the Google OAuth flow (<c>GoogleServiceCollectionExtensions</c>).</summary>
    internal const string GoogleConnectPath = "/google/connect";

    public static NavigationAction Decide(string? target, Uri appBase)
    {
        ArgumentNullException.ThrowIfNull(appBase);
        if (!Uri.TryCreate(target, UriKind.Absolute, out var uri))
        {
            return NavigationAction.Block;
        }

        if (uri.Scheme is "about" or "blob")
        {
            return NavigationAction.Allow;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return NavigationAction.Block;
        }

        if (!IsApp(uri, appBase))
        {
            return NavigationAction.OpenInBrowser;
        }

        return IsGoogleConnect(uri) ? NavigationAction.OpenInBrowser : NavigationAction.Allow;
    }

    public static bool IsGoogleConnect(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return uri.AbsolutePath.Equals(GoogleConnectPath, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsApp(Uri uri, Uri appBase) =>
        uri.Scheme == Uri.UriSchemeHttp
        && uri.Port == appBase.Port
        && (uri.IsLoopback || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase));
}
