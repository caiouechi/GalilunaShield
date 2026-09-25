using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using GalilunaShield.Configuration;
using GalilunaShield.Hotkey;
using WinForms = System.Windows.Forms;

namespace GalilunaShield.App;

public partial class App : Application
{
    private static Mutex? _singleInstance;
    private WinForms.NotifyIcon? _tray;
    private GlobalHotkey? _hotkey;
    private GlobalHotkey? _showHotkey;
    private ShellViewModel? _shell;
    private MainWindow? _window;
    private bool _exiting;

    public static string LogDirectory => Path.Combine(AppPaths.ConfigDirectory, "logs");

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // One-shot elevated action to install/remove the keep-alive scheduled task, then exit.
        var wdIndex = Array.FindIndex(e.Args, a => a.Equals("--watchdog-task", StringComparison.OrdinalIgnoreCase));
        if (wdIndex >= 0 && wdIndex + 1 < e.Args.Length)
        {
            var code = Watchdog.ApplyElevated(e.Args[wdIndex + 1].Equals("on", StringComparison.OrdinalIgnoreCase));
            Shutdown(code);
            return;
        }

        // Dev hook: render the consent window to a PNG and exit (for docs).
        var rcIndex = Array.FindIndex(e.Args, a => a.Equals("--render-consent", StringComparison.OrdinalIgnoreCase));
        if (rcIndex >= 0 && rcIndex + 1 < e.Args.Length)
        {
            var folder = e.Args[rcIndex + 1];
            Directory.CreateDirectory(folder);
            var win = new ConsentWindow(new ConsentManager(Path.Combine(Path.GetTempPath(), "gs-render-consent.json")));
            win.Show();
            await Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
            await Task.Delay(500);
            win.UpdateLayout();
            var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)win.ActualWidth, (int)win.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            bmp.Render(win);
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
            await using (var f = File.Create(Path.Combine(folder, "consent.png"))) enc.Save(f);
            Shutdown();
            return;
        }

        var roIndex = Array.FindIndex(e.Args, a => a.Equals("--render-onboarding", StringComparison.OrdinalIgnoreCase));
        if (roIndex >= 0 && roIndex + 1 < e.Args.Length)
        {
            var folder = e.Args[roIndex + 1];
            Directory.CreateDirectory(folder);
            var cfg = AppConfig.Load(AppPaths.ConfigFile);
            var ids = GalilunaShield.Configuration.RedFlagBuckets.LoadCatalog(AppPaths.ShippedBucketsDirectory, AppPaths.UserBucketsDirectory).Select(b => b.Id).ToList();
            var w = new OnboardingWindow(cfg, ids);
            w.Show();
            await Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
            await Task.Delay(600);
            w.UpdateLayout();
            var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)w.ActualWidth, (int)w.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            bmp.Render(w);
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
            await using (var f = File.Create(Path.Combine(folder, "onboarding.png"))) enc.Save(f);
            Shutdown();
            return;
        }

        _singleInstance = new Mutex(true, @"Local\GalilunaShield.Desktop", out var createdNew);
        if (!createdNew)
        {
            // Already running (possibly hidden): signal the running copy to reveal itself, then quit quietly.
            try
            {
                if (EventWaitHandle.TryOpenExisting(ShowEventName, out var ev)) { ev.Set(); ev.Dispose(); }
            }
            catch { /* fall through */ }
            Shutdown();
            return;
        }

        StartRevealListener();

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => WriteCrashLog(ex.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, ex) => { WriteCrashLog(ex.Exception); ex.SetObserved(); };

        var startMinimized = e.Args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));
        var startMonitoring = e.Args.Any(a => a.Equals("--start", StringComparison.OrdinalIgnoreCase));

        AppPaths.EnsureUserConfig();
        var config = AppConfig.Load(AppPaths.ConfigFile);

        // Developer aid: "--screenshots <folder>" renders pages and exits; it bypasses the consent gate.
        var shotIndex = Array.FindIndex(e.Args, a => a.Equals("--screenshots", StringComparison.OrdinalIgnoreCase));
        var shotFolder = shotIndex >= 0 && shotIndex + 1 < e.Args.Length ? e.Args[shotIndex + 1] : null;

        // Consent & age gate. Monitoring may not run until the operator accepts the current terms.
        var consent = new ConsentManager();
        if (shotFolder is null && consent.NeedsConsent())
        {
            var consentWindow = new ConsentWindow(consent);
            if (consentWindow.ShowDialog() != true)
            {
                Shutdown();
                return;
            }
        }

        // First-run onboarding: pick the child's age (applies a protection profile) and set a PIN.
        if (shotFolder is null && !File.Exists(Path.Combine(AppPaths.ConfigDirectory, "onboarded.marker")))
        {
            var bucketIds = GalilunaShield.Configuration.RedFlagBuckets
                .LoadCatalog(AppPaths.ShippedBucketsDirectory, AppPaths.UserBucketsDirectory)
                .Select(b => b.Id).ToList();
            try
            {
                new OnboardingWindow(config, bucketIds).ShowDialog();
                config = AppConfig.Load(AppPaths.ConfigFile); // reload with the applied age profile
            }
            catch (Exception ex) { WriteCrashLog(ex); }
        }

        _shell = new ShellViewModel(config, Dispatcher);
        _shell.AlertRaisedForNotification += ShowAlertBalloon;
        _shell.ExitRequested += ExitApplication;

        _window = new MainWindow { DataContext = _shell };
        _window.Closing += OnMainWindowClosing;

        // Fully hidden only when Windows started us (--minimized) AND the parent asked for stealth. Opening the
        // app by hand always shows it, so a parent is never locked out.
        var hidden = config.StartHidden && startMinimized;

        if (!hidden) SetupTray();

        if (hidden)
        {
            // No window, no tray icon. The show-window hotkey (or launching the app again) brings it back.
        }
        else if (startMinimized)
        {
            _window.WindowState = WindowState.Minimized;
            _window.ShowInTaskbar = false;
            _window.Show();
            _window.Hide();
            _tray?.ShowBalloonTip(3000, AppPaths.ProductName, "Running in the background. Double-click the shield icon to open.", WinForms.ToolTipIcon.Info);
        }
        else
        {
            _window.Show();
        }

        try
        {
            _hotkey = new GlobalHotkey(config.Hotkey, () => Dispatcher.BeginInvoke(() => _shell.ToggleFromHotkey()));
            _hotkey.Start();
            _shell.HotkeyDescription = _hotkey.Description;
        }
        catch (Exception ex)
        {
            _shell.HotkeyDescription = null;
            _shell.PostActivity(LogLevel.Warning, $"Global hotkey unavailable: {ex.Message}");
        }

        // A separate hotkey brings the window back when running hidden or in the tray.
        if (!string.IsNullOrWhiteSpace(config.ShowWindowHotkey))
        {
            try
            {
                _showHotkey = new GlobalHotkey(config.ShowWindowHotkey, () => Dispatcher.BeginInvoke(ShowMainWindow));
                _showHotkey.Start();
            }
            catch (Exception ex)
            {
                _shell.PostActivity(LogLevel.Warning, $"Show-window hotkey unavailable: {ex.Message}");
            }
        }

        // Log a Windows sign-out / shutdown, so the report shows the shield stopped because the PC went off.
        Microsoft.Win32.SystemEvents.SessionEnding += (_, _) => _shell?.NoteWindowsShutdown();

        if (shotFolder is not null)
        {
            await _window.RenderPageAsync(shotFolder, "first-run"); // the "getting ready" overlay, before the engine loads
        }

        await _shell.InitializeAsync(startMonitoring || config.StartMonitoringOnLaunch);

        if (shotFolder is not null)
        {
            await _window.RenderAllPagesAsync(shotFolder);
            ExitApplication();
        }
    }

    private void SetupTray()
    {
        _tray = new WinForms.NotifyIcon
        {
            Text = AppPaths.ProductName,
            Visible = true,
        };
        try
        {
            _tray.Icon = new System.Drawing.Icon(Path.Combine(AppContext.BaseDirectory, "Assets", "galiluna.ico"));
        }
        catch
        {
            _tray.Icon = System.Drawing.SystemIcons.Shield;
        }

        var menu = new WinForms.ContextMenuStrip();
        var open = new WinForms.ToolStripMenuItem("Open Galiluna Shield", null, (_, _) => ShowMainWindow());
        var toggle = new WinForms.ToolStripMenuItem("Start monitoring", null, (_, _) => _shell?.ToggleFromHotkey());
        var reports = new WinForms.ToolStripMenuItem("Open reports", null, (_, _) => _shell?.OpenReportsCommand.Execute(null));
        var exit = new WinForms.ToolStripMenuItem("Exit", null, (_, _) => _shell?.RequestExit());
        menu.Items.AddRange(new WinForms.ToolStripItem[] { open, toggle, reports, new WinForms.ToolStripSeparator(), exit });
        menu.Opening += (_, _) => toggle.Text = _shell?.IsRunning == true ? "Stop monitoring" : "Start monitoring";
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => ShowMainWindow();

        if (_shell is not null)
        {
            _shell.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName is nameof(ShellViewModel.IsRunning) or nameof(ShellViewModel.IsRecording))
                {
                    _tray.Text = _shell.IsRunning
                        ? (_shell.IsRecording ? "Galiluna Shield - monitoring & recording" : "Galiluna Shield - monitoring")
                        : "Galiluna Shield - off";
                }
            };
        }
    }

    private const string ShowEventName = @"Local\GalilunaShield.Show";
    private EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _showWait;

    /// <summary>Lets a second launch of the app reveal this (possibly hidden) instance instead of starting a new one.</summary>
    private void StartRevealListener()
    {
        try
        {
            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            _showWait = ThreadPool.RegisterWaitForSingleObject(_showEvent, (_, _) => Dispatcher.BeginInvoke(ShowMainWindow), null, -1, false);
        }
        catch { /* not fatal; the show-window hotkey still works */ }
    }

    private void ShowMainWindow()
    {
        if (_window is null) return;
        if (_tray is null) SetupTray(); // was running fully hidden; give it a tray icon now that it is visible
        _window.ShowInTaskbar = true;
        _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private void ShowAlertBalloon(AlertRecord alert)
    {
        if (_tray is null) return;
        var title = alert.PossibleOnly ? "Possible red flag" : $"{alert.Severity} red flag";
        var body = $"{alert.Source}: \"{alert.Text}\"";
        if (body.Length > 200) body = body[..197] + "...";
        var icon = alert.Severity >= Severity.High ? WinForms.ToolTipIcon.Error : WinForms.ToolTipIcon.Warning;
        _tray.ShowBalloonTip(8000, $"{AppPaths.ProductName} - {title}", body, icon);
    }

    private void OnMainWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_exiting) return;
        // Closing the window keeps the shield running in the tray while monitoring; otherwise it really exits.
        if (_shell?.IsRunning == true)
        {
            e.Cancel = true;
            _window!.Hide();
            _window.ShowInTaskbar = false;
            _tray?.ShowBalloonTip(3000, AppPaths.ProductName, "Still monitoring in the background. Double-click the shield icon to open, or right-click it to exit.", WinForms.ToolTipIcon.Info);
        }
        else
        {
            e.Cancel = true;
            _shell?.RequestExit();
        }
    }

    private void ExitApplication()
    {
        if (_exiting) return;
        _exiting = true;
        try
        {
            _hotkey?.Dispose();
            _showHotkey?.Dispose();
            _showWait?.Unregister(null);
            _showEvent?.Dispose();
            _shell?.Dispose();
            if (_tray is not null) { _tray.Visible = false; _tray.Dispose(); }
        }
        finally
        {
            Shutdown();
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteCrashLog(e.Exception);
        _shell?.PostActivity(LogLevel.Error, $"Unexpected error: {e.Exception.Message} (details in {LogDirectory})");
        e.Handled = true;
    }

    public static void WriteCrashLog(Exception? ex)
    {
        if (ex is null) return;
        try
        {
            Directory.CreateDirectory(LogDirectory);
            File.AppendAllText(Path.Combine(LogDirectory, $"errors-{DateTime.Now:yyyy-MM-dd}.log"),
                $"[{DateTime.Now:HH:mm:ss}] {ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { /* nothing else we can do */ }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
