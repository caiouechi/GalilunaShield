using System.Text;
using System.Text.RegularExpressions;

namespace GalilunaShield.Alerts;

public enum ActivityKind
{
    AppStarted,
    AppExited,
    MonitoringStarted,
    MonitoringStopped,
    WindowsShutdown,
    /// <summary>The previous run did not exit cleanly: the process was killed, crashed, or the PC lost power.</summary>
    UnexpectedEnd,
    SettingsChanged,
    RecordingStarted,
    RecordingStopped,
    Note,
}

public sealed record ActivityEvent(DateTimeOffset At, ActivityKind Kind, string Message)
{
    /// <summary>Events a parent should look at: the shield being turned off or dying.</summary>
    public bool IsNoteworthy => Kind is ActivityKind.MonitoringStopped or ActivityKind.AppExited or ActivityKind.UnexpectedEnd or ActivityKind.WindowsShutdown;
}

/// <summary>
/// Program event log: when the app and monitoring started and stopped, who stopped them, Windows shutdowns,
/// and runs that ended without a clean exit. A heartbeat marker is refreshed every minute while the app runs;
/// if it is still there on the next start, the previous run ended unexpectedly and that is logged too.
/// </summary>
public sealed class ActivityLog : IDisposable
{
    private readonly string _dir;
    private readonly string _marker;
    private readonly object _gate = new();
    private readonly Timer _heartbeat;
    private readonly string _appName;

    public event Action<ActivityEvent>? Logged;

    public ActivityLog(string outputDirectory, string appName)
    {
        _dir = Path.Combine(outputDirectory, "events");
        _marker = Path.Combine(_dir, "running.marker");
        _appName = appName;
        Directory.CreateDirectory(_dir);
        _heartbeat = new Timer(_ => Heartbeat(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public string Directory_ => _dir;

    /// <summary>Call once at startup. Detects an unclean previous run and starts the heartbeat.</summary>
    public void Start(string detail = "")
    {
        if (File.Exists(_marker))
        {
            var last = "unknown time";
            try
            {
                var text = File.ReadAllText(_marker).Trim();
                if (DateTimeOffset.TryParse(text, out var t)) last = t.ToString("d MMM yyyy HH:mm");
            }
            catch { }
            Write(ActivityKind.UnexpectedEnd, $"The previous run of {_appName} ended without closing properly (last seen alive {last}). The computer may have been turned off, or the program was killed.");
        }
        Write(ActivityKind.AppStarted, $"{_appName} started{(detail.Length > 0 ? " " + detail : "")}.");
        Heartbeat();
        _heartbeat.Change(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    /// <summary>Call on a clean exit. Removes the heartbeat marker.</summary>
    public void Stop(string reason)
    {
        Write(ActivityKind.AppExited, $"{_appName} exited: {reason}.");
        _heartbeat.Change(Timeout.Infinite, Timeout.Infinite);
        try { File.Delete(_marker); } catch { }
    }

    public void Write(ActivityKind kind, string message)
    {
        var e = new ActivityEvent(DateTimeOffset.Now, kind, message);
        var path = Path.Combine(_dir, $"{e.At:yyyy-MM-dd}.log");
        lock (_gate)
        {
            File.AppendAllText(path, $"[{e.At:HH:mm:ss}] {kind} | {message.Replace('\n', ' ')}{Environment.NewLine}", Encoding.UTF8);
        }
        Logged?.Invoke(e);
    }

    public IReadOnlyList<ActivityEvent> Read(DateOnly day)
    {
        var path = Path.Combine(_dir, $"{day:yyyy-MM-dd}.log");
        if (!File.Exists(path)) return Array.Empty<ActivityEvent>();
        var result = new List<ActivityEvent>();
        string[] lines;
        lock (_gate) lines = File.ReadAllLines(path);
        foreach (var line in lines)
        {
            var m = Regex.Match(line, @"^\[(\d\d):(\d\d):(\d\d)\] (\w+) \| (.*)$");
            if (!m.Success || !Enum.TryParse<ActivityKind>(m.Groups[4].Value, out var kind)) continue;
            var at = new DateTimeOffset(day.Year, day.Month, day.Day, int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value), 0, DateTimeOffset.Now.Offset);
            result.Add(new ActivityEvent(at, kind, m.Groups[5].Value));
        }
        return result;
    }

    private void Heartbeat()
    {
        try
        {
            // Tamper detection: if the events folder was deleted while running, note it and recreate.
            if (!System.IO.Directory.Exists(_dir))
            {
                System.IO.Directory.CreateDirectory(_dir);
                Write(ActivityKind.UnexpectedEnd, "The output/events folder was deleted while the shield was running (possible tampering). It has been recreated.");
            }
            File.WriteAllText(_marker, DateTimeOffset.Now.ToString("O"));
        }
        catch { }
    }

    public void Dispose() => _heartbeat.Dispose();
}
