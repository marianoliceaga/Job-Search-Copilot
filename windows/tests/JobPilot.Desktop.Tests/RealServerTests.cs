using System.Net;
using System.Net.Sockets;
using System.Reflection;
using JobPilot.Desktop.Core;

namespace JobPilot.Desktop.Tests;

/// <summary>
/// Starts the real web app (its build output, in Development so static assets come from the project) with a temporary data
/// folder and a free port: the probe must recognize it, and the window's Google rule must match the app's endpoint.
/// </summary>
public sealed class RealServerTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly string _data = Directory.CreateTempSubdirectory("jsc-desktop-data-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_data, recursive: true);
        }
        catch (IOException)
        {
            // SQLite may release the file a moment after the process exits; the temp folder is cleaned by the OS.
        }
    }

    private static string WebAppExecutable()
    {
        var folder = typeof(RealServerTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == "WebAppDirectory").Value!;
        return Path.Combine(folder, OperatingSystem.IsWindows() ? "JobPilot.Web.exe" : "JobPilot.Web");
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    [Fact]
    public async Task The_window_starts_recognizes_and_stops_the_web_app()
    {
        var port = FreePort();
        var options = new ServerOptions(WebAppExecutable(), port)
        {
            Environment = "Development",
            ExtraEnvironment = new Dictionary<string, string>
            {
                ["JobPilot__Port"] = port.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["JobPilot__DataDirectory"] = _data,
                ["Gemini__ApiKey"] = string.Empty,
                ["Google__ClientId"] = string.Empty,
                ["Google__ClientSecret"] = string.Empty,
            },
        };
        using var http = ServerProbe.CreateClient();
        var probe = new ServerProbe(http);
        using var host = new ServerHost(options, probe, SystemServerProcess.Start);

        var result = await host.StartAsync(Ct);
        Assert.True(result.IsSuccess, $"{result.Error}\n{result.Details}");
        Assert.False(result.Attached);

        // The Google connection the window sends to the browser is the app's endpoint (it redirects to Configuración
        // here because the test has no OAuth client).
        using var noRedirect = new HttpClient(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false });
        var connect = new Uri(options.BaseUri, "google/connect");
        using var response = await noRedirect.GetAsync(connect, Ct);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(NavigationAction.OpenInBrowser, NavigationPolicy.Decide(connect.AbsoluteUri, options.BaseUri));

        // A second window attaches to the running server instead of starting another one.
        using var second = new ServerHost(options, probe, (_, _) => throw new InvalidOperationException("Must not start."));
        Assert.True((await second.StartAsync(Ct)).Attached);

        host.Stop();
        Assert.Equal(ServerState.NotRunning, await probe.ProbeAsync(options.BaseUri, Ct));
    }
}
