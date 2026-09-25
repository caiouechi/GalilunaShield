using System.Text.Json.Serialization;

namespace GalilunaShield.Configuration;

/// <summary>Whether raw audio is saved to disk, and of what.</summary>
public enum RecordingMode
{
    /// <summary>Never save raw audio (alerts still save their short clip).</summary>
    Off,
    /// <summary>Continuously record the microphone.</summary>
    Microphone,
    /// <summary>Continuously record system audio (the same filtered programs the listener hears).</summary>
    SystemAudio,
    /// <summary>Continuously record everything that is being listened to.</summary>
    Both,
    /// <summary>
    /// Record nothing until several red flags land within a short window (or one critical one); then record
    /// everything being listened to, including the seconds just before the trigger, until things stay calm.
    /// </summary>
    Smart,
}

public sealed class RecordingConfig
{
    [JsonConverter(typeof(JsonStringEnumConverter<RecordingMode>))]
    public RecordingMode Mode { get; set; } = RecordingMode.Smart;

    /// <summary>Smart mode: this many alerts...</summary>
    public int SmartTriggerAlerts { get; set; } = 3;
    /// <summary>...within this many minutes starts recording.</summary>
    public double SmartTriggerWindowMinutes { get; set; } = 5;
    /// <summary>Smart mode: a single alert of Critical severity starts recording immediately.</summary>
    public bool SmartTriggerOnCritical { get; set; } = true;
    /// <summary>
    /// Smart mode: keep recording until this many minutes pass with NO new red flag. The parent's "calm-down
    /// margin" - how long after things settle the shield keeps capturing, in case it flares up again.
    /// </summary>
    public double SmartRecordMinutes { get; set; } = 15;
    /// <summary>Smart mode: seconds of audio from before the trigger to include in the recording.</summary>
    public int PreRollSeconds { get; set; } = 60;
    /// <summary>Recordings are split into files of at most this many minutes.</summary>
    public int MaxFileMinutes { get; set; } = 30;
}

/// <summary>Automatic clean-up of saved audio, so recordings do not fill the disk over time.</summary>
public sealed class RetentionConfig
{
    /// <summary>Delete saved audio older than <see cref="Days"/>. Off by default: deleting is irreversible, and evidence should not vanish by surprise.</summary>
    public bool Enabled { get; set; } = false;
    /// <summary>How many days of audio to keep.</summary>
    public int Days { get; set; } = 30;
    /// <summary>Also delete the long proof recordings (not only the short alert/unclear clips).</summary>
    public bool IncludeRecordings { get; set; } = true;
    /// <summary>Also delete screenshots taken at alerts.</summary>
    public bool IncludeScreenshots { get; set; } = true;
    /// <summary>Keep the alert detail text, transcripts and reports even when their audio is deleted, so the written record survives.</summary>
    public bool KeepTextRecord { get; set; } = true;
}

/// <summary>Encryption of the sensitive saved files.</summary>
public sealed class SecurityConfig
{
    /// <summary>
    /// Encrypt audio clips, recordings and screenshots at rest so they can only be opened inside Galiluna
    /// Shield on this Windows account. Strengthened when a parent PIN is set. When on, the HTML reports link
    /// to the app to open media instead of embedding it.
    /// </summary>
    public bool EncryptEvidence { get; set; } = false;
}

/// <summary>A picture of every monitor, captured at the moment of a red flag.</summary>
public sealed class ScreenshotConfig
{
    /// <summary>Save a screenshot of all monitors when a red flag is detected, so a parent sees what was on screen.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Only for these severities and above. Low = every flag; High = only serious ones (saves disk).</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<Severity>))]
    public Severity MinSeverity { get; set; } = Severity.Medium;
    /// <summary>At most one screenshot per this many seconds, so a burst of flags does not fill the disk.</summary>
    public double MinSecondsBetween { get; set; } = 15;
    /// <summary>JPEG quality 1-100. Lower is smaller on disk.</summary>
    public int JpegQuality { get; set; } = 80;
}
