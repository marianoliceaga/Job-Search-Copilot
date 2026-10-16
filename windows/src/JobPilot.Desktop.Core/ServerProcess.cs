using System.Diagnostics;

namespace JobPilot.Desktop.Core;

/// <summary>The web app's process as seen by <see cref="ServerHost"/> (replaced by a fake in tests).</summary>
public interface IServerProcess : IDisposable
{
    bool HasExited { get; }

    int ExitCode { get; }

    /// <summary>Stops the process and its children and waits briefly for them to exit.</summary>
    void Kill();
}

/// <summary>A real process whose output is drained into a <see cref="ServerOutput"/> (a full pipe would block the server).</summary>
public sealed class SystemServerProcess : IServerProcess
{
    private readonly Process _process;

    private SystemServerProcess(Process process) => _process = process;

    public Process Process => _process;

    public bool HasExited => _process.HasExited;

    public int ExitCode => _process.ExitCode;

    public static SystemServerProcess Start(ProcessStartInfo startInfo, ServerOutput output)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        ArgumentNullException.ThrowIfNull(output);
        var process = new Process { StartInfo = startInfo };
        process.OutputDataReceived += (_, e) => output.Add(e.Data);
        process.ErrorDataReceived += (_, e) => output.Add(e.Data);
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return new SystemServerProcess(process);
    }

    public void Kill()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(TimeSpan.FromSeconds(5));
            }
        }
        catch (InvalidOperationException)
        {
            // Already gone.
        }
    }

    public void Dispose() => _process.Dispose();
}
