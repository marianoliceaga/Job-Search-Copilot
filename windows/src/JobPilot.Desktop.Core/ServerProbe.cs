namespace JobPilot.Desktop.Core;

/// <summary>What answers on the app's port.</summary>
public enum ServerState
{
    /// <summary>Nothing answers yet (connection refused or no reply).</summary>
    NotRunning,

    /// <summary>The JobSearch Copilot web app (started by this window, or already running from <c>dotnet run</c>).</summary>
    JobSearchCopilot,

    /// <summary>Another program uses the port.</summary>
    OtherProgram,
}

/// <summary>Recognizes the web app by its own script (<c>/app.js</c> defines <c>window.jobPilot</c>).</summary>
public sealed class ServerProbe(HttpClient http)
{
    internal const string ScriptPath = "app.js";
    internal const string Marker = "window.jobPilot";

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(3);

    /// <summary>A client that never goes through a proxy: the app only listens on the loopback interface.</summary>
    public static HttpClient CreateClient() => new(new SocketsHttpHandler { UseProxy = false, ConnectTimeout = RequestTimeout });

    public async Task<ServerState> ProbeAsync(Uri baseUri, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(baseUri);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        try
        {
            using var response = await http.GetAsync(new Uri(baseUri, ScriptPath), timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                return ServerState.OtherProgram;
            }

            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            return body.Contains(Marker, StringComparison.Ordinal) ? ServerState.JobSearchCopilot : ServerState.OtherProgram;
        }
        catch (HttpRequestException)
        {
            return ServerState.NotRunning;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ServerState.NotRunning;
        }
    }
}
