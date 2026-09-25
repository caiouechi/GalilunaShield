using System.Collections.ObjectModel;
using System.Media;
using System.Windows;
using System.Windows.Threading;
using GalilunaShield.Alerts;
using GalilunaShield.Configuration;
using GalilunaShield.Detection;
using GalilunaShield.Transcription;

namespace GalilunaShield.App;

/// <summary>State and commands behind the whole desktop window.</summary>
public sealed class ShellViewModel : ObservableObject, IDisposable
{
    private const int MaxActivity = 400;
    private const int MaxTranscript = 600;

    private readonly Dispatcher _ui;
    private readonly ActivityLog _activity;
    private AppConfig _config;
    private AlertStore _store;
    private RedFlagDetector? _detector;
    private WhisperTranscriber? _transcriber;
    private MonitorService? _monitor;
    private SoundPlayer? _player;
    private EvidenceProtector _protector;
    private string? _playingTempFile;
    private IReadOnlyList<RedFlagBucket> _buckets = Array.Empty<RedFlagBucket>();

    public ShellViewModel(AppConfig config, Dispatcher ui)
    {
        _config = config;
        _ui = ui;
        _protector = new EvidenceProtector(config.Security.EncryptEvidence, config.ParentPin);
        _store = new AlertStore(AppPaths.ResolveOutputDirectory(config.OutputDirectory), config.SaveAudioClipOnAlert, _protector);
        _activity = new ActivityLog(AppPaths.ResolveLogsDirectory(config.LogsDirectory, config.OutputDirectory), AppPaths.ProductName);
        _activity.Logged += e => Post(() => OnActivityEvent(e));
        _activity.Start("desktop app");
        Settings = new SettingsViewModel(config, this);
        WordList = new WordListViewModel(this);
        Buckets = new BucketsViewModel(this);

        ToggleMonitoringCommand = new RelayCommand(ToggleMonitoring, () => IsReady && !_busyToggling);
        OpenOutputFolderCommand = new RelayCommand(() => Shell.Open(_store.RootDirectory));
        OpenReportsCommand = new RelayCommand(OpenReports);
        BuildDailyReportCommand = new RelayCommand(() => BuildReport(daily: true));
        BuildWeeklyReportCommand = new RelayCommand(() => BuildReport(daily: false));
        PlayClipCommand = new RelayCommand(p => PlayClip(p as string));
        StopClipCommand = new RelayCommand(StopClip);
        OpenFileCommand = new RelayCommand(p => OpenEvidence(p as string));
        RevealFileCommand = new RelayCommand(p => { if (p is string s) Shell.RevealInExplorer(s); });
        SetModeCommand = new RelayCommand(p => { if (p is MonitorMode m) SetMode(m); });
        SetRecordingModeCommand = new RelayCommand(p => { if (p is RecordingMode r) SetRecordingMode(r); });
        ClearActivityCommand = new RelayCommand(() => Activity.Clear());
        ExitCommand = new RelayCommand(RequestExit);
        RefreshReportsCommand = new RelayCommand(LoadReports);
        OpenSettingsFolderCommand = new RelayCommand(() => Shell.Open(AppPaths.ConfigDirectory));
        OpenLegalCommand = new RelayCommand(p => OpenLegal(p as string));
        ExportEvidenceCommand = new RelayCommand(ExportEvidence);
        TestNotificationCommand = new RelayCommand(async () => await TestNotificationAsync());
        StartDashboardCommand = new RelayCommand(StartDashboard, () => WebDashboard.IsInstalled);
        StopDashboardCommand = new RelayCommand(() => { _dashboard.Stop(); RaiseDashboard(); });
        OpenDashboardCommand = new RelayCommand(() => Shell.Open(DashboardUrl));

        AlertsView = System.Windows.Data.CollectionViewSource.GetDefaultView(Alerts);
        AlertsView.Filter = o => _showDismissed || (o is AlertItemViewModel a && !a.IsDismissed);

        LoadRecentAlerts();
        LoadReports();
    }

