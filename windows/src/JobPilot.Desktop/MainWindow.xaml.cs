using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

using JobPilot.Desktop.Core;

using Microsoft.Web.WebView2.Core;

namespace JobPilot.Desktop;

/// <summary>
/// The app's window: starts the web app (or uses the one already running), then shows it in WebView2. Only the app's own
/// pages load here; links to other sites and the Google connection open in the default browser.
/// </summary>
public partial class MainWindow : Window
{
    // WM_NCHITTEST return values — needed to restore resize/move when WebView2 swallows mouse messages.
    private const int WM_NCHITTEST = 0x0084;
    private const int HTCLIENT = 1;
    private const int HTCAPTION = 2;
    private const int HTLEFT = 10;
    private const int HTRIGHT = 11;
    private const int HTTOP = 12;
    private const int HTTOPLEFT = 13;
    private const int HTTOPRIGHT = 14;
    private const int HTBOTTOM = 15;
    private const int HTBOTTOMLEFT = 16;
    private const int HTBOTTOMRIGHT = 17;

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    /// <summary>Microsoft's download page for end users (the WebView2 Runtime comes with Windows 11 and most Windows 10 PCs).</summary>
    private static readonly Uri WebView2RuntimeDownload = new("https://developer.microsoft.com/microsoft-edge/webview2/consumer/");

    private readonly HttpClient _http = ServerProbe.CreateClient();
    private readonly ChildProcessJob _job = new();
    private readonly ServerHost _server;
    private Uri? _link;
    private bool _webViewReady;
    private bool _shown;
    private bool _googlePending;

    public MainWindow()
    {
        InitializeComponent();
        var executable = Path.Combine(AppContext.BaseDirectory, DesktopIdentity.ServerFolder, DesktopIdentity.ServerExecutableName);
        _server = new ServerHost(
            new ServerOptions(executable, DesktopIdentity.Port(Environment.GetEnvironmentVariable)),
            new ServerProbe(_http),
            (info, output) =>
            {
                var process = SystemServerProcess.Start(info, output);
                _job.Add(process.Process);
                return process;
            });
        Loaded += async (_, _) => await StartAsync();
        Activated += (_, _) => RefreshAfterGoogle();
        Closed += (_, _) =>
        {
            SaveWindowPosition();
            _server.Dispose();
            _job.Dispose();
            _http.Dispose();
        };

        RestoreWindowPosition();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // Hook into the Win32 message loop so we can fix WM_NCHITTEST being swallowed by WebView2's HWND.
        var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        source?.AddHook(WndProc);
    }

