using System.Threading.Channels;
using GalilunaShield.Alerts;
using GalilunaShield.Audio;
using GalilunaShield.Configuration;
using GalilunaShield.Detection;
using GalilunaShield.Recording;
using GalilunaShield.Reports;
using GalilunaShield.Transcription;

namespace GalilunaShield;

/// <summary>
/// Owns the capture -> transcribe -> detect -> alert pipeline, plus raw recording, voice analysis, burst
/// rules, the program event log and report generation. UI-agnostic: everything is surfaced through events.
/// </summary>
public sealed class MonitorService : IDisposable
{
    public const string AdultVoiceCategory = "Voice: possibly an adult";

    private readonly AppConfig _config;
    private readonly WhisperTranscriber _transcriber;
    private readonly RedFlagDetector _detector;
    private readonly AlertStore _store;
    private readonly ActivityLog _activity;
    private readonly ReportBuilder _reports;
    private readonly RecordingController _recording;
    private DateTimeOffset _lastScreenshot = DateTimeOffset.MinValue;
    private readonly object _gate = new();
    private readonly LinkedList<ContextLine> _recent = new();
    private readonly Queue<DateTimeOffset> _unclearTimes = new();
    private readonly Dictionary<string, DateTimeOffset> _adultVoiceLastAlert = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Queue<DateTimeOffset>> _categoryHits = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _burstLastAlert = new(StringComparer.OrdinalIgnoreCase);
    private readonly Timer _reportDebounce;

    private List<IAudioSource> _sources = new();
    private Channel<AudioChunk>? _queue;
    private CancellationTokenSource? _cts;
    private Task? _worker;

    public bool IsRunning { get; private set; }
    public int AlertCount { get; private set; }
    public MonitorMode Mode { get; private set; }
    public RecordingMode RecordingMode => _recording.Mode;
    public bool IsRecording => _recording.IsActive && _recording.OpenFiles.Count > 0;
    public AlertStore Store => _store;
    public ActivityLog Activity => _activity;
    public ReportBuilder Reports => _reports;
    public RedFlagDetector Detector => _detector;

    public event Action<LogEntry>? Logged;
    public event Action<TranscriptLine>? Transcribed;
    public event Action<AlertRecord>? AlertRaised;
    public event Action<MonitorState>? StateChanged;

    private readonly RetentionCleaner _retention;
    private readonly EvidenceProtector? _protector;
    private readonly Notifications.Notifier _notifier;

    /// <summary>The notifier, so the UI can send a test message.</summary>
    public Notifications.Notifier Notifier => _notifier;

    public MonitorService(AppConfig config, WhisperTranscriber transcriber, RedFlagDetector detector, AlertStore store, ActivityLog activity,
        EvidenceProtector? protector = null)
    {
        _protector = protector;
        _config = config;
        _transcriber = transcriber;
        _detector = detector;
        _store = store;
        _activity = activity;
        _reports = new ReportBuilder(store, activity, config.Reports.WriteCsv);
        _retention = new RetentionCleaner(store, config.Retention, Log);
        _retention.Start();
        _detector.SetMutedWords(config.MutedWords);
        _notifier = new Notifications.Notifier(config.Notifications, Log);
        _recording = new RecordingController(config.Recording, Log);
        _recording.Activated += () => { _activity.Write(ActivityKind.RecordingStarted, "Proof recording started."); RaiseState(); };
        _recording.Deactivated += () => { _activity.Write(ActivityKind.RecordingStopped, "Proof recording stopped."); RaiseState(); };
        _reportDebounce = new Timer(_ => RebuildReports(), null, Timeout.Infinite, Timeout.Infinite);
        Mode = config.Mode;
    }

    public MonitorState State
    {
        get
        {
            lock (_gate)
            {
                return new MonitorState(IsRunning, Mode, _recording.Mode, IsRecording, _sources.Select(s => s.Name).ToList(), AlertCount);
            }
        }
    }

