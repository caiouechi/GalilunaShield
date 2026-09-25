using System.Text.Json;
using System.Text.Json.Serialization;

namespace GalilunaShield.Configuration;

/// <summary>What the monitor listens to.</summary>
public enum MonitorMode
{
    /// <summary>Only what is said into the microphone (the child speaking). Default.</summary>
    Microphone,
    /// <summary>Only what the computer plays, filtered to calls and games (browsers and media players are skipped).</summary>
    SystemAudio,
    /// <summary>Both of the above.</summary>
    Both,
}

/// <summary>How much CPU the speech recognizer may use. Maps to model size, beam width and threads.</summary>
public enum PerformancePreset
{
    /// <summary>Tiny model, greedy decoding. For older laptops and low-power PCs.</summary>
    Light,
    /// <summary>Base model, beam 3. Good on any modern PC.</summary>
    Balanced,
    /// <summary>Small model, beam 5. Best accuracy; needs a reasonably fast CPU.</summary>
    Accurate,
    /// <summary>Use ModelSize / BeamSize / Threads exactly as set.</summary>
    Custom,
}

public sealed class AppConfig
{
    public string Hotkey { get; set; } = "Ctrl+Alt+S";

    /// <summary>Hotkey that brings the desktop window back when it runs hidden. Empty to disable.</summary>
    public string ShowWindowHotkey { get; set; } = "Ctrl+Alt+G";

    [JsonConverter(typeof(JsonStringEnumConverter<MonitorMode>))]
    public MonitorMode Mode { get; set; } = MonitorMode.Microphone;

    public SystemAudioConfig SystemAudio { get; set; } = new();

    public MicrophoneConfig Microphone { get; set; } = new();

    public RecordingConfig Recording { get; set; } = new();

    public RetentionConfig Retention { get; set; } = new();

    public ScreenshotConfig Screenshots { get; set; } = new();

    public SecurityConfig Security { get; set; } = new();

    public NotificationConfig Notifications { get; set; } = new();

    public WebDashboardConfig Web { get; set; } = new();

    public ReportsConfig Reports { get; set; } = new();

    public VoiceAnalysisConfig VoiceAnalysis { get; set; } = new();

    /// <summary>Rules that raise an alert when many flags of one category pile up ("cursing a lot").</summary>
    public List<BurstRule> Bursts { get; set; } = new() { new BurstRule() };

    /// <summary>Ids (file names) of the optional word buckets the parent switched on.</summary>
    public List<string> EnabledBuckets { get; set; } = new();

    /// <summary>Words/phrases the parent muted from the alerts view: matched but never alerted on again.</summary>
    public List<string> MutedWords { get; set; } = new();

    /// <summary>Friendly app names the parent marked "this is fine": still transcribed, but no alerts raised.</summary>
    public List<string> MutedApps { get; set; } = new();

    [JsonConverter(typeof(JsonStringEnumConverter<PerformancePreset>))]
    public PerformancePreset Performance { get; set; } = PerformancePreset.Balanced;

    /// <summary>tiny | base | small | medium | large. Overwritten by <see cref="Performance"/> unless Custom.</summary>
    public string ModelSize { get; set; } = "base";
    /// <summary>ISO language code (e.g. "en", "pt") or "auto".</summary>
    public string Language { get; set; } = "auto";
    /// <summary>Whisper beam-search width. 1 = fastest (greedy); 3-5 = more accurate, more CPU.</summary>
    public int BeamSize { get; set; } = 3;
    /// <summary>Recognizer threads. 0 = automatic (half the cores).</summary>
    public int Threads { get; set; } = 0;

    /// <summary>Relative to Documents, or an absolute path.</summary>
    public string OutputDirectory { get; set; } = "Galiluna Shield";

    /// <summary>
    /// Where the program activity/event log (start, stop, shutdown, tamper) and diagnostic logs are saved.
    /// Empty = alongside the output folder. Otherwise a folder path (relative to Documents, or absolute).
    /// </summary>
    public string LogsDirectory { get; set; } = "";
    public int ContextSegments { get; set; } = 4;
    public bool SaveAudioClipOnAlert { get; set; } = true;
    public double MinChunkSeconds { get; set; } = 3.0;
    public double MaxChunkSeconds { get; set; } = 10.0;
    public double SilenceSeconds { get; set; } = 0.7;
    /// <summary>RMS level below which audio counts as silence. Lower picks up quieter, farther-away speech.</summary>
    public float SilenceThreshold { get; set; } = 0.005f;

