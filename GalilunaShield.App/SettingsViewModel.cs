using System.Windows;
using GalilunaShield.Configuration;

namespace GalilunaShield.App;

/// <summary>Editable copy of the settings a parent is likely to touch. Save writes appsettings.json.</summary>
public sealed class SettingsViewModel : ObservableObject
{
    private readonly ShellViewModel _shell;
    private AppConfig _config;

    public SettingsViewModel(AppConfig config, ShellViewModel shell)
    {
        _config = config;
        _shell = shell;
        LoadFrom(config);
        SaveCommand = new RelayCommand(async () => await SaveAsync());
        RevertCommand = new RelayCommand(() => LoadFrom(_config));
        BrowseOutputCommand = new RelayCommand(BrowseOutput);
        BrowseLogsCommand = new RelayCommand(BrowseLogs);
        OpenConfigFileCommand = new RelayCommand(() => Shell.Open(AppPaths.ConfigFile));
        ToggleKeepAliveCommand = new RelayCommand(ToggleKeepAlive);
        RefreshKeepAlive();
    }

    public RelayCommand SaveCommand { get; }
    public RelayCommand RevertCommand { get; }
    public RelayCommand BrowseOutputCommand { get; }
    public RelayCommand BrowseLogsCommand { get; }
    public RelayCommand OpenConfigFileCommand { get; }
    public RelayCommand ToggleKeepAliveCommand { get; }

    // Pass-throughs to the shell for notification test and the web dashboard.
    public RelayCommand TestNotificationCommand => _shell.TestNotificationCommand;
    public RelayCommand StartDashboardCommand => _shell.StartDashboardCommand;
    public RelayCommand StopDashboardCommand => _shell.StopDashboardCommand;
    public RelayCommand OpenDashboardCommand => _shell.OpenDashboardCommand;
    public bool DashboardInstalled => _shell.DashboardInstalled;
    public string DashboardStatus => _shell.DashboardStatus;
    public string DashboardUrl => _shell.DashboardUrl;

    private bool _keepAlive;
    public bool KeepAliveInstalled { get => _keepAlive; private set { if (Set(ref _keepAlive, value)) { OnPropertyChanged(nameof(KeepAliveText)); OnPropertyChanged(nameof(KeepAliveButtonText)); } } }
    public string KeepAliveText => _keepAlive
        ? "On: if Galiluna Shield is closed or killed, Windows restarts it within a few minutes, and the gap is recorded in the report."
        : "Off: if the app is closed, it stays closed until reopened. Turn on to auto-restart it (needs administrator approval once).";
    public string KeepAliveButtonText => _keepAlive ? "Turn off keep-alive" : "Turn on keep-alive (needs admin)";

    private void RefreshKeepAlive() => KeepAliveInstalled = Watchdog.IsInstalled();

    private void ToggleKeepAlive()
    {
        if (!PinDialog.Authorize(_config.ParentPin, "Enter the parent PIN to change keep-alive.")) return;
        var want = !_keepAlive;
        var ok = Watchdog.RequestSet(want);
        RefreshKeepAlive();
        Status = ok
            ? (want ? "Keep-alive turned on." : "Keep-alive turned off.")
            : "Keep-alive was not changed (administrator approval was needed and not given).";
    }

    public IEnumerable<MonitorMode> Modes => Enum.GetValues<MonitorMode>();
    public IEnumerable<RecordingMode> RecordingModes => Enum.GetValues<RecordingMode>();
    public IEnumerable<PerformancePreset> Presets => Enum.GetValues<PerformancePreset>();
    public IEnumerable<Severity> Severities => Enum.GetValues<Severity>();
    public IEnumerable<string> ModelSizes => new[] { "tiny", "base", "small", "medium", "large" };
    public IEnumerable<string> Languages => new[] { "auto", "en", "pt", "es", "fr", "de", "it", "nl", "pl", "ru", "ja", "zh", "ko", "ar", "hi", "tr" };
    public string SuggestedPresetText => $"This computer looks best suited to: {AppConfig.SuggestedPreset()}.";

