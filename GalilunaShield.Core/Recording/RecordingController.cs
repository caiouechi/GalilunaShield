using GalilunaShield.Configuration;

namespace GalilunaShield.Recording;

/// <summary>
/// Decides whether raw audio is being written to disk right now. Fixed modes are always on for their
/// sources; Smart mode turns on when enough alerts arrive in a short window (or one critical alert) and
/// off again after a quiet period.
/// </summary>
public sealed class RecordingController : IDisposable
{
    private readonly RecordingConfig _config;
    private readonly Action<LogLevel, string> _log;
    private readonly object _gate = new();
    private readonly Queue<DateTimeOffset> _alerts = new();
    private readonly HashSet<string> _openFiles = new();
    private readonly Timer _expiry;
    private DateTimeOffset _smartActiveUntil = DateTimeOffset.MinValue;

    public RecordingMode Mode { get; private set; }

    /// <summary>Fired when Smart mode starts recording, so recorders open their files (with pre-roll) right away.</summary>
    public event Action? Activated;

    /// <summary>Fired when Smart mode switches from recording to not recording, so recorders can close their files.</summary>
    public event Action? Deactivated;

    public RecordingController(RecordingConfig config, Action<LogLevel, string> log)
    {
        _config = config;
        _log = log;
        Mode = config.Mode;
        _expiry = new Timer(_ => CheckExpiry(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    public void SetMode(RecordingMode mode)
    {
        lock (_gate)
        {
            Mode = mode;
            _alerts.Clear();
            _smartActiveUntil = DateTimeOffset.MinValue;
        }
    }

    /// <summary>Whether a source of this kind should have a recorder attached at all.</summary>
    public bool RecordsSource(bool isMicrophone) => Mode switch
    {
        RecordingMode.Off => false,
        RecordingMode.Microphone => isMicrophone,
        RecordingMode.SystemAudio => !isMicrophone,
        _ => true, // Both, Smart
    };

    /// <summary>Whether attached recorders should be writing right now.</summary>
    public bool IsActive => Mode switch
    {
        RecordingMode.Off => false,
        RecordingMode.Smart => DateTimeOffset.Now < _smartActiveUntil,
        _ => true,
    };

    public bool IsSmartTriggered => Mode == RecordingMode.Smart && IsActive;

    public IReadOnlyCollection<string> OpenFiles
    {
        get { lock (_openFiles) return _openFiles.ToList(); }
    }

    /// <summary>Call for every alert. In Smart mode this may start (or extend) proof recording.</summary>
    public void ReportAlert(DateTimeOffset at, Severity severity)
    {
        if (Mode != RecordingMode.Smart) return;

        string? reason = null;
        lock (_gate)
        {
            _alerts.Enqueue(at);
            var window = TimeSpan.FromMinutes(_config.SmartTriggerWindowMinutes);
            while (_alerts.Count > 0 && at - _alerts.Peek() > window) _alerts.Dequeue();

            var wasActive = at < _smartActiveUntil;
            if (wasActive)
            {
                _smartActiveUntil = at.AddMinutes(_config.SmartRecordMinutes); // keep going while flags keep coming
                return;
            }

            if (_config.SmartTriggerOnCritical && severity == Severity.Critical)
            {
                reason = "a CRITICAL red flag";
            }
            else if (_alerts.Count >= _config.SmartTriggerAlerts)
            {
                reason = $"{_alerts.Count} red flags within {_config.SmartTriggerWindowMinutes:0.#} min";
            }
            if (reason is null) return;

            _smartActiveUntil = at.AddMinutes(_config.SmartRecordMinutes);
        }

        _log(LogLevel.Alert, $"PROOF RECORDING STARTED: {reason}. Recording everything (plus the last {_config.PreRollSeconds}s) " +
                             $"until {_config.SmartRecordMinutes:0.#} min pass with no new flag.");
        Activated?.Invoke();
    }

    private void CheckExpiry()
    {
        bool expired;
        lock (_gate)
        {
            expired = Mode == RecordingMode.Smart && _smartActiveUntil != DateTimeOffset.MinValue && DateTimeOffset.Now >= _smartActiveUntil;
            if (expired)
            {
                _smartActiveUntil = DateTimeOffset.MinValue;
                _alerts.Clear();
            }
        }
        if (expired)
        {
            _log(LogLevel.Info, "Proof recording stopped (quiet period elapsed).");
            Deactivated?.Invoke();
        }
    }

    internal void FileOpened(string path)
    {
        lock (_openFiles) _openFiles.Add(path);
    }

    internal void FileClosed(string path)
    {
        lock (_openFiles) _openFiles.Remove(path);
    }

    public void Dispose() => _expiry.Dispose();
}
