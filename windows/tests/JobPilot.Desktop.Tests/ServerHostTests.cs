using System.Diagnostics;
using System.Net;
using JobPilot.Desktop.Core;

namespace JobPilot.Desktop.Tests;

/// <summary>Start, attach and failure paths with a fake server process and a fake port.</summary>
public sealed class ServerHostTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly string _folder = Directory.CreateTempSubdirectory("jsc-desktop-").FullName;
    private readonly FakePort _port = new();
    private readonly List<(ProcessStartInfo Info, FakeProcess Process)> _started = [];

    private string Executable
    {
        get
        {
            var path = Path.Combine(_folder, "server", DesktopIdentity.ServerExecutableName);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "fake");
            return path;
        }
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private ServerHost Host(string executable, Action<FakeProcess, ServerOutput>? onStart = null, ServerOptions? options = null) =>
        new(
            options ?? new ServerOptions(executable, 5080) { PollInterval = TimeSpan.FromMilliseconds(5) },
            new ServerProbe(new HttpClient(_port)),
            (info, output) =>
            {
                var process = new FakeProcess();
                onStart?.Invoke(process, output);
                _started.Add((info, process));
                return process;
            });

    [Fact]
    public async Task A_running_app_is_used_and_left_running()
    {
        _port.Answers.Enqueue(PortAnswer.App);
        using var host = Host(Executable);

        var result = await host.StartAsync(Ct);
        host.Stop();

        Assert.Equal((true, true), (result.IsSuccess, result.Attached));
        Assert.Empty(_started);
        Assert.False(host.OwnsServer);
    }

    [Fact]
    public async Task Another_program_on_the_port_is_reported_without_starting_the_server()
    {
        _port.Answers.Enqueue(PortAnswer.Other);
        using var host = Host(Executable);

        var result = await host.StartAsync(Ct);

        Assert.False(result.IsSuccess);
        Assert.Contains("Otro programa está usando el puerto 5080", result.Error, StringComparison.Ordinal);
        Assert.Empty(_started);
    }

    [Fact]
    public async Task A_missing_server_asks_to_reinstall()
    {
        using var host = Host(Path.Combine(_folder, "missing", DesktopIdentity.ServerExecutableName));

        var result = await host.StartAsync(Ct);

        Assert.Contains("Reinstalá JobSearch Copilot", result.Error, StringComparison.Ordinal);
        Assert.Empty(_started);
    }

    [Fact]
    public async Task The_server_is_started_from_its_folder_in_production_and_awaited()
    {
        var executable = Executable;
        _port.Answers.Enqueue(PortAnswer.Refused);
        _port.Answers.Enqueue(PortAnswer.Refused);
        _port.Answers.Enqueue(PortAnswer.Refused);
        _port.Answers.Enqueue(PortAnswer.App);
        using var host = Host(executable);

        var result = await host.StartAsync(Ct);

        Assert.Equal((true, false), (result.IsSuccess, result.Attached));
        var (info, process) = Assert.Single(_started);
        Assert.Equal(executable, info.FileName);
        Assert.Equal(Path.GetDirectoryName(executable), info.WorkingDirectory);
        Assert.Equal("Production", info.Environment["ASPNETCORE_ENVIRONMENT"]);
        Assert.True(info.CreateNoWindow && !info.UseShellExecute && info.RedirectStandardOutput && info.RedirectStandardError);
        Assert.True(host.OwnsServer);

        host.Stop();

        Assert.True(process.Killed && process.Disposed);
        Assert.False(host.OwnsServer);
    }

    [Fact]
    public async Task A_server_that_exits_while_starting_is_reported_with_its_output()
    {
        using var host = Host(Executable, (process, output) =>
        {
            output.Add("Failed to bind to address http://127.0.0.1:5080: address already in use.");
            process.Exit(1);
        });

        var result = await host.StartAsync(Ct);

        Assert.Contains("se cerró al iniciar (código 1)", result.Error, StringComparison.Ordinal);
        Assert.Contains("address already in use", result.Details, StringComparison.Ordinal);
        Assert.True(Assert.Single(_started).Process.Disposed);
        Assert.False(host.OwnsServer);
    }

    [Fact]
    public async Task A_server_that_never_answers_is_stopped_after_the_timeout()
    {
        var options = new ServerOptions(Executable, 5080) { StartTimeout = TimeSpan.FromMilliseconds(100), PollInterval = TimeSpan.FromMilliseconds(5) };
        using var host = Host(options.ExecutablePath, options: options);

        var result = await host.StartAsync(Ct);

        Assert.Contains("no respondió", result.Error, StringComparison.Ordinal);
        Assert.True(Assert.Single(_started).Process.Killed);
    }

    [Fact]
    public async Task Extra_environment_reaches_the_server()
    {
        _port.Answers.Enqueue(PortAnswer.Refused);
        _port.Answers.Enqueue(PortAnswer.App);
        var options = new ServerOptions(Executable, 5090)
        {
            Environment = "Development",
            ExtraEnvironment = new Dictionary<string, string> { ["JobPilot__DataDirectory"] = _folder },
        };
        using var host = Host(options.ExecutablePath, options: options);

        await host.StartAsync(Ct);

        var info = Assert.Single(_started).Info;
        Assert.Equal(("Development", _folder), (info.Environment["ASPNETCORE_ENVIRONMENT"], info.Environment["JobPilot__DataDirectory"]));
        Assert.All(_port.Requests, uri => Assert.Equal("http://localhost:5090/app.js", uri.AbsoluteUri));
    }

    [Theory]
    [InlineData(null, 5080)]
    [InlineData("5099", 5099)]
    [InlineData("0", 5080)]
    [InlineData("70000", 5080)]
    [InlineData("abc", 5080)]
    public void The_port_comes_from_the_web_apps_environment_variable(string? value, int expected) =>
        Assert.Equal(expected, DesktopIdentity.Port(name => name == "JobPilot__Port" ? value : null));

    private enum PortAnswer
    {
        Refused,
        App,
        Other,
    }

    /// <summary>Answers probes in order; once the queue is empty, the port stays closed.</summary>
    private sealed class FakePort : HttpMessageHandler
    {
        public Queue<PortAnswer> Answers { get; } = new();

        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return (Answers.TryDequeue(out var answer) ? answer : PortAnswer.Refused) switch
            {
                PortAnswer.App => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("window.jobPilot = { downloadFile: async () => {} };"),
                }),
                PortAnswer.Other => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)),
                _ => throw new HttpRequestException("Connection refused"),
            };
        }
    }

    private sealed class FakeProcess : IServerProcess
    {
        private int? _exitCode;

        public bool Killed { get; private set; }

        public bool Disposed { get; private set; }

        public bool HasExited => _exitCode is not null;

        public int ExitCode => _exitCode ?? throw new InvalidOperationException("Still running.");

        public void Exit(int code) => _exitCode = code;

        public void Kill()
        {
            Killed = true;
            _exitCode ??= -1;
        }

        public void Dispose() => Disposed = true;
    }
}