    private MonitorMode _mode; public MonitorMode Mode { get => _mode; set => Set(ref _mode, value); }
    private RecordingMode _recordingMode;
    public RecordingMode RecordingMode
    {
        get => _recordingMode;
        set { if (Set(ref _recordingMode, value)) { OnPropertyChanged(nameof(IsSmart)); OnPropertyChanged(nameof(IsContinuousRecording)); } }
    }
    public bool IsSmart => RecordingMode == RecordingMode.Smart;
    public bool IsContinuousRecording => RecordingMode is RecordingMode.Microphone or RecordingMode.SystemAudio or RecordingMode.Both;
    private int _smartAlerts; public int SmartTriggerAlerts { get => _smartAlerts; set => Set(ref _smartAlerts, value); }
    private double _smartWindow; public double SmartTriggerWindowMinutes { get => _smartWindow; set => Set(ref _smartWindow, value); }
    private bool _smartCritical; public bool SmartTriggerOnCritical { get => _smartCritical; set => Set(ref _smartCritical, value); }
    private double _smartRecord; public double SmartRecordMinutes { get => _smartRecord; set => Set(ref _smartRecord, value); }
    private int _preRoll; public int PreRollSeconds { get => _preRoll; set => Set(ref _preRoll, value); }
    private int _maxFileMinutes; public int MaxFileMinutes { get => _maxFileMinutes; set => Set(ref _maxFileMinutes, Math.Clamp(value, 1, 240)); }

    private PerformancePreset _preset;
    public PerformancePreset Performance { get => _preset; set { if (Set(ref _preset, value)) OnPropertyChanged(nameof(PresetDescription)); } }
    public string PresetDescription => AppConfig.DescribePreset(Performance);

    private string _modelSize = "base"; public string ModelSize { get => _modelSize; set { if (Set(ref _modelSize, value)) OnPropertyChanged(nameof(ModelDescription)); } }
    public string ModelDescription => Transcription.WhisperTranscriber.ModelSizeDescription(ModelSize);
    private string _language = "auto"; public string Language { get => _language; set => Set(ref _language, value); }
    private int _beam; public int BeamSize { get => _beam; set => Set(ref _beam, Math.Clamp(value, 1, 8)); }

    // Auto-delete of saved audio
    private bool _retentionEnabled; public bool RetentionEnabled { get => _retentionEnabled; set => Set(ref _retentionEnabled, value); }
    private int _retentionDays; public int RetentionDays { get => _retentionDays; set => Set(ref _retentionDays, Math.Clamp(value, 1, 3650)); }
    private bool _retentionRecordings; public bool RetentionIncludeRecordings { get => _retentionRecordings; set => Set(ref _retentionRecordings, value); }
    private bool _retentionScreenshots; public bool RetentionIncludeScreenshots { get => _retentionScreenshots; set => Set(ref _retentionScreenshots, value); }
    private bool _retentionKeepText; public bool RetentionKeepTextRecord { get => _retentionKeepText; set => Set(ref _retentionKeepText, value); }

    // Screenshots at a red flag
    private bool _shotsEnabled; public bool ScreenshotsEnabled { get => _shotsEnabled; set => Set(ref _shotsEnabled, value); }
    private Severity _shotSeverity; public Severity ScreenshotMinSeverity { get => _shotSeverity; set => Set(ref _shotSeverity, value); }
    private int _shotQuality; public int ScreenshotQuality { get => _shotQuality; set => Set(ref _shotQuality, Math.Clamp(value, 20, 100)); }

    // Adult-voice detection
    private bool _voiceEnabled; public bool VoiceEnabled { get => _voiceEnabled; set => Set(ref _voiceEnabled, value); }
    private bool _voiceOnSystem; public bool VoiceFlagOnSystemAudio { get => _voiceOnSystem; set => Set(ref _voiceOnSystem, value); }
    private bool _voiceOnMic; public bool VoiceFlagOnMicrophone { get => _voiceOnMic; set => Set(ref _voiceOnMic, value); }

    // Hidden start
    private bool _startHidden; public bool StartHidden { get => _startHidden; set => Set(ref _startHidden, value); }
    private string _showHotkey = ""; public string ShowWindowHotkey { get => _showHotkey; set => Set(ref _showHotkey, value); }