    /// <summary>Also flag words/phrases the recognizer probably mis-heard slightly ("kill my sale" ~ "kill myself").</summary>
    public bool FuzzyMatching { get; set; } = true;
    /// <summary>Fraction of a phrase's letters allowed to differ for a fuzzy ("possible") match. 0.2 = one letter in five.</summary>
    public double FuzzyTolerance { get; set; } = 0.2;

    /// <summary>
    /// Speech the recognizer was not confident about (average word probability below this) is marked "unclear"
    /// and, if <see cref="SaveUnclearClips"/> is on, its audio is saved so a parent can listen to it.
    /// </summary>
    public float LowConfidenceThreshold { get; set; } = 0.7f;
    public bool SaveUnclearClips { get; set; } = true;
    /// <summary>Cap on saved unclear clips, so game sound effects can't fill the disk.</summary>
    public int MaxUnclearClipsPerHour { get; set; } = 40;

    /// <summary>Start monitoring as soon as the app opens.</summary>
    public bool StartMonitoringOnLaunch { get; set; } = false;

    /// <summary>
    /// Desktop app: when started with Windows, run completely hidden (no window, no tray icon) with monitoring on.
    /// Bring it back with <see cref="ShowWindowHotkey"/> or by launching it again.
    /// </summary>
    public bool StartHidden { get; set; } = false;

    /// <summary>Optional 4-8 digit PIN a parent must enter in the desktop app to stop monitoring or change settings.</summary>
    public string ParentPin { get; set; } = "";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static AppConfig Load(string path)
    {
        var config = new AppConfig();
        if (File.Exists(path))
        {
            var json = File.ReadAllText(path);
            config = JsonSerializer.Deserialize<AppConfig>(json, Options) ?? new AppConfig();
        }
        config.ApplyPerformancePreset();
        return config;
    }

    /// <summary>Writes the configuration back (comments in the original file are not preserved).</summary>
    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, Options));
    }

    /// <summary>Makes ModelSize / BeamSize / Threads consistent with the chosen preset.</summary>
    public void ApplyPerformancePreset()
    {
        switch (Performance)
        {
            case PerformancePreset.Light:
                ModelSize = "tiny"; BeamSize = 1; Threads = Math.Max(1, Environment.ProcessorCount / 2);
                break;
            case PerformancePreset.Balanced:
                ModelSize = "base"; BeamSize = 3; Threads = 0;
                break;
            case PerformancePreset.Accurate:
                ModelSize = "small"; BeamSize = 5; Threads = 0;
                break;
        }
    }

    /// <summary>What this machine can comfortably run, from its core count and memory.</summary>
    public static PerformancePreset SuggestedPreset()
    {
        var cores = Environment.ProcessorCount;
        var memoryGb = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1073741824.0;
        if (cores <= 4 || memoryGb < 7) return PerformancePreset.Light;
        if (cores >= 12 && memoryGb >= 15) return PerformancePreset.Accurate;
        return PerformancePreset.Balanced;
    }

    public static string DescribePreset(PerformancePreset p) => p switch
    {
        PerformancePreset.Light => "Light - for older or low-power computers. Tiny model, fastest, less accurate.",
        PerformancePreset.Balanced => "Balanced - recommended for most computers. Base model, good accuracy.",
        PerformancePreset.Accurate => "Accurate - for a fast PC. Small model, fewest mis-hearings, more CPU.",
        PerformancePreset.Custom => "Custom - model, beam size and threads set manually in the settings file.",
        _ => p.ToString(),
    };
}

public sealed class ReportsConfig
{
    /// <summary>Rebuild today's HTML report automatically after each alert and when monitoring stops.</summary>
    public bool AutoGenerate { get; set; } = true;
    /// <summary>Also write a CSV next to each HTML report, for spreadsheets or sharing with professionals.</summary>
    public bool WriteCsv { get; set; } = true;
}