    /// <param name="by">Who or what asked: "hotkey", "tray", "button", "startup", ...; ends up in the event log.</param>
    public void Toggle(string by = "user")
    {
        lock (_gate)
        {
            if (IsRunning) Stop(by); else Start(by);
        }
    }

    /// <summary>Switches listening mode. If monitoring is running it is restarted with the new sources.</summary>
    public void SetMode(MonitorMode mode)
    {
        lock (_gate)
        {
            if (mode == Mode) return;
            Restart(() => Mode = mode, $"Listening mode set to: {Describe(mode)}");
        }
    }

    public void CycleMode()
    {
        SetMode(Mode switch
        {
            MonitorMode.Microphone => MonitorMode.SystemAudio,
            MonitorMode.SystemAudio => MonitorMode.Both,
            _ => MonitorMode.Microphone,
        });
    }

    /// <summary>Switches recording mode. If monitoring is running it is restarted so recorders are (re)attached.</summary>
    public void SetRecordingMode(RecordingMode mode)
    {
        lock (_gate)
        {
            if (mode == _recording.Mode) return;
            Restart(() => _recording.SetMode(mode), $"Recording set to: {DescribeRecording(mode)}");
        }
    }

    public void CycleRecordingMode()
    {
        SetRecordingMode(_recording.Mode switch
        {
            RecordingMode.Off => RecordingMode.Microphone,
            RecordingMode.Microphone => RecordingMode.SystemAudio,
            RecordingMode.SystemAudio => RecordingMode.Both,
            RecordingMode.Both => RecordingMode.Smart,
            _ => RecordingMode.Off,
        });
    }

    private void Restart(Action change, string message)
    {
        var wasRunning = IsRunning;
        if (wasRunning) Stop("settings change");
        change();
        Log(LogLevel.Info, message);
        _activity.Write(ActivityKind.SettingsChanged, message);
        if (wasRunning) Start("settings change"); else RaiseState();
    }

    public static string Describe(MonitorMode mode) => mode switch
    {
        MonitorMode.Microphone => "Microphone only (what the child says)",
        MonitorMode.SystemAudio => "System audio only (calls & games; videos/music skipped)",
        MonitorMode.Both => "Microphone + system audio (calls & games)",
        _ => mode.ToString(),
    };

    public string DescribeRecording(RecordingMode mode) => mode switch
    {
        RecordingMode.Off => "Off (only alert clips are saved)",
        RecordingMode.Microphone => "Record microphone continuously",
        RecordingMode.SystemAudio => "Record system audio (calls & games) continuously",
        RecordingMode.Both => "Record microphone + system audio continuously",
        RecordingMode.Smart => $"Smart: record only after {_config.Recording.SmartTriggerAlerts} red flags in {_config.Recording.SmartTriggerWindowMinutes:0.#} min" +
                               (_config.Recording.SmartTriggerOnCritical ? " or 1 critical flag" : "") +
                               $" (keeps {_config.Recording.PreRollSeconds}s before, records {_config.Recording.SmartRecordMinutes:0.#} min after the last flag)",
        _ => mode.ToString(),
    };