    private string _outputDirectory = ""; public string OutputDirectory { get => _outputDirectory; set => Set(ref _outputDirectory, value); }
    private string _logsDirectory = ""; public string LogsDirectory { get => _logsDirectory; set { if (Set(ref _logsDirectory, value)) OnPropertyChanged(nameof(LogsDirectoryEffective)); } }
    /// <summary>Shows the parent where the log actually goes when they leave the box on the default.</summary>
    public string LogsDirectoryEffective => string.IsNullOrWhiteSpace(LogsDirectory)
        ? System.IO.Path.Combine(string.IsNullOrWhiteSpace(OutputDirectory) ? "(output folder)" : OutputDirectory, "events") + "  (default: alongside your other data)"
        : System.IO.Path.Combine(LogsDirectory, "events");
    private bool _saveClips; public bool SaveAudioClipOnAlert { get => _saveClips; set => Set(ref _saveClips, value); }
    private bool _fuzzy; public bool FuzzyMatching { get => _fuzzy; set => Set(ref _fuzzy, value); }
    private bool _saveUnclear; public bool SaveUnclearClips { get => _saveUnclear; set => Set(ref _saveUnclear, value); }
    private double _lowConf; public double LowConfidencePercent { get => _lowConf; set => Set(ref _lowConf, Math.Clamp(value, 0, 100)); }
    private double _silence; public double SilenceThreshold { get => _silence; set => Set(ref _silence, value); }

    private bool _autoMic; public bool AutoSelectMicrophone { get => _autoMic; set => Set(ref _autoMic, value); }
    private string _micName = ""; public string MicrophoneDeviceName { get => _micName; set => Set(ref _micName, value); }
    private string _ignore = ""; public string IgnoreProcesses { get => _ignore; set => Set(ref _ignore, value); }
    private string _focus = ""; public string FocusProcesses { get => _focus; set => Set(ref _focus, value); }
    private string _mutedWords = ""; public string MutedWords { get => _mutedWords; set => Set(ref _mutedWords, value); }
    private string _mutedApps = ""; public string MutedApps { get => _mutedApps; set => Set(ref _mutedApps, value); }

    private string _hotkey = ""; public string Hotkey { get => _hotkey; set => Set(ref _hotkey, value); }
    private bool _startOnLaunch; public bool StartMonitoringOnLaunch { get => _startOnLaunch; set => Set(ref _startOnLaunch, value); }
    private bool _startWithWindows; public bool StartWithWindows { get => _startWithWindows; set => Set(ref _startWithWindows, value); }
    private string _pin = ""; public string ParentPin { get => _pin; set => Set(ref _pin, value); }
    private bool _autoReports; public bool AutoGenerateReports { get => _autoReports; set => Set(ref _autoReports, value); }
    private bool _encrypt; public bool EncryptEvidence { get => _encrypt; set => Set(ref _encrypt, value); }

    // Remote notifications
    public IEnumerable<Severity> Severities2 => Enum.GetValues<Severity>();
    private bool _notify; public bool NotificationsEnabled { get => _notify; set => Set(ref _notify, value); }
    private Severity _notifySev; public Severity NotifyMinSeverity { get => _notifySev; set => Set(ref _notifySev, value); }
    private bool _emailOn; public bool EmailEnabled { get => _emailOn; set => Set(ref _emailOn, value); }
    private string _smtpHost = ""; public string SmtpHost { get => _smtpHost; set => Set(ref _smtpHost, value); }
    private int _smtpPort = 587; public int SmtpPort { get => _smtpPort; set => Set(ref _smtpPort, value); }
    private bool _smtpSsl = true; public bool SmtpSsl { get => _smtpSsl; set => Set(ref _smtpSsl, value); }
    private string _smtpUser = ""; public string SmtpUser { get => _smtpUser; set => Set(ref _smtpUser, value); }
    private string _smtpPass = ""; public string SmtpPassword { get => _smtpPass; set => Set(ref _smtpPass, value); }
    private string _emailTo = ""; public string EmailTo { get => _emailTo; set => Set(ref _emailTo, value); }
    private bool _pushOn; public bool WebhookEnabled { get => _pushOn; set => Set(ref _pushOn, value); }
    public IEnumerable<string> WebhookTypes => new[] { "ntfy", "telegram", "slack", "discord", "generic" };
    private string _pushType = "ntfy"; public string WebhookType { get => _pushType; set => Set(ref _pushType, value); }
    private string _pushUrl = ""; public string WebhookUrl { get => _pushUrl; set => Set(ref _pushUrl, value); }
    private string _tgToken = ""; public string TelegramBotToken { get => _tgToken; set => Set(ref _tgToken, value); }
    private string _tgChat = ""; public string TelegramChatId { get => _tgChat; set => Set(ref _tgChat, value); }