    // ---------------------------------------------------------------- state

    public SettingsViewModel Settings { get; }
    public WordListViewModel WordList { get; }

    public ObservableCollection<LogEntry> Activity { get; } = new();
    public ObservableCollection<TranscriptLine> Transcript { get; } = new();
    public ObservableCollection<AlertItemViewModel> Alerts { get; } = new();
    public System.ComponentModel.ICollectionView AlertsView { get; }
    public ObservableCollection<FileInfo> ReportFiles { get; } = new();

    private bool _showDismissed;
    public bool ShowDismissed { get => _showDismissed; set { if (Set(ref _showDismissed, value)) AlertsView.Refresh(); } }

    public event Action<AlertRecord>? AlertRaisedForNotification;
    public event Action? ExitRequested;

    private bool _isReady;
    public bool IsReady { get => _isReady; private set { if (Set(ref _isReady, value)) ToggleMonitoringCommand.RaiseCanExecuteChanged(); } }

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; private set => Set(ref _isBusy, value); }

    private string _statusHeadline = "Getting ready...";
    public string StatusHeadline { get => _statusHeadline; private set => Set(ref _statusHeadline, value); }

    private string _statusDetail = "";
    public string StatusDetail { get => _statusDetail; private set => Set(ref _statusDetail, value); }

    private double _downloadProgress;
    public double DownloadProgress { get => _downloadProgress; private set => Set(ref _downloadProgress, value); }

    private bool _isDownloading;
    public bool IsDownloading { get => _isDownloading; private set => Set(ref _isDownloading, value); }

    private bool _isRunning;
    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (Set(ref _isRunning, value))
            {
                OnPropertyChanged(nameof(ToggleButtonText));
                OnPropertyChanged(nameof(StatusBadgeText));
            }
        }
    }

    private bool _isRecording;
    public bool IsRecording { get => _isRecording; private set { if (Set(ref _isRecording, value)) OnPropertyChanged(nameof(StatusBadgeText)); } }

    public string ToggleButtonText => _busyToggling ? BusyLabel : (IsRunning ? "Stop monitoring" : "Start monitoring");
    public string StatusBadgeText => !IsReady ? "Preparing" : IsRunning ? (IsRecording ? "Monitoring & recording" : "Monitoring") : "Off";

    private MonitorMode _mode;
    public MonitorMode Mode { get => _mode; private set { if (Set(ref _mode, value)) OnPropertyChanged(nameof(ModeDescription)); } }
    public string ModeDescription => MonitorService.Describe(Mode);

    private RecordingMode _recordingMode;
    public RecordingMode RecordingMode { get => _recordingMode; private set { if (Set(ref _recordingMode, value)) OnPropertyChanged(nameof(RecordingDescription)); } }
    public string RecordingDescription => _monitor?.DescribeRecording(RecordingMode) ?? "";

    private string _sourcesText = "";
    public string SourcesText { get => _sourcesText; private set => Set(ref _sourcesText, value); }

    private int _alertsToday;
    public int AlertsToday { get => _alertsToday; private set => Set(ref _alertsToday, value); }

    private int _newAlerts;
    public int NewAlertCount { get => _newAlerts; private set => Set(ref _newAlerts, value); }

    private int _criticalToday;
    public int CriticalToday { get => _criticalToday; private set => Set(ref _criticalToday, value); }

    private int _alertsWeek;
    public int AlertsWeek { get => _alertsWeek; private set => Set(ref _alertsWeek, value); }

    private string _lastAlertText = "No red flags yet";
    public string LastAlertText { get => _lastAlertText; private set => Set(ref _lastAlertText, value); }

    private string? _hotkeyDescription;
    public string? HotkeyDescription { get => _hotkeyDescription; set { if (Set(ref _hotkeyDescription, value)) OnPropertyChanged(nameof(HotkeyHint)); } }
    public string HotkeyHint => HotkeyDescription is null ? "" : $"Tip: press {HotkeyDescription} from any window to start or stop.";

    public string OutputDirectory => _store.RootDirectory;
    public string ConfigDirectory => AppPaths.ConfigDirectory;
    public string Version => typeof(ShellViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    public string ConsentSummary
    {
        get
        {
            var r = new ConsentManager().Read();
            return r is null
                ? "No consent record found."
                : $"Accepted on {r.AcceptedAt:d MMMM yyyy HH:mm} by Windows user \"{r.WindowsUser}\" on \"{r.MachineName}\" (documents v{r.EulaVersion}/{r.PrivacyVersion}/{r.AcceptableUseVersion}). Child age band: {r.ChildAgeBand ?? "not stated"}.";
        }
    }

    public RelayCommand OpenLegalCommand { get; }
    public RelayCommand ExportEvidenceCommand { get; }
    public RelayCommand TestNotificationCommand { get; }
    public RelayCommand StartDashboardCommand { get; }
    public RelayCommand StopDashboardCommand { get; }
    public RelayCommand OpenDashboardCommand { get; }

    private readonly WebDashboard _dashboard = new();
    public bool DashboardInstalled => WebDashboard.IsInstalled;
    public bool DashboardRunning => _dashboard.IsRunning;
    public int DashboardPort => _config.Web.Port <= 0 ? 8787 : _config.Web.Port;
    public string DashboardUrl => WebDashboard.Url(DashboardPort);
    public string DashboardStatus => !WebDashboard.IsInstalled
        ? "The dashboard component is not installed."
        : _dashboard.IsRunning
            ? $"Running. On your phone (same wifi) open: {DashboardUrl}"
            : "Stopped. Start it, then open the address on your phone.";

    private void RaiseDashboard()
    {
        OnPropertyChanged(nameof(DashboardRunning));
        OnPropertyChanged(nameof(DashboardStatus));
    }

    private void StartDashboard()
    {
        if (string.IsNullOrEmpty(_config.ParentPin))
        {
            MessageBox.Show("Set a parent PIN in Settings first. The dashboard needs it to sign in and to unlock encrypted clips.",
                AppPaths.ProductName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (_dashboard.Start(DashboardPort))
        {
            PostActivity(LogLevel.Success, $"Parent dashboard started at {DashboardUrl}");
        }
        else
        {
            PostActivity(LogLevel.Warning, "Could not start the parent dashboard.");
        }
        RaiseDashboard();
    }

    private void ExportEvidence()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save evidence pack",
            Filter = "Evidence pack (*.zip)|*.zip",
            FileName = $"GalilunaShield-evidence-{DateTime.Now:yyyy-MM-dd}.zip",
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var pack = new EvidencePack(_store, _activity, _protector);
            var from = DateOnly.FromDateTime(DateTime.Now.AddDays(-30));
            var to = DateOnly.FromDateTime(DateTime.Now);
            var path = pack.Build(dlg.FileName, from, to);
            PostActivity(LogLevel.Success, $"Evidence pack saved: {path}");
            Shell.RevealInExplorer(path);
        }
        catch (Exception ex)
        {
            PostActivity(LogLevel.Warning, $"Could not build the evidence pack: {ex.Message}");
        }
    }

    private async Task TestNotificationAsync()
    {
        try
        {
            using var notifier = new GalilunaShield.Notifications.Notifier(_config.Notifications, (l, m) => PostActivity(l, m));
            var result = await notifier.SendTestAsync();
            PostActivity(LogLevel.Info, result);
        }
        catch (Exception ex)
        {
            PostActivity(LogLevel.Warning, $"Test notification failed: {ex.Message}");
        }
    }

    private static void OpenLegal(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return;
        var path = fileName == "consent.json" ? AppPaths.ConsentFile : System.IO.Path.Combine(AppPaths.LegalDirectory, fileName);
        if (File.Exists(path)) Shell.Open(path);
    }

    public IEnumerable<MonitorMode> Modes => Enum.GetValues<MonitorMode>();
    public IEnumerable<RecordingMode> RecordingModes => Enum.GetValues<RecordingMode>();

    // ---------------------------------------------------------------- commands

    public RelayCommand ToggleMonitoringCommand { get; }
    public RelayCommand OpenOutputFolderCommand { get; }
    public RelayCommand OpenReportsCommand { get; }
    public RelayCommand BuildDailyReportCommand { get; }
    public RelayCommand BuildWeeklyReportCommand { get; }
    public RelayCommand PlayClipCommand { get; }
    public RelayCommand StopClipCommand { get; }
    public RelayCommand OpenFileCommand { get; }
    public RelayCommand RevealFileCommand { get; }
    public RelayCommand SetModeCommand { get; }
    public RelayCommand SetRecordingModeCommand { get; }
    public RelayCommand ClearActivityCommand { get; }
    public RelayCommand ExitCommand { get; }
    public RelayCommand RefreshReportsCommand { get; }
    public RelayCommand OpenSettingsFolderCommand { get; }

    // ---------------------------------------------------------------- lifecycle

    /// <summary>Loads the word list and the speech model (downloading it on first run), then optionally starts.</summary>
    public async Task InitializeAsync(bool startMonitoring)
    {
        IsBusy = true;
        try
        {
            StatusHeadline = "Loading the red-flag list...";
            _buckets = RedFlagBuckets.LoadCatalog(AppPaths.ShippedBucketsDirectory, AppPaths.UserBucketsDirectory);
            _detector = new RedFlagDetector(BuildRules, _config.FuzzyMatching, _config.FuzzyTolerance);
            _detector.Reloaded += n => Post(() => { PostActivity(LogLevel.Info, $"Red-flag list reloaded: {n} words/phrases."); WordList.Refresh(); });
            _detector.ReloadFailed += ex => Post(() => PostActivity(LogLevel.Warning, $"Could not reload the red-flag list: {ex.Message}"));
            _detector.WatchFile(AppPaths.RedFlagsFile);
            _detector.WatchDirectory(AppPaths.UserBucketsDirectory);
            PostActivity(LogLevel.Info, $"{_detector.RuleCount} red-flag words/phrases loaded ({EnabledBucketCount} of {_buckets.Count} optional topic packs on).");
            WordList.Refresh();
            Buckets.Refresh();

            if (!WhisperTranscriber.IsModelDownloaded(AppPaths.ModelDirectory, _config.ModelSize))
            {
                StatusHeadline = "First-time setup: downloading the speech recognizer";
                StatusDetail = $"Model '{_config.ModelSize}' ({WhisperTranscriber.ModelSizeDescription(_config.ModelSize)}). This happens once; afterwards everything works offline.";
                IsDownloading = true;
            }
            else
            {
                StatusHeadline = "Loading the speech recognizer...";
            }

            var progress = new Progress<DownloadProgress>(p =>
            {
                if (p.Fraction is double f) DownloadProgress = f;
                StatusDetail = p.TotalBytes is long t
                    ? $"{p.BytesReceived / 1048576.0:F0} of {t / 1048576.0:F0} MB"
                    : $"{p.BytesReceived / 1048576.0:F0} MB";
            });

            _transcriber = await WhisperTranscriber.CreateAsync(_config.ModelSize, _config.Language, _config.BeamSize, AppPaths.ModelDirectory, progress, CancellationToken.None, _config.Threads);
            IsDownloading = false;

            _monitor = new MonitorService(_config, _transcriber, _detector, _store, _activity, _protector);
            _monitor.Logged += e => Post(() => AddActivity(e));
            _monitor.Transcribed += t => Post(() => AddTranscript(t));
            _monitor.AlertRaised += a => Post(() => OnAlert(a));
            _monitor.StateChanged += s => Post(() => ApplyState(s));
            ApplyState(_monitor.State);

            IsReady = true;
            StatusHeadline = "Ready";
            StatusDetail = "";
            OnPropertyChanged(nameof(StatusBadgeText));
            OnPropertyChanged(nameof(RecordingDescription));
            PostActivity(LogLevel.Success, "Speech recognizer ready. Everything runs on this computer; nothing is uploaded.");

            if (startMonitoring) _monitor.Start();
        }
        catch (Exception ex)
        {
            StatusHeadline = "Could not start";
            StatusDetail = ex.Message;
            IsDownloading = false;
            PostActivity(LogLevel.Error, $"Startup failed: {ex.Message}");
            App.WriteCrashLog(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Tears the engine down and builds it again (after settings that need it, e.g. model size).</summary>
    public async Task ReinitializeAsync(AppConfig newConfig)
    {
        var wasRunning = IsRunning;
        IsReady = false;
        _monitor?.Dispose();
        _monitor = null;
        _detector?.Dispose();
        _transcriber?.Dispose();
        _config = newConfig;
        _protector = new EvidenceProtector(newConfig.Security.EncryptEvidence, newConfig.ParentPin);
        _store = new AlertStore(AppPaths.ResolveOutputDirectory(newConfig.OutputDirectory), newConfig.SaveAudioClipOnAlert, _protector);
        OnPropertyChanged(nameof(OutputDirectory));
        OnPropertyChanged(nameof(EnabledBucketCount));
        await InitializeAsync(wasRunning);
    }

    public AppConfig Config => _config;
    public ActivityLog ActivityLog => _activity;

    // ---------------------------------------------------------------- word buckets (optional topic packs)

    public BucketsViewModel Buckets { get; }
    public IReadOnlyList<RedFlagBucket> AllBuckets => _buckets;
    public int EnabledBucketCount => _buckets.Count(b => _config.EnabledBuckets.Contains(b.Id, StringComparer.OrdinalIgnoreCase));

    public bool IsBucketEnabled(string id) => _config.EnabledBuckets.Contains(id, StringComparer.OrdinalIgnoreCase);

    /// <summary>Turns an optional topic pack on or off and applies it immediately (PIN-protected).</summary>
    public bool SetBucketEnabled(string id, bool enabled)
    {
        if (IsBucketEnabled(id) == enabled) return true;
        if (!PinDialog.Authorize(_config.ParentPin, "Enter the parent PIN to change the red-flag topics.")) return false;
        _config.EnabledBuckets.RemoveAll(x => x.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (enabled) _config.EnabledBuckets.Add(id);
        SaveConfigQuietly();
        _detector?.Reload();
        OnPropertyChanged(nameof(EnabledBucketCount));
        var name = _buckets.FirstOrDefault(b => b.Id == id)?.Name ?? id;
        PostActivity(LogLevel.Info, $"Topic pack '{name}' turned {(enabled ? "on" : "off")}.");
        return true;
    }

    private IEnumerable<RedFlagEntry> BuildRules()
    {
        var enabled = _buckets.Where(b => IsBucketEnabled(b.Id));
        return RedFlagBuckets.Combine(RedFlagFile.Load(AppPaths.RedFlagsFile), enabled);
    }

    private void OnActivityEvent(ActivityEvent e)
    {
        // Surface the shield being turned off / dying in the activity feed, above ordinary logs.
        if (e.IsNoteworthy)
        {
            AddActivity(new LogEntry(e.At, e.Kind == ActivityKind.MonitoringStarted ? LogLevel.Success : LogLevel.Warning, e.Message));
        }
    }

    // ---------------------------------------------------------------- monitoring

    private bool _busyToggling;

    private async void ToggleMonitoring()
    {
        if (_monitor is null || _busyToggling) return;
        // The PIN prompt is a modal window, so it must run on the UI thread.
        if (_monitor.IsRunning && !PinDialog.Authorize(_config.ParentPin, "Enter the parent PIN to stop monitoring.")) return;
        // Stopping waits for the transcription worker to drain its queue and can encrypt a recording and rebuild
        // the report. Run it off the UI thread so the window stays responsive; the button shows a busy state.
        SetToggling(true, _monitor.IsRunning ? "Stopping..." : "Starting...");
        try
        {
            await Task.Run(() => _monitor.Toggle("button"));
        }
        catch (Exception ex)
        {
            PostActivity(LogLevel.Error, $"Could not change monitoring: {ex.Message}");
            App.WriteCrashLog(ex);
        }
        finally
        {
            SetToggling(false, null);
        }
    }

    private void SetToggling(bool on, string? label)
    {
        _busyToggling = on;
        if (label is not null) BusyLabel = label;
        OnPropertyChanged(nameof(IsToggling));
        OnPropertyChanged(nameof(ToggleButtonText));
        ToggleMonitoringCommand.RaiseCanExecuteChanged();
    }

    public bool IsToggling => _busyToggling;
    private string _busyLabel = "";
    public string BusyLabel { get => _busyLabel; private set => Set(ref _busyLabel, value); }

    /// <summary>Hotkey/tray toggle: stopping still needs the PIN when one is set.</summary>
    public void ToggleFromHotkey() => ToggleMonitoring();

    private async void SetMode(MonitorMode mode)
    {
        if (_monitor is null || mode == Mode || _busyToggling) return;
        if (!PinDialog.Authorize(_config.ParentPin, "Enter the parent PIN to change what is monitored.")) { OnPropertyChanged(nameof(Mode)); return; }
        Mode = mode; // optimistic, so the dropdown doesn't flicker while the engine restarts
        _config.Mode = mode;
        SaveConfigQuietly();
        SetToggling(true, "Applying...");
        try { await Task.Run(() => _monitor.SetMode(mode)); }
        catch (Exception ex) { PostActivity(LogLevel.Error, $"Could not change mode: {ex.Message}"); }
        finally { SetToggling(false, null); }
    }

    private async void SetRecordingMode(RecordingMode mode)
    {
        if (_monitor is null || mode == RecordingMode || _busyToggling) return;
        if (!PinDialog.Authorize(_config.ParentPin, "Enter the parent PIN to change recording.")) { OnPropertyChanged(nameof(RecordingMode)); return; }
        RecordingMode = mode; // optimistic
        _config.Recording.Mode = mode;
        SaveConfigQuietly();
        SetToggling(true, "Applying...");
        try { await Task.Run(() => _monitor.SetRecordingMode(mode)); }
        catch (Exception ex) { PostActivity(LogLevel.Error, $"Could not change recording: {ex.Message}"); }
        finally { SetToggling(false, null); }
    }

    private void SaveConfigQuietly()
    {
        try { _config.Save(AppPaths.ConfigFile); }
        catch (Exception ex) { PostActivity(LogLevel.Warning, $"Could not save settings: {ex.Message}"); }
    }

    private void ApplyState(MonitorState s)
    {
        IsRunning = s.IsRunning;
        IsRecording = s.IsRecording;
        Mode = s.Mode;
        RecordingMode = s.RecordingMode;
        SourcesText = s.Sources.Count == 0 ? "Nothing (monitoring is off)" : string.Join(", ", s.Sources);
        OnPropertyChanged(nameof(RecordingDescription));
    }

    // ---------------------------------------------------------------- feeds

    public void PostActivity(LogLevel level, string message) => Post(() => AddActivity(new LogEntry(DateTimeOffset.Now, level, message)));

    private void AddActivity(LogEntry e)
    {
        if (e.Level == LogLevel.Debug && e.Message.StartsWith("Report updated")) return;
        Activity.Insert(0, e);
        while (Activity.Count > MaxActivity) Activity.RemoveAt(Activity.Count - 1);
    }

    private void AddTranscript(TranscriptLine t)
    {
        Transcript.Insert(0, t);
        while (Transcript.Count > MaxTranscript) Transcript.RemoveAt(Transcript.Count - 1);
    }

    private void OnAlert(AlertRecord a)
    {
        Alerts.Insert(0, new AlertItemViewModel(this, a, AlertStatus.New));
        RecountAlerts();
        LastAlertText = $"{a.At:HH:mm} - {a.HeardWhere ?? a.Source}: \"{a.Text}\"";
        AlertRaisedForNotification?.Invoke(a);
    }

    private void LoadRecentAlerts()
    {
        Alerts.Clear();
        var statuses = _store.ReadStatuses();
        foreach (var a in _store.ReadAlerts(DateTimeOffset.Now.AddDays(-30)).OrderByDescending(a => a.At))
        {
            var status = statuses.TryGetValue(a.Id, out var s) ? s.Status : AlertStatus.New;
            Alerts.Add(new AlertItemViewModel(this, a, status));
        }
        RecountAlerts();
        if (Alerts.Count > 0)
        {
            var a = Alerts[0];
            LastAlertText = $"{a.At:d MMM HH:mm} - {a.HeardWhere ?? a.Record.Source}: \"{a.Text}\"";
        }
    }

    /// <summary>Records the parent's triage decision and updates the view.</summary>
    public void SetAlertStatus(AlertItemViewModel item, AlertStatus status)
    {
        try { _store.SetAlertStatus(item.Record.Id, status); }
        catch (Exception ex) { PostActivity(LogLevel.Warning, $"Could not save the review status: {ex.Message}"); }
        item.Status = status;
        RecountAlerts();
        AlertsView.Refresh();
    }

    /// <summary>Mutes a word so it never raises an alert again (PIN-protected).</summary>
    public void MuteWord(string word, AlertItemViewModel? source = null)
    {
        if (string.IsNullOrWhiteSpace(word)) return;
        if (!PinDialog.Authorize(_config.ParentPin, $"Enter the parent PIN to mute \"{word}\".")) return;
        if (!_config.MutedWords.Contains(word, StringComparer.OrdinalIgnoreCase)) _config.MutedWords.Add(word);
        SaveConfigQuietly();
        _monitor?.RefreshMutes();
        PostActivity(LogLevel.Info, $"Muted the word \"{word}\". It will no longer raise alerts.");
        if (source is not null) SetAlertStatus(source, AlertStatus.Dismissed);
    }

    /// <summary>Marks an app "fine": still transcribed, but no alerts (PIN-protected).</summary>
    public void MuteApp(string app, AlertItemViewModel? source = null)
    {
        if (string.IsNullOrWhiteSpace(app)) return;
        if (!PinDialog.Authorize(_config.ParentPin, $"Enter the parent PIN to stop alerting on {app}.")) return;
        if (!_config.MutedApps.Contains(app, StringComparer.OrdinalIgnoreCase)) _config.MutedApps.Add(app);
        SaveConfigQuietly();
        PostActivity(LogLevel.Info, $"Muted {app}. It is still transcribed, but will not raise alerts.");
        if (source is not null) SetAlertStatus(source, AlertStatus.Dismissed);
    }

    private void RecountAlerts()
    {
        var today = DateTime.Today;
        AlertsToday = Alerts.Count(a => a.At.Date == today && !a.IsDismissed);
        NewAlertCount = Alerts.Count(a => a.IsNew);
        CriticalToday = Alerts.Count(a => a.At.Date == today && a.Severity == Severity.Critical);
        AlertsWeek = Alerts.Count(a => a.At >= DateTimeOffset.Now.AddDays(-7));
    }

    // ---------------------------------------------------------------- reports & files

    private void OpenReports()
    {
        try
        {
            var index = _monitor?.Reports.BuildIndex() ?? new Reports.ReportBuilder(_store, _config.Reports.WriteCsv).BuildIndex();
            if (Alerts.Count > 0 || Directory.GetFiles(_store.ReportsDirectory, "report-*.html").Length == 0)
            {
                (_monitor?.Reports ?? new Reports.ReportBuilder(_store, _config.Reports.WriteCsv)).BuildCurrent();
            }
            LoadReports();
            Shell.Open(index);
        }
        catch (Exception ex)
        {
            PostActivity(LogLevel.Warning, $"Could not open reports: {ex.Message}");
        }
    }

    private void BuildReport(bool daily)
    {
        try
        {
            var builder = _monitor?.Reports ?? new Reports.ReportBuilder(_store, _config.Reports.WriteCsv);
            var today = DateOnly.FromDateTime(DateTime.Now);
            var path = daily ? builder.BuildDaily(today) : builder.BuildWeekly(today);
            builder.BuildIndex();
            LoadReports();
            PostActivity(LogLevel.Success, $"Report written: {Path.GetFileName(path)}");
            Shell.Open(path);
        }
        catch (Exception ex)
        {
            PostActivity(LogLevel.Warning, $"Could not build the report: {ex.Message}");
        }
    }

    private void LoadReports()
    {
        ReportFiles.Clear();
        if (!Directory.Exists(_store.ReportsDirectory)) return;
        foreach (var f in new DirectoryInfo(_store.ReportsDirectory).GetFiles("*.html")
                     .Where(f => !f.Name.Equals("index.html", StringComparison.OrdinalIgnoreCase))
                     .OrderByDescending(f => f.LastWriteTime))
        {
            ReportFiles.Add(f);
        }
    }

    private void PlayClip(string? path)
    {
        if (path is null || !File.Exists(path)) return;
        // An encrypted clip needs the parent PIN before it can be decrypted for playback.
        if (EvidenceProtector.IsEncrypted(path) && !PinDialog.Authorize(_config.ParentPin, "Enter the parent PIN to play this protected clip.")) return;
        try
        {
            StopClip();
            var playable = _store.PrepareForViewing(path); // decrypts to a temp file if needed
            if (playable is null) { PostActivity(LogLevel.Warning, "Could not decrypt the clip."); return; }
            _playingTempFile = EvidenceProtector.IsEncrypted(path) ? playable : null;
            _player = new SoundPlayer(playable);
            _player.Play();
        }
        catch (Exception ex)
        {
            PostActivity(LogLevel.Warning, $"Could not play the clip: {ex.Message}");
        }
    }

    private void StopClip()
    {
        try { _player?.Stop(); _player?.Dispose(); } catch { }
        _player = null;
        if (_playingTempFile is not null)
        {
            try { File.Delete(_playingTempFile); } catch { }
            _playingTempFile = null;
        }
    }

    /// <summary>Opens an evidence file, decrypting it to a temporary file first if it is encrypted (PIN-gated).</summary>
    private void OpenEvidence(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        if (EvidenceProtector.IsEncrypted(path))
        {
            if (!PinDialog.Authorize(_config.ParentPin, "Enter the parent PIN to open this protected file.")) return;
            var temp = _store.PrepareForViewing(path);
            if (temp is not null) Shell.Open(temp); else PostActivity(LogLevel.Warning, "Could not decrypt the file.");
        }
        else
        {
            Shell.Open(path);
        }
    }

    // ---------------------------------------------------------------- exit

    public void RequestExit()
    {
        if (IsRunning && !PinDialog.Authorize(_config.ParentPin, "Enter the parent PIN to exit Galiluna Shield.")) return;
        if (IsRunning)
        {
            var answer = MessageBox.Show("Monitoring is running. Exit anyway? The child will no longer be protected until you start it again.",
                AppPaths.ProductName, MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;
        }
        ExitRequested?.Invoke();
    }

    private void Post(Action action)
    {
        if (_ui.CheckAccess()) action(); else _ui.BeginInvoke(action);
    }

    /// <summary>Records a Windows sign-out / shutdown in the program log (called from the session-ending handler).</summary>
    public void NoteWindowsShutdown() => _activity.Write(ActivityKind.WindowsShutdown, "Windows is shutting down or signing out; the shield is closing with it.");

    public void Dispose()
    {
        StopClip();
        _dashboard.Dispose();
        _monitor?.Dispose();
        _detector?.Dispose();
        _transcriber?.Dispose();
        _activity.Stop("app closed");
        _activity.Dispose();
    }
}
