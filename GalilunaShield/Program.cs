using System.Runtime.InteropServices;
using GalilunaShield;
using GalilunaShield.Alerts;
using GalilunaShield.Configuration;
using GalilunaShield.Detection;
using GalilunaShield.Hotkey;
using GalilunaShield.Transcription;

Console.OutputEncoding = System.Text.Encoding.UTF8;
try { Console.Title = AppPaths.ProductName; } catch { /* no console window */ }

// ---------------------------------------------------------------------------------------------------------
// Command line
//   --start                                  begin monitoring immediately
//   --mode microphone|system|both            override the configured listening mode
//   --record off|microphone|system|both|smart override the configured recording mode
//   --report [today|week|YYYY-MM-DD]         build the report(s) and exit
//   --minimized                              start with the console window minimized
//   --config <path>                          use a specific appsettings.json
// ---------------------------------------------------------------------------------------------------------
var startImmediately = false;
var minimized = false;
string? reportArg = null;
string? configOverride = null;
MonitorMode? modeOverride = null;
RecordingMode? recordOverride = null;

for (var i = 0; i < args.Length; i++)
{
    var a = args[i].ToLowerInvariant();
    string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{a} needs a value");
    switch (a)
    {
        case "--start": startImmediately = true; break;
        case "--minimized": minimized = true; break;
        case "--config": configOverride = Next(); break;
        case "--report": reportArg = i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[++i] : "today"; break;
        case "--mode":
            modeOverride = Next().ToLowerInvariant() switch
            {
                "mic" or "microphone" => MonitorMode.Microphone,
                "system" or "systemaudio" or "speakers" => MonitorMode.SystemAudio,
                "both" or "all" => MonitorMode.Both,
                var v => throw new ArgumentException($"Unknown --mode '{v}'. Use microphone, system or both."),
            };
            break;
        case "--record":
            recordOverride = Next().ToLowerInvariant() switch
            {
                "off" or "none" => RecordingMode.Off,
                "mic" or "microphone" => RecordingMode.Microphone,
                "system" or "systemaudio" or "speakers" => RecordingMode.SystemAudio,
                "both" or "all" => RecordingMode.Both,
                "smart" or "auto" => RecordingMode.Smart,
                var v => throw new ArgumentException($"Unknown --record '{v}'. Use off, microphone, system, both or smart."),
            };
            break;
        case "--help" or "-h" or "/?":
            PrintHelp();
            return 0;
        default:
            Console.Error.WriteLine($"Unknown option '{args[i]}'. Use --help.");
            return 2;
    }
}

if (minimized) MinimizeConsole();

AppPaths.EnsureUserConfig();
var configPath = configOverride ?? AppPaths.ConfigFile;
var config = AppConfig.Load(configPath);
if (modeOverride is not null) config.Mode = modeOverride.Value;
if (recordOverride is not null) config.Recording.Mode = recordOverride.Value;
startImmediately |= config.StartMonitoringOnLaunch;

var outputDir = AppPaths.ResolveOutputDirectory(config.OutputDirectory);
var evidenceProtector = new EvidenceProtector(config.Security.EncryptEvidence, config.ParentPin);
var store = new AlertStore(outputDir, config.SaveAudioClipOnAlert, evidenceProtector);

if (reportArg is not null)
{
    return BuildReportAndExit(store, config, reportArg);
}

// Monitoring requires accepted terms. Building reports (above) does not; viewing your own data is always allowed.
if (new ConsentManager().NeedsConsent())
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("Galiluna Shield has not been set up on this account yet.");
    Console.WriteLine("Open the Galiluna Shield desktop app once and accept the terms (parent/guardian consent");
    Console.WriteLine("and the licence, privacy and acceptable-use policies). Monitoring will not run until then.");
    Console.ResetColor();
    return 3;
}

Banner();

using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; shutdown.Cancel(); };