    // Web dashboard
    private bool _webOn; public bool WebEnabled { get => _webOn; set => Set(ref _webOn, value); }
    private int _webPort = 8787; public int WebPort { get => _webPort; set => Set(ref _webPort, value); }

    private string _status = ""; public string Status { get => _status; private set => Set(ref _status, value); }

    private void LoadFrom(AppConfig c)
    {
        Mode = c.Mode;
        RecordingMode = c.Recording.Mode;
        SmartTriggerAlerts = c.Recording.SmartTriggerAlerts;
        SmartTriggerWindowMinutes = c.Recording.SmartTriggerWindowMinutes;
        SmartTriggerOnCritical = c.Recording.SmartTriggerOnCritical;
        SmartRecordMinutes = c.Recording.SmartRecordMinutes;
        PreRollSeconds = c.Recording.PreRollSeconds;
        MaxFileMinutes = c.Recording.MaxFileMinutes;
        Performance = c.Performance;
        ModelSize = c.ModelSize;
        Language = c.Language;
        BeamSize = c.BeamSize;
        RetentionEnabled = c.Retention.Enabled;
        RetentionDays = c.Retention.Days;
        RetentionIncludeRecordings = c.Retention.IncludeRecordings;
        RetentionIncludeScreenshots = c.Retention.IncludeScreenshots;
        RetentionKeepTextRecord = c.Retention.KeepTextRecord;
        ScreenshotsEnabled = c.Screenshots.Enabled;
        ScreenshotMinSeverity = c.Screenshots.MinSeverity;
        ScreenshotQuality = c.Screenshots.JpegQuality;
        VoiceEnabled = c.VoiceAnalysis.Enabled;
        VoiceFlagOnSystemAudio = c.VoiceAnalysis.FlagOnSystemAudio;
        VoiceFlagOnMicrophone = c.VoiceAnalysis.FlagOnMicrophone;
        StartHidden = c.StartHidden;
        ShowWindowHotkey = c.ShowWindowHotkey;
        OutputDirectory = AppPaths.ResolveOutputDirectory(c.OutputDirectory);
        LogsDirectory = c.LogsDirectory; // empty = default (alongside output)
        SaveAudioClipOnAlert = c.SaveAudioClipOnAlert;
        FuzzyMatching = c.FuzzyMatching;
        SaveUnclearClips = c.SaveUnclearClips;
        LowConfidencePercent = Math.Round(c.LowConfidenceThreshold * 100);
        SilenceThreshold = Math.Round(c.SilenceThreshold, 4);
        AutoSelectMicrophone = c.Microphone.AutoSelectDevice;
        MicrophoneDeviceName = c.Microphone.DeviceName;
        IgnoreProcesses = string.Join(", ", c.SystemAudio.IgnoreProcesses);
        FocusProcesses = string.Join(", ", c.SystemAudio.FocusProcesses);
        MutedWords = string.Join(", ", c.MutedWords);
        MutedApps = string.Join(", ", c.MutedApps);
        Hotkey = c.Hotkey;
        StartMonitoringOnLaunch = c.StartMonitoringOnLaunch;
        StartWithWindows = Autostart.IsEnabled();
        ParentPin = c.ParentPin;
        AutoGenerateReports = c.Reports.AutoGenerate;
        EncryptEvidence = c.Security.EncryptEvidence;
        NotificationsEnabled = c.Notifications.Enabled;
        NotifyMinSeverity = c.Notifications.MinSeverity;
        EmailEnabled = c.Notifications.Email.Enabled;
        SmtpHost = c.Notifications.Email.SmtpHost;
        SmtpPort = c.Notifications.Email.SmtpPort;
        SmtpSsl = c.Notifications.Email.UseSsl;
        SmtpUser = c.Notifications.Email.Username;
        SmtpPassword = c.Notifications.Email.Password;
        EmailTo = c.Notifications.Email.To;
        WebhookEnabled = c.Notifications.Webhook.Enabled;
        WebhookType = c.Notifications.Webhook.Type;
        WebhookUrl = c.Notifications.Webhook.Url;
        TelegramBotToken = c.Notifications.Webhook.BotToken;
        TelegramChatId = c.Notifications.Webhook.ChatId;
        WebEnabled = c.Web.Enabled;
        WebPort = c.Web.Port;
        Status = "";
    }