    /// <summary>
    /// Restores WPF resize and drag behaviour that WebView2's child HWND steals by consuming all mouse messages,
    /// including those in the non-client resize border. We rerun DefWindowProc for the resize/caption hit areas
    /// so the OS can handle them normally.
    /// </summary>
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_NCHITTEST)
        {
            var result = DefWindowProc(hwnd, msg, wParam, lParam);
            int hit = (int)result;
            if (hit is HTLEFT or HTRIGHT or HTTOP or HTTOPLEFT or HTTOPRIGHT
                     or HTBOTTOM or HTBOTTOMLEFT or HTBOTTOMRIGHT or HTCAPTION)
            {
                handled = true;
                return result;
            }
        }

        return IntPtr.Zero;
    }

    public void BringToFront()
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private async Task StartAsync()
    {
        ShowStatus($"Iniciando {DesktopIdentity.ProductName}…");
        if (!IsWebView2Installed())
        {
            ShowError(
                "Falta Microsoft Edge WebView2 Runtime, el componente de Windows que muestra la app. Instalalo desde la página de " +
                "Microsoft y volvé a abrir JobSearch Copilot.",
                link: (WebView2RuntimeDownload, "Descargar WebView2"));
            return;
        }

        var result = await _server.StartAsync(CancellationToken.None);
        if (!result.IsSuccess)
        {
            ShowError(result.Error!, result.Details);
            return;
        }

        try
        {
            await EnsureWebViewAsync();
        }
        catch (Exception ex) when (ex is WebView2RuntimeNotFoundException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            ShowError("No se pudo abrir la vista de la app.", ex.Message);
            return;
        }

        Browser.CoreWebView2.Navigate(_server.Options.BaseUri.AbsoluteUri);
    }

    private static bool IsWebView2Installed()
    {
        try
        {
            return CoreWebView2Environment.GetAvailableBrowserVersionString() is not null;
        }
        catch (WebView2RuntimeNotFoundException)
        {
            return false;
        }
    }

    private async Task EnsureWebViewAsync()
    {
        if (_webViewReady)
        {
            return;
        }

        // Browser cache and cookies of the window, next to the app's data (never in the install folder).
        var userData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JobPilot", "desktop", "WebView2");
        var environment = await CoreWebView2Environment.CreateAsync(browserExecutableFolder: null, userDataFolder: userData);
        await Browser.EnsureCoreWebView2Async(environment);

        var core = Browser.CoreWebView2;
        core.Settings.IsStatusBarEnabled = false;
        // The Gemini key and the Google client secret are typed into the app: WebView2 must not offer to save them.
        core.Settings.IsPasswordAutosaveEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false;
#if !DEBUG
        core.Settings.AreDevToolsEnabled = false;
#endif
        core.NavigationStarting += OnNavigationStarting;
        core.NewWindowRequested += OnNewWindowRequested;
        core.NavigationCompleted += (_, _) => ShowBrowser();
        core.DocumentTitleChanged += (_, _) =>
            Title = string.IsNullOrWhiteSpace(core.DocumentTitle) ? DesktopIdentity.ProductName : core.DocumentTitle;
        core.ProcessFailed += OnProcessFailed;
        _webViewReady = true;
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        switch (NavigationPolicy.Decide(e.Uri, _server.Options.BaseUri))
        {
            case NavigationAction.Allow:
                return;
            case NavigationAction.OpenInBrowser:
                e.Cancel = true;
                OpenInBrowser(new Uri(e.Uri));
                return;
            default:
                e.Cancel = true;
                return;
        }
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        // No second WebView2 window: app pages open here, everything else in the browser.
        e.Handled = true;
        switch (NavigationPolicy.Decide(e.Uri, _server.Options.BaseUri))
        {
            case NavigationAction.Allow:
                Browser.CoreWebView2.Navigate(e.Uri);
                return;
            case NavigationAction.OpenInBrowser:
                OpenInBrowser(new Uri(e.Uri));
                return;
            default:
                return;
        }
    }

    private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited)
        {
            ShowError("La vista de la app se cerró inesperadamente. Cerrá y volvé a abrir JobSearch Copilot.");
            RetryButton.Visibility = Visibility.Collapsed;
            return;
        }

        Browser.CoreWebView2.Reload();
    }

    private void OpenInBrowser(Uri uri)
    {
        if (NavigationPolicy.IsGoogleConnect(uri))
        {
            _googlePending = true;
            GoogleBanner.Visibility = Visibility.Visible;
        }

        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })?.Dispose();
    }

    private void RefreshAfterGoogle()
    {
        if (!_googlePending || !_webViewReady)
        {
            return;
        }

        _googlePending = false;
        GoogleBanner.Visibility = Visibility.Collapsed;
        Browser.CoreWebView2.Reload();
    }

    private void GoogleDone_Click(object sender, RoutedEventArgs e)
    {
        _googlePending = true;
        RefreshAfterGoogle();
    }

    private async void Retry_Click(object sender, RoutedEventArgs e) => await StartAsync();

    private void Link_Click(object sender, RoutedEventArgs e)
    {
        if (_link is not null)
        {
            Process.Start(new ProcessStartInfo(_link.AbsoluteUri) { UseShellExecute = true })?.Dispose();
        }
    }

    private void ShowBrowser()
    {
        if (_shown)
        {
            return;
        }

        _shown = true;
        StatusPanel.Visibility = Visibility.Collapsed;
        Browser.Visibility = Visibility.Visible;
    }

    private void ShowStatus(string text)
    {
        _shown = false;
        Browser.Visibility = Visibility.Hidden;
        StatusPanel.Visibility = Visibility.Visible;
        StatusText.Text = text;
        DetailsText.Visibility = Visibility.Collapsed;
        ActionsPanel.Visibility = Visibility.Collapsed;
    }

    private void ShowError(string message, string? details = null, (Uri Uri, string Label)? link = null)
    {
        ShowStatus(message);
        DetailsText.Text = details ?? string.Empty;
        DetailsText.Visibility = string.IsNullOrWhiteSpace(details) ? Visibility.Collapsed : Visibility.Visible;
        ActionsPanel.Visibility = Visibility.Visible;
        RetryButton.Visibility = Visibility.Visible;
        _link = link?.Uri;
        LinkButton.Content = link?.Label;
        LinkButton.Visibility = link is null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Saves the current window position, size, and state to user settings.</summary>
    private void SaveWindowPosition()
    {
        if (WindowState == WindowState.Normal)
        {
            Properties.Settings.Default.WindowLeft = Left;
            Properties.Settings.Default.WindowTop = Top;
            Properties.Settings.Default.WindowWidth = Width;
            Properties.Settings.Default.WindowHeight = Height;
        }

        Properties.Settings.Default.WindowState = (int)WindowState;
        Properties.Settings.Default.Save();
    }

    /// <summary>Restores window position and size from user settings, ensuring the window is on a visible screen.</summary>
    private void RestoreWindowPosition()
    {
        var s = Properties.Settings.Default;
        if (s.WindowWidth <= 0 || s.WindowHeight <= 0)
        {
            return; // first launch — use XAML defaults
        }

        // Guard: ensure the saved bounds intersect at least one WPF virtual desktop area.
        // VirtualScreenLeft/Top/Width/Height cover all monitors in WPF units.
        var vLeft = SystemParameters.VirtualScreenLeft;
        var vTop = SystemParameters.VirtualScreenTop;
        var vRight = vLeft + SystemParameters.VirtualScreenWidth;
        var vBottom = vTop + SystemParameters.VirtualScreenHeight;

        bool visible = s.WindowLeft < vRight && s.WindowLeft + s.WindowWidth > vLeft
                    && s.WindowTop < vBottom && s.WindowTop + s.WindowHeight > vTop;

        if (!visible)
        {
            return; // screen was disconnected — fall back to XAML defaults
        }

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = s.WindowLeft;
        Top = s.WindowTop;
        Width = s.WindowWidth;
        Height = s.WindowHeight;

        var savedState = (WindowState)s.WindowState;
        if (savedState == WindowState.Maximized)
        {
            WindowState = WindowState.Maximized;
        }
    }
}