    public void Start(string by = "user")
    {
        lock (_gate)
        {
            if (IsRunning) return;

            var queue = Channel.CreateUnbounded<AudioChunk>(new UnboundedChannelOptions { SingleReader = true });
            _queue = queue;
            _cts = new CancellationTokenSource();
            _sources = new List<IAudioSource>();

            // A source is needed if we listen to it or record it. Sources only needed for recording are not transcribed.
            var listenMic = Mode is MonitorMode.Microphone or MonitorMode.Both;
            var listenSys = Mode is MonitorMode.SystemAudio or MonitorMode.Both;
            var recordMic = _recording.RecordsSource(isMicrophone: true);
            var recordSys = _recording.RecordsSource(isMicrophone: false);
            // Smart mode only records what is being listened to (that's where the flags come from).
            if (_recording.Mode == RecordingMode.Smart) { recordMic &= listenMic; recordSys &= listenSys; }

            if (listenMic || recordMic)
            {
                TryAddSource("microphone", () => new MicrophoneSource(
                    BuildSink("Microphone", listenMic, recordMic, queue), _config.Microphone, _config.SilenceThreshold, Log));
            }
            if (listenSys || recordSys)
            {
                TryAddSource("system audio", () => new SystemAudioCaptureManager(
                    _config.SystemAudio, name => BuildSink(name, listenSys, recordSys, queue), Log));
            }

            if (_sources.Count == 0)
            {
                Log(LogLevel.Error, "No audio sources could be started. Monitoring is NOT active.");
                Teardown();
                RaiseState();
                return;
            }

            var token = _cts.Token;
            _worker = Task.Run(() => ProcessQueueAsync(queue.Reader, token), token);

            foreach (var s in _sources.ToList())
            {
                try
                {
                    s.Start();
                }
                catch (Exception ex)
                {
                    Log(LogLevel.Warning, $"Could not start {s.Name}: {ex.Message}");
                    _sources.Remove(s);
                    s.Dispose();
                }
            }

            if (_sources.Count == 0)
            {
                Log(LogLevel.Error, "No audio sources could be started. Monitoring is NOT active.");
                queue.Writer.TryComplete();
                Teardown();
                RaiseState();
                return;
            }

            IsRunning = true;
            Log(LogLevel.Success, $"MONITORING STARTED  [{Describe(Mode)}]  listening to: {string.Join(", ", _sources.Select(s => s.Name))}");
            Log(LogLevel.Info, $"Recording: {DescribeRecording(_recording.Mode)}");
            _activity.Write(ActivityKind.MonitoringStarted, $"Monitoring started ({by}). Listening: {Describe(Mode)}. Recording: {_recording.Mode}.");
            RaiseState();
        }
    }