    private async Task SaveAsync()
    {
        if (!PinDialog.Authorize(_config.ParentPin, "Enter the current parent PIN to change settings.")) return;

        if (ParentPin.Length > 0 && (ParentPin.Length is < 4 or > 8 || !ParentPin.All(char.IsDigit)))
        {
            Status = "The parent PIN must be 4 to 8 digits (or empty for no PIN).";
            return;
        }
        foreach (var (combo, label) in new[] { (Hotkey, "start/stop"), (ShowWindowHotkey, "show-window") })
        {
            if (string.IsNullOrWhiteSpace(combo) && label == "show-window") continue; // show-window hotkey is optional
            try { _ = new Hotkey.GlobalHotkey(combo, () => { }); }
            catch (Exception ex) { Status = $"The {label} hotkey is invalid: {ex.Message}"; return; }
        }

        var c = AppConfig.Load(AppPaths.ConfigFile); // keep any advanced settings we don't show
        c.Performance = Performance;
        // The preset dictates model/beam/threads; only honour the manual model boxes in Custom mode.
        if (Performance == PerformancePreset.Custom) { c.ModelSize = ModelSize; c.BeamSize = BeamSize; }
        c.ApplyPerformancePreset();

        var needsEngineRestart = c.ModelSize != ModelSize || c.Language != Language || c.BeamSize != BeamSize || c.Performance != _config.Performance ||
                                 AppPaths.ResolveOutputDirectory(c.OutputDirectory) != OutputDirectory ||
                                 c.FuzzyMatching != FuzzyMatching || c.SaveAudioClipOnAlert != SaveAudioClipOnAlert ||
                                 Math.Abs(c.LowConfidenceThreshold - LowConfidencePercent / 100) > 0.001 ||
                                 Math.Abs(c.SilenceThreshold - SilenceThreshold) > 0.00001 ||
                                 c.Microphone.AutoSelectDevice != AutoSelectMicrophone || c.Microphone.DeviceName != MicrophoneDeviceName ||
                                 string.Join(",", c.SystemAudio.IgnoreProcesses) != string.Join(",", SplitList(IgnoreProcesses)) ||
                                 string.Join(",", c.SystemAudio.FocusProcesses) != string.Join(",", SplitList(FocusProcesses)) ||
                                 string.Join(",", c.MutedWords) != string.Join(",", SplitList(MutedWords)) ||
                                 string.Join(",", c.MutedApps) != string.Join(",", SplitList(MutedApps)) ||
                                 c.Recording.SmartTriggerAlerts != SmartTriggerAlerts || Math.Abs(c.Recording.SmartTriggerWindowMinutes - SmartTriggerWindowMinutes) > 0.001 ||
                                 c.Recording.SmartTriggerOnCritical != SmartTriggerOnCritical || Math.Abs(c.Recording.SmartRecordMinutes - SmartRecordMinutes) > 0.001 ||
                                 c.Recording.PreRollSeconds != PreRollSeconds || c.Recording.MaxFileMinutes != MaxFileMinutes ||
                                 c.SaveUnclearClips != SaveUnclearClips || c.Reports.AutoGenerate != AutoGenerateReports ||
                                 c.Retention.Enabled != RetentionEnabled || c.Retention.Days != RetentionDays ||
                                 c.Retention.IncludeRecordings != RetentionIncludeRecordings || c.Retention.IncludeScreenshots != RetentionIncludeScreenshots ||
                                 c.Retention.KeepTextRecord != RetentionKeepTextRecord ||
                                 c.Screenshots.Enabled != ScreenshotsEnabled || c.Screenshots.MinSeverity != ScreenshotMinSeverity || c.Screenshots.JpegQuality != ScreenshotQuality ||
                                 c.VoiceAnalysis.Enabled != VoiceEnabled || c.VoiceAnalysis.FlagOnSystemAudio != VoiceFlagOnSystemAudio || c.VoiceAnalysis.FlagOnMicrophone != VoiceFlagOnMicrophone ||
                                 c.Security.EncryptEvidence != EncryptEvidence ||
                                 c.Notifications.Enabled != NotificationsEnabled || c.Notifications.MinSeverity != NotifyMinSeverity ||
                                 c.Notifications.Email.Enabled != EmailEnabled || c.Notifications.Email.SmtpHost != SmtpHost.Trim() || c.Notifications.Email.To != EmailTo.Trim() ||
                                 c.Notifications.Email.Username != SmtpUser.Trim() || c.Notifications.Email.Password != SmtpPassword ||
                                 c.Notifications.Webhook.Enabled != WebhookEnabled || c.Notifications.Webhook.Url != WebhookUrl.Trim() || c.Notifications.Webhook.Type != WebhookType;
        var hotkeyChanged = c.Hotkey != Hotkey || c.ShowWindowHotkey != ShowWindowHotkey;
        var logsChanged = (c.LogsDirectory ?? "") != LogsDirectory.Trim();

        c.Mode = Mode;
        c.Recording.Mode = RecordingMode;
        c.Recording.SmartTriggerAlerts = Math.Max(1, SmartTriggerAlerts);
        c.Recording.SmartTriggerWindowMinutes = Math.Max(0.5, SmartTriggerWindowMinutes);
        c.Recording.SmartTriggerOnCritical = SmartTriggerOnCritical;
        c.Recording.SmartRecordMinutes = Math.Max(0.5, SmartRecordMinutes);
        c.Recording.PreRollSeconds = Math.Clamp(PreRollSeconds, 0, 600);
        c.Recording.MaxFileMinutes = MaxFileMinutes;
        c.Language = Language;
        c.OutputDirectory = OutputDirectory;
        c.LogsDirectory = LogsDirectory.Trim();
        c.SaveAudioClipOnAlert = SaveAudioClipOnAlert;
        c.FuzzyMatching = FuzzyMatching;
        c.SaveUnclearClips = SaveUnclearClips;
        c.LowConfidenceThreshold = (float)(LowConfidencePercent / 100);
        c.SilenceThreshold = (float)SilenceThreshold;
        c.Microphone.AutoSelectDevice = AutoSelectMicrophone;
        c.Microphone.DeviceName = MicrophoneDeviceName.Trim();
        c.SystemAudio.IgnoreProcesses = SplitList(IgnoreProcesses);
        c.SystemAudio.FocusProcesses = SplitList(FocusProcesses);
        c.MutedWords = SplitList(MutedWords);
        c.MutedApps = SplitList(MutedApps);
        c.Retention.Enabled = RetentionEnabled;
        c.Retention.Days = RetentionDays;
        c.Retention.IncludeRecordings = RetentionIncludeRecordings;
        c.Retention.IncludeScreenshots = RetentionIncludeScreenshots;
        c.Retention.KeepTextRecord = RetentionKeepTextRecord;
        c.Screenshots.Enabled = ScreenshotsEnabled;
        c.Screenshots.MinSeverity = ScreenshotMinSeverity;
        c.Screenshots.JpegQuality = ScreenshotQuality;
        c.VoiceAnalysis.Enabled = VoiceEnabled;
        c.VoiceAnalysis.FlagOnSystemAudio = VoiceFlagOnSystemAudio;
        c.VoiceAnalysis.FlagOnMicrophone = VoiceFlagOnMicrophone;
        c.Hotkey = Hotkey.Trim();
        c.ShowWindowHotkey = ShowWindowHotkey.Trim();
        c.StartHidden = StartHidden;
        c.StartMonitoringOnLaunch = StartMonitoringOnLaunch;
        c.ParentPin = ParentPin;
        c.Reports.AutoGenerate = AutoGenerateReports;
        c.Security.EncryptEvidence = EncryptEvidence;
        c.Notifications.Enabled = NotificationsEnabled;
        c.Notifications.MinSeverity = NotifyMinSeverity;
        c.Notifications.Email.Enabled = EmailEnabled;
        c.Notifications.Email.SmtpHost = SmtpHost.Trim();
        c.Notifications.Email.SmtpPort = SmtpPort;
        c.Notifications.Email.UseSsl = SmtpSsl;
        c.Notifications.Email.Username = SmtpUser.Trim();
        c.Notifications.Email.Password = SmtpPassword;
        c.Notifications.Email.To = EmailTo.Trim();
        c.Notifications.Webhook.Enabled = WebhookEnabled;
        c.Notifications.Webhook.Type = WebhookType;
        c.Notifications.Webhook.Url = WebhookUrl.Trim();
        c.Notifications.Webhook.BotToken = TelegramBotToken.Trim();
        c.Notifications.Webhook.ChatId = TelegramChatId.Trim();
        c.Web.Enabled = WebEnabled;
        c.Web.Port = WebPort;

        try
        {
            c.Save(AppPaths.ConfigFile);
            Autostart.Set(StartWithWindows);
        }
        catch (Exception ex)
        {
            Status = $"Could not save: {ex.Message}";
            return;
        }

        _config = c;
        if (needsEngineRestart)
        {
            Status = "Saved. Restarting the engine with the new settings...";
            await _shell.ReinitializeAsync(c);
            Status = "Saved and applied.";
        }
        else
        {
            // Live-applicable bits
            _shell.SetModeCommand.Execute(Mode);
            _shell.SetRecordingModeCommand.Execute(RecordingMode);
            Status = hotkeyChanged ? "Saved. The new hotkey takes effect the next time the app starts." : "Saved.";
        }
        LoadFrom(c);
        if (hotkeyChanged) Status = "Saved. The new hotkey takes effect the next time the app starts.";
        if (logsChanged) Status = "Saved. The activity-log location changes the next time the app starts.";
    }