RedFlagDetector detector;
WhisperTranscriber transcriber;
try
{
    // Main list plus any optional word buckets the parent switched on.
    var buckets = GalilunaShield.Configuration.RedFlagBuckets.LoadCatalog(AppPaths.ShippedBucketsDirectory, AppPaths.UserBucketsDirectory);
    var enabled = buckets.Where(b => config.EnabledBuckets.Contains(b.Id, StringComparer.OrdinalIgnoreCase)).ToList();
    detector = new RedFlagDetector(
        () => GalilunaShield.Configuration.RedFlagBuckets.Combine(RedFlagFile.Load(AppPaths.RedFlagsFile), enabled),
        config.FuzzyMatching, config.FuzzyTolerance);
    Console.WriteLine($"Loaded {detector.RuleCount} red-flag words/phrases ({buckets.Count} optional buckets available, {enabled.Count} on).");
    Console.WriteLine("  (edit redflags.txt any time; changes are picked up automatically)");
    detector.Reloaded += count => Console.WriteLine($"{DateTime.Now:HH:mm:ss} Red-flag list reloaded: {count} words/phrases.");
    detector.ReloadFailed += ex => Console.WriteLine($"{DateTime.Now:HH:mm:ss} Could not reload red-flag list: {ex.Message}");
    detector.WatchFile(AppPaths.RedFlagsFile);

    if (!WhisperTranscriber.IsModelDownloaded(AppPaths.ModelDirectory, config.ModelSize))
    {
        Console.WriteLine($"First run: downloading the '{config.ModelSize}' speech model ({WhisperTranscriber.ModelSizeDescription(config.ModelSize)}).");
        Console.WriteLine("This happens once. Nothing else is ever downloaded or uploaded.");
    }
    var progress = new Progress<DownloadProgress>(p =>
    {
        var pct = p.Fraction is double f ? $"{f:P0}" : $"{p.BytesReceived / 1048576.0:F0} MB";
        Console.Write($"\r  downloading... {pct}   ");
        if (p.Fraction >= 1) Console.WriteLine();
    });
    transcriber = await WhisperTranscriber.CreateAsync(config.ModelSize, config.Language, config.BeamSize, AppPaths.ModelDirectory, progress, shutdown.Token, config.Threads);
    Console.WriteLine($"Speech recognition ready ({config.Performance} preset: model {config.ModelSize}, beam {config.BeamSize}; language {config.Language}; fuzzy matching {(config.FuzzyMatching ? "on" : "off")}).");
}
catch (OperationCanceledException)
{
    return 0;
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"Startup failed: {ex.Message}");
    Console.ResetColor();
    return 1;
}

using var activity = new ActivityLog(AppPaths.ResolveLogsDirectory(config.LogsDirectory, config.OutputDirectory), AppPaths.ProductName + " (console)");
activity.Start("console engine");
using var monitor = new MonitorService(config, transcriber, detector, store, activity, evidenceProtector);
monitor.Logged += PrintLog;
// Log a clean shutdown when the console is closed or the process exits.
AppDomain.CurrentDomain.ProcessExit += (_, _) => activity.Stop("process exit");

using var hotkey = new GlobalHotkey(config.Hotkey, () => monitor.Toggle("hotkey"));
try
{
    hotkey.Start();
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine(ex.Message);
    Console.WriteLine("You can still toggle monitoring by pressing SPACE in this window.");
    Console.ResetColor();
}

Console.WriteLine();
Console.WriteLine($"  Listening: {MonitorService.Describe(monitor.Mode)}");
Console.WriteLine($"  Recording: {monitor.DescribeRecording(monitor.RecordingMode)}");
Console.WriteLine();
Console.WriteLine($"  Press {hotkey.Description} (works from any window) to START / STOP monitoring.");
Console.WriteLine("  In this window: SPACE toggles, M cycles listening mode, R cycles recording mode, P builds the report, Q or Ctrl+C quits.");
Console.WriteLine($"  Output folder:  {store.RootDirectory}");
Console.WriteLine($"  Settings:       {configPath}");
Console.WriteLine();

if (startImmediately)
{
    monitor.Start();
}
else
{
    Console.WriteLine("Monitoring is currently OFF. (Tip: launch with --start to begin monitoring immediately.)");
}

