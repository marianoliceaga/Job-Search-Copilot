namespace JobPilot.Desktop.Core;

/// <summary>Names shared by the desktop host, its installer (<c>installer/JobSearchCopilot.iss</c>) and the tests.</summary>
public static class DesktopIdentity
{
    public const string ProductName = "JobSearch Copilot";

    /// <summary>File name of the host executable (the project's <c>AssemblyName</c>).</summary>
    public const string ExecutableName = "JobSearchCopilot.exe";

    /// <summary>Folder, next to the host, that holds the published web app.</summary>
    public const string ServerFolder = "server";

    /// <summary>The published web app's executable inside <see cref="ServerFolder"/>.</summary>
    public const string ServerExecutableName = "JobPilot.Web.exe";

    /// <summary>Held while the window is open: a second start activates it, and the installer waits for it to close.</summary>
    public const string MutexName = "JobSearchCopilotDesktop";

    /// <summary>Signalled by a second start so that the running window comes to the front.</summary>
    public const string ActivateEventName = "JobSearchCopilotDesktop.Activate";

    /// <summary>The web app's default port (<c>JobPilot:Port</c>); the Google OAuth redirect URI uses it.</summary>
    public const int DefaultPort = 5080;

    /// <summary>
    /// The port to open: the web app reads <c>JobPilot:Port</c> from configuration, and the environment variable
    /// <c>JobPilot__Port</c> (inherited by the server process) overrides it; otherwise the default.
    /// </summary>
    public static int Port(Func<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        return int.TryParse(environment("JobPilot__Port"), out var port) && port is > 0 and < 65536 ? port : DefaultPort;
    }
}
