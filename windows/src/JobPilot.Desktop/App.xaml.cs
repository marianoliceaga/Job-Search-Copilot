using System.Windows;

using JobPilot.Desktop.Core;

namespace JobPilot.Desktop;

/// <summary>
/// One window at a time: a second start brings the running window to the front and quits. The installer waits for the
/// same mutex before replacing files.
/// </summary>
public partial class App : Application
{
    private Mutex? _mutex;
    private EventWaitHandle? _activate;
    private RegisteredWaitHandle? _activateWait;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _mutex = new Mutex(initiallyOwned: true, DesktopIdentity.MutexName, out var createdNew);
        if (!createdNew)
        {
            if (EventWaitHandle.TryOpenExisting(DesktopIdentity.ActivateEventName, out var running))
            {
                running.Set();
                running.Dispose();
            }

            _mutex.Dispose();
            _mutex = null;
            Shutdown();
            return;
        }

        var window = new MainWindow();
        MainWindow = window;
        window.Show();

        _activate = new EventWaitHandle(false, EventResetMode.AutoReset, DesktopIdentity.ActivateEventName);
        _activateWait = ThreadPool.RegisterWaitForSingleObject(
            _activate, (_, _) => Dispatcher.BeginInvoke(window.BringToFront), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _activateWait?.Unregister(null);
        _activate?.Dispose();
        if (_mutex is not null)
        {
            _mutex.ReleaseMutex();
            _mutex.Dispose();
        }

        base.OnExit(e);
    }
}