    private void BrowseLogs()
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Choose where the activity/event log is saved",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(LogsDirectory) ? LogsDirectory : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            LogsDirectory = dlg.SelectedPath;
        }
    }

    private static List<string> SplitList(string text) =>
        text.Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private void BrowseOutput()
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Choose where alerts, recordings and reports are saved",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(OutputDirectory) ? OutputDirectory : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            OutputDirectory = dlg.SelectedPath;
        }
    }
}

/// <summary>Editor for redflags.txt with live validation.</summary>
public sealed class WordListViewModel : ObservableObject
{
    private readonly ShellViewModel _shell;

    public WordListViewModel(ShellViewModel shell)
    {
        _shell = shell;
        SaveCommand = new RelayCommand(Save);
        ReloadCommand = new RelayCommand(Refresh);
        OpenInNotepadCommand = new RelayCommand(() => Shell.Open(AppPaths.RedFlagsFile));
        Refresh();
    }

    public RelayCommand SaveCommand { get; }
    public RelayCommand ReloadCommand { get; }
    public RelayCommand OpenInNotepadCommand { get; }

    private string _text = "";
    public string Text
    {
        get => _text;
        set { if (Set(ref _text, value)) Validate(); }
    }

    private string _summary = "";
    public string Summary { get => _summary; private set => Set(ref _summary, value); }