    private void Teardown()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _queue = null;
        _worker = null;
    }

    public void Stop(string by = "user")
    {
        Task? worker;
        lock (_gate)
        {
            if (!IsRunning) return;

            // Flip the flag and detach state under the lock, but do NOT wait for the worker here: the worker
            // takes _gate to bump the alert count, so waiting while holding _gate would deadlock (and freeze
            // the app). We stop the sources, complete the queue, then wait outside the lock.
            IsRunning = false;

            foreach (var s in _sources)
            {
                try { s.Stop(); s.Dispose(); }
                catch (Exception ex) { Log(LogLevel.Warning, $"Error stopping {s.Name}: {ex.Message}"); }
            }
            _sources.Clear();

            _queue?.Writer.TryComplete();
            worker = _worker;
        }

        // Let the worker finish transcribing whatever was already captured (bounded), then tear down.
        try { worker?.Wait(TimeSpan.FromSeconds(20)); } catch { /* faulted worker already logged itself */ }

        lock (_gate)
        {
            Teardown();
            Log(LogLevel.Warning, $"MONITORING STOPPED ({by})");
            _activity.Write(ActivityKind.MonitoringStopped, $"Monitoring stopped ({by}).");
            RaiseState();
        }

        if (_config.Reports.AutoGenerate) RebuildReports();
    }

    /// <summary>Builds the sink for one source: speech chunker (if listened to) and/or recorder (if recorded).</summary>
    private IAudioSink BuildSink(string source, bool listen, bool record, Channel<AudioChunk> queue)
    {
        var sinks = new List<IAudioSink>();
        if (listen)
        {
            sinks.Add(new SpeechChunker(source, _config.MinChunkSeconds, _config.MaxChunkSeconds, _config.SilenceSeconds,
                _config.SilenceThreshold, chunk => queue.Writer.TryWrite(chunk)));
        }
        if (record)
        {
            sinks.Add(new SourceRecorder(source, _recording, _store.RecordingsDirectory,
                _config.Recording.PreRollSeconds, _config.Recording.MaxFileMinutes, Log, _protector));
        }
        return sinks.Count == 1 ? sinks[0] : new AudioSinkGroup(sinks.ToArray());
    }

    private void TryAddSource(string name, Func<IAudioSource> factory)
    {
        try
        {
            _sources.Add(factory());
        }
        catch (Exception ex)
        {
            Log(LogLevel.Warning, $"Could not open {name}: {ex.Message}");
        }
    }

    private async Task ProcessQueueAsync(ChannelReader<AudioChunk> reader, CancellationToken ct)
    {
        try
        {
            await foreach (var chunk in reader.ReadAllAsync(ct))
            {
                try
                {
                    await HandleChunkAsync(chunk, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    Log(LogLevel.Error, $"Transcription error ({chunk.Source}): {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task HandleChunkAsync(AudioChunk chunk, CancellationToken ct)
    {
        var result = await _transcriber.TranscribeAsync(chunk.Samples, ct);
        var at = chunk.StartedAt;

        if (!result.HasSpeech)
        {
            // The chunker only emits audio that had speech-like energy, yet the recognizer made out no words.
            // Mumbling, screaming, a language it doesn't know, or heavy game noise. Keep it for a parent to check.
            var note = result.DroppedText.Length > 0 ? $"recognizer only produced: {result.DroppedText}" : "no words recognized";
            Log(LogLevel.Debug, $"[{chunk.Source}] (unclear - {note}, {chunk.Duration.TotalSeconds:F1}s)");
            _store.AppendTranscript(at, chunk.Source, $"<{note}>", unclear: true);
            SaveUnclearIfAllowed(chunk, $"<{note}>", 0f);
            Transcribed?.Invoke(new TranscriptLine(at, chunk.Source, $"<{note}>", 0f, true));
            return;
        }

        var segments = result.Segments;
        var text = string.Join(" ", segments.Select(s => s.Text));

        // Confidence weighted by segment length; low values mean the recognizer was guessing.
        var totalLen = segments.Sum(s => Math.Max(1, s.Text.Length));
        var probability = segments.Sum(s => s.Probability * Math.Max(1, s.Text.Length)) / totalLen;
        var unclear = probability < _config.LowConfidenceThreshold;

        // Who does the voice sound like?
        var pitch = AnalyzeVoice(chunk);
        var voiceNote = pitch is not null && pitch.MedianHz < _config.VoiceAnalysis.AdultPitchHz ? $" [adult-sounding voice, {pitch.Describe()}]" : "";

        _store.AppendTranscript(at, chunk.Source, text + voiceNote, unclear);
        Log(unclear ? LogLevel.Debug : LogLevel.Info, $"[{chunk.Source}] ({probability:P0}{(unclear ? " - unclear" : "")}) {text}{voiceNote}");
        Transcribed?.Invoke(new TranscriptLine(at, chunk.Source, text + voiceNote, probability, unclear));

        if (unclear)
        {
            SaveUnclearIfAllowed(chunk, text, probability);
        }

        // "This app is fine": still transcribed above, but no alerts raised for a muted program.
        if (IsAppMuted(chunk.Source))
        {
            return;
        }

        var matches = new List<RedFlagMatch>(_detector.Detect(text));
        var now = DateTimeOffset.Now;

        // Adult-sounding voice talking to the child: an alert of its own, rate-limited per source.
        if (pitch is not null && ShouldFlagAdultVoice(chunk.Source, pitch, now))
        {
            matches.Add(new RedFlagMatch($"adult-sounding voice ({pitch.Describe()})", AdultVoiceCategory, Severity.High, MatchConfidence.Possible, text));
            _adultVoiceLastAlert[chunk.Source] = now;
        }

        // "Cursing a lot": many flags of one category in a short window.
        foreach (var burst in CheckBursts(matches, now))
        {
            matches.Add(burst);
        }

        IReadOnlyList<ContextLine> context;
        lock (_recent)
        {
            context = _recent.ToList();
            _recent.AddLast(new ContextLine(at, chunk.Source, text));
            while (_recent.Count > _config.ContextSegments) _recent.RemoveFirst();
        }

        if (matches.Count > 0)
        {
            var ordered = matches.OrderByDescending(m => m.Severity).ThenBy(m => m.Confidence).ToList();
            _recording.ReportAlert(now, ordered[0].Severity); // may start Smart proof recording
            var record = _store.WriteAlert(now, chunk, text, probability, ordered, context, _recording.OpenFiles,
                heardWhere: HeardWhere(chunk.Source), captureScreenshot: ScreenshotFor(ordered[0].Severity, now));
            lock (_gate) AlertCount++;

            var summary = string.Join(", ", ordered.Select(m =>
                m.Confidence == MatchConfidence.Exact ? $"\"{m.Word}\" ({m.Category})" : $"\"{m.Word}\"? heard \"{Shorten(m.HeardAs)}\" ({m.Category})"));
            Log(LogLevel.Alert, $"{(record.PossibleOnly ? "POSSIBLE RED FLAG" : $"RED FLAG - {record.Severity.ToString().ToUpperInvariant()}")} [{summary}]  -> {Path.GetFileName(record.DetailsPath!)}");
            _notifier.Notify(record); // email / push to the parent if they're away
            AlertRaised?.Invoke(record);
            RaiseState();

            if (_config.Reports.AutoGenerate) _reportDebounce.Change(TimeSpan.FromSeconds(20), Timeout.InfiniteTimeSpan);
        }
    }

    private static string Shorten(string s) => s.Length > 40 ? s[..37] + "..." : s;

    /// <summary>Re-reads the muted-words and muted-apps lists from config after the parent changes them.</summary>
    public void RefreshMutes() => _detector.SetMutedWords(_config.MutedWords);

    private bool IsAppMuted(string source)
    {
        if (_config.MutedApps.Count == 0) return false;
        var app = AppNameOf(source);
        return app is not null && _config.MutedApps.Contains(app, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The friendly app name inside a "System audio: X" source, or null for the microphone.</summary>
    public static string? AppNameOf(string source)
    {
        const string prefix = "System audio: ";
        return source.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? source[prefix.Length..] : null;
    }

    /// <summary>Turns an internal source name into something a parent reads: where the words were heard.</summary>
    private static string HeardWhere(string source)
    {
        if (source.StartsWith("Microphone", StringComparison.OrdinalIgnoreCase))
        {
            return "the microphone (your child speaking, or someone in the room)";
        }
        const string prefix = "System audio: ";
        if (source.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            var app = source[prefix.Length..];
            return $"{app} (a call or game on the computer)";
        }
        return source;
    }

    /// <summary>Returns a screenshot capturer (raw JPEG bytes) for this alert, or null if off, too soon, or too minor.</summary>
    private Func<byte[]?>? ScreenshotFor(Severity severity, DateTimeOffset now)
    {
        var s = _config.Screenshots;
        if (!s.Enabled || severity < s.MinSeverity) return null;
        if (now - _lastScreenshot < TimeSpan.FromSeconds(s.MinSecondsBetween)) return null;
        if (!OperatingSystem.IsWindows()) return null;
        _lastScreenshot = now;
        return () =>
        {
            var bytes = ScreenCapture.CaptureAllScreens(s.JpegQuality);
            if (bytes is not null)
            {
                Log(LogLevel.Debug, $"Captured all {ScreenCapture.MonitorCount()} monitor(s) for this alert.");
            }
            return bytes;
        };
    }

    private PitchEstimate? AnalyzeVoice(AudioChunk chunk)
    {
        var v = _config.VoiceAnalysis;
        if (!v.Enabled) return null;
        var isMic = chunk.Source.StartsWith("Microphone", StringComparison.OrdinalIgnoreCase);
        if (isMic ? !v.FlagOnMicrophone : !v.FlagOnSystemAudio) return null;
        try
        {
            return VoicePitch.Estimate(chunk.Samples, _config.SilenceThreshold, v.AdultPitchHz);
        }
        catch
        {
            return null;
        }
    }

    private bool ShouldFlagAdultVoice(string source, PitchEstimate pitch, DateTimeOffset now)
    {
        var v = _config.VoiceAnalysis;
        if (pitch.VoicedSeconds < v.MinVoicedSeconds) return false;
        if (pitch.MedianHz >= v.AdultPitchHz || pitch.LowFraction < 0.6) return false;
        return !_adultVoiceLastAlert.TryGetValue(source, out var last) || now - last > TimeSpan.FromMinutes(v.CooldownMinutes);
    }

    private IEnumerable<RedFlagMatch> CheckBursts(IReadOnlyList<RedFlagMatch> matches, DateTimeOffset now)
    {
        foreach (var rule in _config.Bursts)
        {
            if (rule.Count <= 0 || string.IsNullOrWhiteSpace(rule.Category)) continue;
            var hitsNow = matches.Count(m => m.Category.Equals(rule.Category, StringComparison.OrdinalIgnoreCase) && m.Confidence == MatchConfidence.Exact);
            if (hitsNow == 0) continue;

            if (!_categoryHits.TryGetValue(rule.Category, out var q)) _categoryHits[rule.Category] = q = new Queue<DateTimeOffset>();
            for (var i = 0; i < hitsNow; i++) q.Enqueue(now);
            var window = TimeSpan.FromMinutes(rule.WindowMinutes);
            while (q.Count > 0 && now - q.Peek() > window) q.Dequeue();

            if (q.Count >= rule.Count &&
                (!_burstLastAlert.TryGetValue(rule.Category, out var last) || now - last > window))
            {
                _burstLastAlert[rule.Category] = now;
                yield return new RedFlagMatch($"{q.Count} {rule.Category.ToLowerInvariant()} flags in {rule.WindowMinutes:0.#} min", rule.Label, rule.Severity, MatchConfidence.Exact, "");
            }
        }
    }

    /// <summary>Saves an unclear clip unless the hourly cap is reached (game sound effects can produce many).</summary>
    private void SaveUnclearIfAllowed(AudioChunk chunk, string guessedText, float probability)
    {
        if (!_config.SaveUnclearClips) return;

        var now = DateTimeOffset.Now;
        lock (_unclearTimes)
        {
            while (_unclearTimes.Count > 0 && now - _unclearTimes.Peek() > TimeSpan.FromHours(1)) _unclearTimes.Dequeue();
            if (_unclearTimes.Count >= _config.MaxUnclearClipsPerHour)
            {
                if (_unclearTimes.Count == _config.MaxUnclearClipsPerHour)
                {
                    Log(LogLevel.Warning, $"Unclear-clip limit reached ({_config.MaxUnclearClipsPerHour}/hour); further unclear audio is logged but not saved.");
                    _unclearTimes.Enqueue(now); // mark so the message prints once per window
                }
                return;
            }
            _unclearTimes.Enqueue(now);
        }

        try
        {
            _store.WriteUnclear(chunk, guessedText, probability);
        }
        catch (Exception ex)
        {
            Log(LogLevel.Warning, $"Could not save unclear clip: {ex.Message}");
        }
    }

    /// <summary>Regenerates today's/this week's reports and the index. Safe to call any time.</summary>
    public string? RebuildReports()
    {
        try
        {
            var path = _reports.BuildCurrent();
            Log(LogLevel.Debug, $"Report updated: {path}");
            return path;
        }
        catch (Exception ex)
        {
            Log(LogLevel.Warning, $"Could not build report: {ex.Message}");
            return null;
        }
    }

    private void RaiseState() => StateChanged?.Invoke(State);

    private void Log(LogLevel level, string message) => Logged?.Invoke(new LogEntry(DateTimeOffset.Now, level, message));

    /// <summary>Runs the auto-delete sweep now (after the parent changes retention settings). Returns files removed and bytes freed.</summary>
    public (int Files, long Bytes) RunRetentionNow() => _retention.Run();

    public void Dispose()
    {
        Stop("shutdown");
        _reportDebounce.Dispose();
        _recording.Dispose();
        _retention.Dispose();
    }
}
