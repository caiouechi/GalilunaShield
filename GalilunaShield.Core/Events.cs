using GalilunaShield.Configuration;

namespace GalilunaShield;

public enum LogLevel
{
    Debug,
    Info,
    Success,
    Warning,
    Error,
    /// <summary>A red flag was detected.</summary>
    Alert,
}

public sealed record LogEntry(DateTimeOffset At, LogLevel Level, string Message);

/// <summary>One transcribed utterance from one source.</summary>
public sealed record TranscriptLine(DateTimeOffset At, string Source, string Text, float Confidence, bool Unclear);

/// <summary>How serious a category is. Drives report colouring and Smart-recording triggers.</summary>
public enum Severity
{
    Low,
    Medium,
    High,
    Critical,
}

public sealed record AlertMatchRecord(string Word, string Category, Severity Severity, string Confidence, string HeardAs);

/// <summary>Parent's triage state for an alert.</summary>
public enum AlertStatus
{
    /// <summary>Not yet looked at.</summary>
    New,
    /// <summary>Seen and acknowledged.</summary>
    Reviewed,
    /// <summary>Marked as not a concern (a false alarm).</summary>
    Dismissed,
}

public sealed record AlertStatusRecord(AlertStatus Status, DateTimeOffset At, string? Note);

public sealed record ContextLine(DateTimeOffset At, string Source, string Text);

/// <summary>Everything known about one detection. Persisted as JSON lines so reports can be rebuilt any time.</summary>
public sealed record AlertRecord(
    string Id,
    DateTimeOffset At,
    string Source,
    Severity Severity,
    string Text,
    float Confidence,
    bool PossibleOnly,
    IReadOnlyList<AlertMatchRecord> Matches,
    IReadOnlyList<ContextLine> Context,
    string? DetailsPath,
    string? ClipPath,
    string? ClipSha256,
    IReadOnlyList<string> Recordings,
    /// <summary>Friendly place it was heard: "Discord (call/game)" or "the microphone".</summary>
    string? HeardWhere = null,
    /// <summary>Screenshot of all monitors captured at the alert, if enabled.</summary>
    string? ScreenshotPath = null);

/// <summary>Snapshot of what the monitor is doing, for UIs.</summary>
public sealed record MonitorState(
    bool IsRunning,
    MonitorMode Mode,
    RecordingMode RecordingMode,
    bool IsRecording,
    IReadOnlyList<string> Sources,
    int AlertsThisSession);
