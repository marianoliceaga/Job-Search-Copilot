using System.ComponentModel;
using System.Diagnostics;

namespace JobPilot.Desktop.Core;

/// <summary>How the window reaches the web app.</summary>
public sealed record ServerOptions(string ExecutablePath, int Port)
{
    /// <summary>The web app's environment. The published app runs in Production (static assets come from the publish output).</summary>
    public string Environment { get; init; } = "Production";

    /// <summary>Extra variables for the server process (tests use them for the data folder and port).</summary>
    public IReadOnlyDictionary<string, string> ExtraEnvironment { get; init; } = new Dictionary<string, string>();

    /// <summary>The first start applies the database migrations, so it can take a while on a slow disk.</summary>
    public TimeSpan StartTimeout { get; init; } = TimeSpan.FromSeconds(90);

    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(250);

    public Uri BaseUri => new($"http://localhost:{Port}/");
}

/// <summary>Outcome of <see cref="ServerHost.StartAsync"/>. <see cref="Details"/> holds the server's last output lines.</summary>
public sealed record ServerStartResult(bool IsSuccess, bool Attached, string? Error, string? Details)
{
    public static ServerStartResult Started() => new(true, false, null, null);

    public static ServerStartResult AttachedToRunning() => new(true, true, null, null);

    public static ServerStartResult Failure(string error, string? details = null) =>
        new(false, false, error, string.IsNullOrWhiteSpace(details) ? null : details);
}

/// <summary>
/// Starts the published web app as a child process (loopback only, as always) and waits until it answers. When the app is
/// already running on the port (for example from <c>dotnet run</c>), the window uses it and leaves it running on close.
/// </summary>
public sealed class ServerHost(
    ServerOptions options,
    ServerProbe probe,
    Func<ProcessStartInfo, ServerOutput, IServerProcess> start,
    TimeProvider? time = null) : IDisposable
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private IServerProcess? _process;

    public ServerOptions Options => options;

    public ServerOutput Output { get; } = new();

    /// <summary>True when this window started the server (and stops it on close).</summary>
    public bool OwnsServer => _process is not null;

    public async Task<ServerStartResult> StartAsync(CancellationToken cancellationToken)
    {
        switch (await probe.ProbeAsync(options.BaseUri, cancellationToken))
        {
            case ServerState.JobSearchCopilot:
                return ServerStartResult.AttachedToRunning();
            case ServerState.OtherProgram:
                return ServerStartResult.Failure(
                    $"Otro programa está usando el puerto {options.Port}. Cerralo, o elegí otro puerto con la variable de entorno " +
                    $"JobPilot__Port (si usás Google, el URI de redirección del cliente OAuth tiene que usar ese puerto).");
        }

        if (!File.Exists(options.ExecutablePath))
        {
            return ServerStartResult.Failure(
                $"No se encontró el servidor de la app en \"{options.ExecutablePath}\". Reinstalá {DesktopIdentity.ProductName}.");
        }

        Stop();
        var startInfo = new ProcessStartInfo(options.ExecutablePath)
        {
            // The web app reads appsettings.json, prompts and seeds relative to its own folder.
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(options.ExecutablePath))!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = options.Environment;
        foreach (var (name, value) in options.ExtraEnvironment)
        {
            startInfo.Environment[name] = value;
        }

        try
        {
            _process = start(startInfo, Output);
        }
        catch (Win32Exception ex)
        {
            return ServerStartResult.Failure($"No se pudo iniciar el servidor de la app: {ex.Message}");
        }

        var deadline = _time.GetUtcNow() + options.StartTimeout;
        while (true)
        {
            if (_process.HasExited)
            {
                var exitCode = _process.ExitCode;
                Stop();
                return ServerStartResult.Failure($"El servidor de la app se cerró al iniciar (código {exitCode}).", Output.Text);
            }

            if (await probe.ProbeAsync(options.BaseUri, cancellationToken) == ServerState.JobSearchCopilot)
            {
                return ServerStartResult.Started();
            }

            if (_time.GetUtcNow() > deadline)
            {
                Stop();
                return ServerStartResult.Failure(
                    $"El servidor de la app no respondió en {options.StartTimeout.TotalSeconds:0} segundos.", Output.Text);
            }

            await Task.Delay(options.PollInterval, _time, cancellationToken);
        }
    }

    /// <summary>Stops the server if this window started it; a server that was already running is left alone.</summary>
    public void Stop()
    {
        if (_process is null)
        {
            return;
        }

        _process.Kill();
        _process.Dispose();
        _process = null;
    }

    public void Dispose() => Stop();
}
