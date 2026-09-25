using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GalilunaShield.Audio;
using GalilunaShield.Detection;
using NAudio.Wave;

namespace GalilunaShield.Alerts;

/// <summary>
/// Persists everything a parent may need later: per-alert detail files with audio clips (hashed, so a clip
/// can be shown to be unaltered), a machine-readable alert log used to build reports, daily transcripts,
/// and "unclear" clips the recognizer could not make out.
/// </summary>
public sealed class AlertStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly bool _saveAudio;
    private readonly EvidenceProtector? _protector;
    private readonly object _gate = new();

    public bool EncryptsEvidence => _protector?.Enabled == true;

    public string RootDirectory { get; }
    public string AlertsDirectory { get; }
    public string TranscriptsDirectory { get; }
    public string UnclearDirectory { get; }
    public string RecordingsDirectory { get; }
    public string ReportsDirectory { get; }
    public string ScreenshotsDirectory { get; }
    public string AlertLogPath => Path.Combine(AlertsDirectory, "alerts.jsonl");
    public string AlertStatusPath => Path.Combine(AlertsDirectory, "alerts-status.json");

    public AlertStore(string outputDirectory, bool saveAudio, EvidenceProtector? protector = null)
    {
        _protector = protector;
        RootDirectory = outputDirectory;
        AlertsDirectory = Path.Combine(outputDirectory, "alerts");
        TranscriptsDirectory = Path.Combine(outputDirectory, "transcripts");
        UnclearDirectory = Path.Combine(outputDirectory, "unclear");
        RecordingsDirectory = Path.Combine(outputDirectory, "recordings");
        ReportsDirectory = Path.Combine(outputDirectory, "reports");
        ScreenshotsDirectory = Path.Combine(outputDirectory, "screenshots");
        _saveAudio = saveAudio;
        foreach (var d in new[] { AlertsDirectory, TranscriptsDirectory, UnclearDirectory, RecordingsDirectory, ReportsDirectory, ScreenshotsDirectory })
        {
            Directory.CreateDirectory(d);
        }
    }

    public void AppendTranscript(DateTimeOffset at, string source, string text, bool unclear)
    {
        var path = Path.Combine(TranscriptsDirectory, $"{at:yyyy-MM-dd}.log");
        var marker = unclear ? " (unclear)" : "";
        lock (_gate)
        {
            File.AppendAllText(path, $"[{at:HH:mm:ss}] [{source}]{marker} {text}{Environment.NewLine}", Encoding.UTF8);
        }
    }

    /// <summary>Saves audio the recognizer was unsure about, so a parent can listen to what was really said.</summary>
    public string WriteUnclear(AudioChunk chunk, string guessedText, float probability)
    {
        var baseName = $"{chunk.StartedAt:yyyy-MM-dd_HH-mm-ss-fff}_{Safe(chunk.Source)}";
        var wavPath = WriteWav(Path.Combine(UnclearDirectory, baseName + ".wav"), chunk.Samples);
        var note = $"[{chunk.StartedAt:HH:mm:ss}] [{chunk.Source}] confidence {probability:P0} - recognizer guessed: {guessedText} -> {Path.GetFileName(wavPath)}{Environment.NewLine}";
        lock (_gate)
        {
            File.AppendAllText(Path.Combine(UnclearDirectory, $"{chunk.StartedAt:yyyy-MM-dd}.log"), note, Encoding.UTF8);
        }
        return wavPath;
    }

    /// <param name="heardWhere">Friendly place it was heard, e.g. "Discord (call or game)".</param>
    /// <param name="captureScreenshot">Returns raw JPEG bytes of all monitors, or null. The store writes (and encrypts) them.</param>
    public AlertRecord WriteAlert(DateTimeOffset detectedAt, AudioChunk chunk, string flaggedText, float probability,
        IReadOnlyList<RedFlagMatch> matches, IReadOnlyList<ContextLine> context, IReadOnlyCollection<string> openRecordings,
        string? heardWhere = null, Func<byte[]?>? captureScreenshot = null)
    {
        var primary = matches[0]; // detector orders by severity then exactness
        var baseName = $"{detectedAt:yyyy-MM-dd_HH-mm-ss-fff}_{Safe(primary.Word)}";
        var txtPath = Path.Combine(AlertsDirectory, baseName + ".txt");
        var allPossible = matches.All(m => m.Confidence == MatchConfidence.Possible);

        string? wavPath = null;
        string? sha = null;
        if (_saveAudio)
        {
            wavPath = WriteWav(Path.Combine(AlertsDirectory, baseName + ".wav"), chunk.Samples);
            sha = Sha256(wavPath); // fingerprint of the file as stored (encrypted or not)
        }

        // Screenshot of every monitor at this moment, if the caller supplied one.
        string? shotPath = null;
        var shotBytes = captureScreenshot?.Invoke();
        if (shotBytes is not null)
        {
            var target = Path.Combine(ScreenshotsDirectory, $"{detectedAt:yyyy-MM-dd}", baseName + ".jpg");
            shotPath = _protector?.Enabled == true ? _protector.WriteBytes(target, shotBytes) : WritePlain(target, shotBytes);
        }

        var record = new AlertRecord(
            Id: baseName,
            At: detectedAt,
            Source: chunk.Source,
            Severity: primary.Severity,
            Text: flaggedText,
            Confidence: probability,
            PossibleOnly: allPossible,
            Matches: matches.Select(m => new AlertMatchRecord(m.Word, m.Category, m.Severity, m.Confidence.ToString(), m.HeardAs)).ToList(),
            Context: context,
            DetailsPath: txtPath,
            ClipPath: wavPath,
            ClipSha256: sha,
            Recordings: openRecordings.ToList(),
            HeardWhere: heardWhere,
            ScreenshotPath: shotPath);

        var sb = new StringBuilder();
        sb.AppendLine(allPossible
            ? "=== GALILUNA SHIELD ALERT (POSSIBLE MATCH - please listen to the clip) ==="
            : $"=== GALILUNA SHIELD ALERT - {primary.Severity.ToString().ToUpperInvariant()} ===");
        sb.AppendLine($"Detected at    : {detectedAt:yyyy-MM-dd HH:mm:ss.fff zzz}");
        sb.AppendLine($"Heard on       : {heardWhere ?? chunk.Source}");
        sb.AppendLine($"Audio source   : {chunk.Source}");
        sb.AppendLine($"Audio window   : {chunk.StartedAt:HH:mm:ss.fff} -> {chunk.EndedAt:HH:mm:ss.fff} ({chunk.Duration.TotalSeconds:F1}s)");
        sb.AppendLine($"Recognition    : {probability:P0} confident{(probability < 0.7f ? "  (LOW - words may be wrong)" : "")}");
        sb.AppendLine();
        sb.AppendLine("Red flags matched:");
        foreach (var m in matches)
        {
            var how = m.Confidence == MatchConfidence.Exact ? "exact" : $"possible - heard as \"{m.HeardAs}\"";
            sb.AppendLine($"  - \"{m.Word}\"  [{m.Category} / {m.Severity}]  ({how})");
        }
        sb.AppendLine();
        sb.AppendLine("What was said:");
        sb.AppendLine($"  >>> {flaggedText}");
        sb.AppendLine();
        sb.AppendLine("Context (what was heard just before):");
        if (context.Count == 0) sb.AppendLine("  (none)");
        foreach (var line in context)
        {
            sb.AppendLine($"  [{line.At:HH:mm:ss}] [{line.Source}] {line.Text}");
        }
        if (wavPath is not null)
        {
            sb.AppendLine();
            sb.AppendLine($"Audio clip     : {Path.GetFileName(wavPath)}");
            sb.AppendLine($"Clip SHA-256   : {sha}   (fingerprint proving the clip has not been altered)");
        }
        if (shotPath is not null)
        {
            sb.AppendLine();
            sb.AppendLine($"Screen capture : {Path.GetFileName(shotPath)}  (all monitors, what was on screen at this moment)");
        }
        if (openRecordings.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Full recording in progress (this moment is inside these files):");
            foreach (var f in openRecordings) sb.AppendLine($"  {f}");
        }

        lock (_gate)
        {
            File.WriteAllText(txtPath, sb.ToString(), Encoding.UTF8);
            File.AppendAllText(AlertLogPath, JsonSerializer.Serialize(record, JsonOptions) + Environment.NewLine, Encoding.UTF8);
        }
        return record;
    }

    /// <summary>All alerts ever recorded, oldest first. Corrupt lines are skipped.</summary>
    public IReadOnlyList<AlertRecord> ReadAlerts(DateTimeOffset? from = null, DateTimeOffset? to = null)
    {
        if (!File.Exists(AlertLogPath)) return Array.Empty<AlertRecord>();
        string[] lines;
        lock (_gate) lines = File.ReadAllLines(AlertLogPath);

        var result = new List<AlertRecord>();
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var r = JsonSerializer.Deserialize<AlertRecord>(line, JsonOptions);
                if (r is null) continue;
                if (from is not null && r.At < from) continue;
                if (to is not null && r.At >= to) continue;
                result.Add(r);
            }
            catch (JsonException) { /* skip corrupt line */ }
        }
        return result;
    }

    /// <summary>Lines of the unclear log for a day: (time, source, note, wav file).</summary>
    public IReadOnlyList<(DateTimeOffset At, string Source, string Note, string? WavPath)> ReadUnclear(DateOnly day)
    {
        var path = Path.Combine(UnclearDirectory, $"{day:yyyy-MM-dd}.log");
        if (!File.Exists(path)) return Array.Empty<(DateTimeOffset, string, string, string?)>();
        var result = new List<(DateTimeOffset, string, string, string?)>();
        foreach (var line in File.ReadAllLines(path))
        {
            // [HH:mm:ss] [Source] note -> file.wav
            var m = System.Text.RegularExpressions.Regex.Match(line, @"^\[(\d\d):(\d\d):(\d\d)\] \[([^\]]+)\] (.*?)(?: -> (\S+\.wav))?$");
            if (!m.Success) continue;
            var at = new DateTimeOffset(day.Year, day.Month, day.Day, int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value), 0, DateTimeOffset.Now.Offset);
            var wav = m.Groups[6].Success ? Path.Combine(UnclearDirectory, m.Groups[6].Value) : null;
            result.Add((at, m.Groups[4].Value, m.Groups[5].Value, wav));
        }
        return result;
    }

    public IReadOnlyList<string> ReadTranscript(DateOnly day)
    {
        var path = Path.Combine(TranscriptsDirectory, $"{day:yyyy-MM-dd}.log");
        return File.Exists(path) ? File.ReadAllLines(path) : Array.Empty<string>();
    }

    public IReadOnlyList<FileInfo> ListRecordings(DateOnly day)
    {
        var dir = Path.Combine(RecordingsDirectory, day.ToString("yyyy-MM-dd"));
        return Directory.Exists(dir)
            ? new DirectoryInfo(dir).GetFiles("*.wav").OrderBy(f => f.Name).ToList()
            : Array.Empty<FileInfo>();
    }

    /// <summary>Reads the parent's triage decisions (id -> status). Kept in a sidecar so alerts.jsonl stays append-only.</summary>
    public Dictionary<string, AlertStatusRecord> ReadStatuses()
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(AlertStatusPath)) return new(StringComparer.Ordinal);
                return JsonSerializer.Deserialize<Dictionary<string, AlertStatusRecord>>(File.ReadAllText(AlertStatusPath), JsonOptions)
                       ?? new(StringComparer.Ordinal);
            }
            catch { return new(StringComparer.Ordinal); }
        }
    }

    public void SetAlertStatus(string alertId, AlertStatus status, string? note = null)
    {
        lock (_gate)
        {
            var map = ReadStatuses();
            map[alertId] = new AlertStatusRecord(status, DateTimeOffset.Now, note);
            File.WriteAllText(AlertStatusPath, JsonSerializer.Serialize(map, JsonOptions), Encoding.UTF8);
        }
    }

    public static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string Safe(string s) => string.Concat(s.Where(char.IsLetterOrDigit)).ToLowerInvariant();

    private static string WritePlain(string path, byte[] data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, data);
        return path;
    }

    /// <summary>Reads an evidence file, decrypting it if it is encrypted. For the app to play/view.</summary>
    public byte[] ReadEvidence(string path) => _protector is not null && EvidenceProtector.IsEncrypted(path) ? _protector.ReadBytes(path) : File.ReadAllBytes(path);

    /// <summary>Decrypts an encrypted evidence file to a temporary file and returns its path (caller deletes it); returns the original path if not encrypted.</summary>
    public string? PrepareForViewing(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return path;
        if (_protector is null || !EvidenceProtector.IsEncrypted(path)) return path;
        try { return _protector.DecryptToTemp(path); } catch { return null; }
    }

    /// <summary>Writes a 16 kHz mono WAV, encrypting it when evidence protection is on. Returns the actual path.</summary>
    private string WriteWav(string path, float[] samples)
    {
        if (_protector?.Enabled == true)
        {
            using var ms = new MemoryStream();
            using (var writer = new WaveFileWriter(new NAudio.Utils.IgnoreDisposeStream(ms), new WaveFormat(AudioChunk.SampleRate, 16, 1)))
            {
                writer.WriteSamples(samples, 0, samples.Length);
            }
            return _protector.WriteBytes(path, ms.ToArray());
        }
        using var w = new WaveFileWriter(path, new WaveFormat(AudioChunk.SampleRate, 16, 1));
        w.WriteSamples(samples, 0, samples.Length);
        return path;
    }
}