    private string _status = "";
    public string Status { get => _status; private set => Set(ref _status, value); }

    public void Refresh()
    {
        try
        {
            _text = File.Exists(AppPaths.RedFlagsFile) ? File.ReadAllText(AppPaths.RedFlagsFile) : "";
            OnPropertyChanged(nameof(Text));
            Validate();
            Status = "";
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
    }

    private void Validate()
    {
        var entries = RedFlagFile.Parse(Text.Split('\n'));
        var byCat = entries.GroupBy(e => e.Category).Select(g => $"{g.Key} ({g.Count()})");
        Summary = entries.Count == 0
            ? "No words yet."
            : $"{entries.Count} words/phrases in {entries.Select(e => e.Category).Distinct().Count()} categories: {string.Join(", ", byCat)}";
    }

    private void Save()
    {
        if (!PinDialog.Authorize(_shell.Config.ParentPin, "Enter the parent PIN to change the word list.")) return;
        try
        {
            File.WriteAllText(AppPaths.RedFlagsFile, Text.Replace("\r\n", "\n").Replace("\n", Environment.NewLine));
            Status = $"Saved {DateTime.Now:HH:mm:ss}. The engine picks the change up automatically.";
        }
        catch (Exception ex)
        {
            Status = $"Could not save: {ex.Message}";
        }
    }
}