/// <summary>Flags a voice that sounds like an adult (low pitch) talking to the child in calls or games.</summary>
public sealed class VoiceAnalysisConfig
{
    public bool Enabled { get; set; } = true;
    /// <summary>Median pitch below this (Hz) counts as an adult-sounding voice. Children: ~250-400 Hz; adult men: ~85-155 Hz; adult women: ~165-255 Hz.</summary>
    public float AdultPitchHz { get; set; } = 165f;
    /// <summary>At least this much voiced speech is needed before judging a chunk.</summary>
    public double MinVoicedSeconds { get; set; } = 1.0;
    /// <summary>Check voices heard in calls and games (other people talking to the child).</summary>
    public bool FlagOnSystemAudio { get; set; } = true;
    /// <summary>Also check the microphone (an adult in the room, or an older sibling).</summary>
    public bool FlagOnMicrophone { get; set; } = false;
    /// <summary>One adult-voice alert per source within this many minutes (game narrators would otherwise flood the log).</summary>
    public double CooldownMinutes { get; set; } = 10;
}

/// <summary>"Cursing a lot": N flags of one category within M minutes raise an extra, higher-severity alert.</summary>
public sealed class BurstRule
{
    public string Category { get; set; } = "Profanity";
    public int Count { get; set; } = 5;
    public double WindowMinutes { get; set; } = 10;
    [JsonConverter(typeof(JsonStringEnumConverter<Severity>))]
    public Severity Severity { get; set; } = Severity.High;
    public string Label { get; set; } = "Heavy cursing";
}

public sealed class MicrophoneConfig
{
    /// <summary>
    /// Open every input device (built-in mic, headset mic, USB mic...) and follow the one that actually has
    /// sound. The child may be using a headset that is not the Windows default device.
    /// </summary>
    public bool AutoSelectDevice { get; set; } = true;

    /// <summary>Optional: part of a device name to force (e.g. "Headset"). Overrides auto-selection.</summary>
    public string DeviceName { get; set; } = "";

    /// <summary>Switch away from the current device after it has been silent this long while another one has sound.</summary>
    public double SwitchAfterSilentSeconds { get; set; } = 5.0;

    /// <summary>How often to look for input devices that were plugged in or removed.</summary>
    public double RescanSeconds { get; set; } = 5.0;
}

public sealed class SystemAudioConfig
{
    /// <summary>
    /// Process names (without .exe) whose audio is never transcribed. Defaults cover browsers and
    /// media players so YouTube, Netflix, Spotify etc. don't flood the transcript.
    /// </summary>
    public List<string> IgnoreProcesses { get; set; } = new()
    {
        // browsers
        "chrome", "msedge", "msedgewebview2", "firefox", "brave", "opera", "opera_gx", "vivaldi", "arc", "iexplore",
        // media players / streaming apps
        "vlc", "wmplayer", "Video.UI", "Music.UI", "Microsoft.Media.Player", "Spotify", "mpc-hc", "mpc-hc64",
        "PotPlayerMini64", "PotPlayer64", "Netflix", "AppleMusic", "AppleTV", "Amazon Music", "iTunes",
        "PrimeVideo", "Disney+", "Hulu", "Plex", "Plex HTPC", "Kodi", "MediaMonkey", "foobar2000", "AIMP", "Winamp",
        "Movies & TV", "Photos", "ZuneVideo", "ZuneMusic",
    };

    /// <summary>
    /// If non-empty, ONLY these processes are transcribed (e.g. "Discord", "RobloxPlayerBeta", "javaw").
    /// Leave empty to transcribe every process that is not in <see cref="IgnoreProcesses"/>.
    /// </summary>
    public List<string> FocusProcesses { get; set; } = new();

    /// <summary>How often to look for programs that started or stopped playing audio.</summary>
    public double RescanSeconds { get; set; } = 2.0;

    /// <summary>
    /// When per-process capture is unavailable (Windows older than 10 build 20348), fall back to capturing
    /// everything the computer plays, with no filtering.
    /// </summary>
    public bool FallbackToWholeSystemAudio { get; set; } = true;
}