// Keyboard loop for the console window itself (the global hotkey works regardless of focus).
// When input is redirected (e.g. launched by a script or service), keys aren't available; just idle until Ctrl+C.
var keyboardAvailable = !Console.IsInputRedirected;
while (!shutdown.IsCancellationRequested)
{
    if (keyboardAvailable && Console.KeyAvailable)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Q) break;
        if (key.Key == ConsoleKey.Spacebar) monitor.Toggle("keyboard");
        if (key.Key == ConsoleKey.M) monitor.CycleMode();
        if (key.Key == ConsoleKey.R) monitor.CycleRecordingMode();
        if (key.Key == ConsoleKey.P)
        {
            var path = monitor.RebuildReports();
            if (path is not null) Console.WriteLine($"{DateTime.Now:HH:mm:ss} Report written: {path}");
        }
    }
    else
    {
        await Task.Delay(100);
    }
}

monitor.Stop("app closing");
activity.Stop("user quit");
detector.Dispose();
transcriber.Dispose();
Console.WriteLine($"Goodbye. Alerts raised this session: {monitor.AlertCount}");
return 0;

// ---------------------------------------------------------------------------------------------------------

static void PrintLog(LogEntry e)
{
    var color = e.Level switch
    {
        LogLevel.Debug => ConsoleColor.DarkGray,
        LogLevel.Success => ConsoleColor.Green,
        LogLevel.Warning => ConsoleColor.Yellow,
        LogLevel.Error => ConsoleColor.Red,
        LogLevel.Alert => e.Message.StartsWith("POSSIBLE") ? ConsoleColor.Magenta : ConsoleColor.Red,
        _ => ConsoleColor.Gray,
    };
    lock (Console.Out)
    {
        var prev = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine($"{e.At:HH:mm:ss} {(e.Level == LogLevel.Alert ? "!!! " : "")}{e.Message}");
        Console.ForegroundColor = prev;
    }
    if (e.Level == LogLevel.Alert && !e.Message.StartsWith("PROOF"))
    {
        try { Console.Beep(1000, 200); } catch { /* no sound device */ }
    }
}

static int BuildReportAndExit(AlertStore store, AppConfig config, string what)
{
    var reports = new GalilunaShield.Reports.ReportBuilder(store, config.Reports.WriteCsv);
    string path;
    switch (what.ToLowerInvariant())
    {
        case "today": path = reports.BuildDaily(DateOnly.FromDateTime(DateTime.Now)); break;
        case "week": path = reports.BuildWeekly(DateOnly.FromDateTime(DateTime.Now)); break;
        default:
            if (!DateOnly.TryParse(what, out var day))
            {
                Console.Error.WriteLine($"Unknown report '{what}'. Use today, week or a date like 2026-09-23.");
                return 2;
            }
            path = reports.BuildDaily(day);
            break;
    }
    reports.BuildIndex();
    Console.WriteLine(path);
    return 0;
}

static void PrintHelp()
{
    Console.WriteLine("""
        Galiluna Shield - command-line engine

          galiluna [--start] [--minimized] [--mode microphone|system|both]
                   [--record off|microphone|system|both|smart] [--config <appsettings.json>]
          galiluna --report [today|week|YYYY-MM-DD]

        Settings live in %APPDATA%\Galiluna Shield\appsettings.json; the red-flag list in redflags.txt next to it.
        Output (alerts, transcripts, recordings, reports) goes to Documents\Galiluna Shield.
        """);
}

static void Banner()
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine("""
       ____       _ _ _                     ____  _     _      _     _
      / ___| __ _| (_) |_   _ _ __   __ _  / ___|| |__ (_) ___| | __| |
     | |  _ / _` | | | | | | | '_ \ / _` | \___ \| '_ \| |/ _ \ |/ _` |
     | |_| | (_| | | | | |_| | | | | (_| |  ___) | | | | |  __/ | (_| |
      \____|\__,_|_|_|_|\__,_|_| |_|\__,_| |____/|_| |_|_|\___|_|\__,_|
                         Audio red-flag monitor for kids' safety
    """);
    Console.ResetColor();
}

static void MinimizeConsole()
{
    try
    {
        var handle = GetConsoleWindow();
        if (handle != IntPtr.Zero) ShowWindow(handle, 6 /* SW_MINIMIZE */);
    }
    catch { /* not fatal */ }
}

[DllImport("kernel32.dll")] static extern IntPtr GetConsoleWindow();
[DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
