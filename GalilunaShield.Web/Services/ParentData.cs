using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GalilunaShield.Web.Services;

public enum Severity { Low, Medium, High, Critical }
public enum AlertStatus { New, Reviewed, Dismissed }

public sealed record MatchDto(string Word, string Category, Severity Severity, string Confidence, string HeardAs);
public sealed record ContextDto(DateTimeOffset At, string Source, string Text);

public sealed record AlertDto(
    string Id, DateTimeOffset At, string Source, Severity Severity, string Text, float Confidence,
    bool PossibleOnly, List<MatchDto> Matches, List<ContextDto> Context,
    string? DetailsPath, string? ClipPath, string? ClipSha256, List<string> Recordings,
    string? HeardWhere, string? ScreenshotPath)
{
    public AlertStatus Status { get; set; } = AlertStatus.New;
    public string PrimaryWord => Matches.Count > 0 ? Matches[0].Word : "";
    public bool HasClip => !string.IsNullOrEmpty(ClipPath);
    public bool HasScreenshot => !string.IsNullOrEmpty(ScreenshotPath);
}

public sealed record StatusDto(AlertStatus Status, DateTimeOffset At, string? Note);

/// <summary>Where the child computer keeps its data, read from the shared appsettings.json (comments tolerated).</summary>
public sealed class ParentSettings
{
    public string OutputDirectory { get; set; } = "Galiluna Shield";
    public string LogsDirectory { get; set; } = "";
    public string ParentPin { get; set; } = "";
    public SecuritySection Security { get; set; } = new();
    public WebSection Web { get; set; } = new();

    public sealed class SecuritySection { public bool EncryptEvidence { get; set; } }
    public sealed class WebSection { public bool Enabled { get; set; } public int Port { get; set; } = 8787; }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static string ConfigDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Galiluna Shield");
    public static string ConfigFile => Path.Combine(ConfigDir, "appsettings.json");

    public static ParentSettings Load()
    {
        try
        {
            if (File.Exists(ConfigFile))
                return JsonSerializer.Deserialize<ParentSettings>(File.ReadAllText(ConfigFile), Options) ?? new();
        }
        catch { }
        return new();
    }

    public string ResolveOutput() => Resolve(OutputDirectory);

    /// <summary>Where the activity/event log lives: the logs directory when set, otherwise the output folder.</summary>
    public string ResolveLogs() => string.IsNullOrWhiteSpace(LogsDirectory) ? ResolveOutput() : Resolve(LogsDirectory);

    private static string Resolve(string dir) => Path.IsPathRooted(dir)
        ? dir
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), dir);
}

/// <summary>Reads the local Galiluna Shield data folder and serves it to the dashboard. No engine dependency.</summary>
public sealed class ParentData
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public ParentSettings Settings { get; private set; }
    public string Root { get; private set; }

    public string AlertsDir => Path.Combine(Root, "alerts");
    public string TranscriptsDir => Path.Combine(Root, "transcripts");
    public string ReportsDir => Path.Combine(Root, "reports");
    public string EventsDir => Path.Combine(Settings.ResolveLogs(), "events");
    public string AlertLog => Path.Combine(AlertsDir, "alerts.jsonl");
    public string StatusFile => Path.Combine(AlertsDir, "alerts-status.json");

    public ParentData()
    {
        Settings = ParentSettings.Load();
        Root = Settings.ResolveOutput();
    }

    public void Reload()
    {
        Settings = ParentSettings.Load();
        Root = Settings.ResolveOutput();
    }

    public List<AlertDto> ReadAlerts(int days = 60)
    {
        var result = new List<AlertDto>();
        if (!File.Exists(AlertLog)) return result;
        var cutoff = DateTimeOffset.Now.AddDays(-days);
        var statuses = ReadStatuses();
        foreach (var line in File.ReadLines(AlertLog))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var a = JsonSerializer.Deserialize<AlertDto>(line, Json);
                if (a is null || a.At < cutoff) continue;
                if (statuses.TryGetValue(a.Id, out var s)) a.Status = s.Status;
                result.Add(a);
            }
            catch (JsonException) { }
        }
        result.Reverse(); // newest first
        return result;
    }

    public AlertDto? FindAlert(string id) => ReadAlerts(3650).FirstOrDefault(a => a.Id == id);

    public Dictionary<string, StatusDto> ReadStatuses()
    {
        try
        {
            if (File.Exists(StatusFile))
                return JsonSerializer.Deserialize<Dictionary<string, StatusDto>>(File.ReadAllText(StatusFile), Json) ?? new();
        }
        catch { }
        return new();
    }

    private readonly object _statusGate = new();
    public void SetStatus(string id, AlertStatus status)
    {
        lock (_statusGate)
        {
            var map = ReadStatuses();
            map[id] = new StatusDto(status, DateTimeOffset.Now, null);
            Directory.CreateDirectory(AlertsDir);
            File.WriteAllText(StatusFile, JsonSerializer.Serialize(map, Json));
        }
    }

    public List<string> ReadTranscript(DateOnly day)
    {
        var path = Path.Combine(TranscriptsDir, $"{day:yyyy-MM-dd}.log");
        return File.Exists(path) ? File.ReadAllLines(path).Reverse().ToList() : new();
    }

    public List<FileInfo> Reports()
    {
        if (!Directory.Exists(ReportsDir)) return new();
        return new DirectoryInfo(ReportsDir).GetFiles("*.html")
            .Where(f => !f.Name.Equals("index.html", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => f.LastWriteTime).ToList();
    }

    // ---- evidence reading (with optional DPAPI decryption) ----

    public bool IsEncrypted(string path) => path.EndsWith(".enc", StringComparison.OrdinalIgnoreCase);

    public byte[]? ReadEvidence(string path, string? pin)
    {
        if (!File.Exists(path)) return null;
        var bytes = File.ReadAllBytes(path);
        if (!IsEncrypted(path)) return bytes;
        try
        {
            var entropy = string.IsNullOrEmpty(pin) ? null : SHA256.HashData(Encoding.UTF8.GetBytes("GalilunaShield.v1:" + pin));
            return ProtectedData.Unprotect(bytes, entropy, DataProtectionScope.CurrentUser);
        }
        catch
        {
            return null; // wrong PIN or not this account
        }
    }

    public static string ContentType(string path)
    {
        var p = IsEnc(path) ? path[..^4] : path;
        return Path.GetExtension(p).ToLowerInvariant() switch
        {
            ".wav" => "audio/wav",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            _ => "application/octet-stream",
        };
        static bool IsEnc(string s) => s.EndsWith(".enc", StringComparison.OrdinalIgnoreCase);
    }
}
