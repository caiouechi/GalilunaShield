using GalilunaShield.Alerts;
using GalilunaShield.Configuration;

namespace GalilunaShield;

/// <summary>
/// Deletes saved audio (and optionally screenshots) older than the retention window, so long-running
/// installs do not fill the disk. Runs once at startup and then daily. Text records - alert details,
/// transcripts, reports, the event log - are kept when <see cref="RetentionConfig.KeepTextRecord"/> is on,
/// so the written history survives even after the audio is gone.
/// </summary>
public sealed class RetentionCleaner : IDisposable
{
    private readonly AlertStore _store;
    private readonly RetentionConfig _config;
    private readonly Action<LogLevel, string> _log;
    private readonly Timer _timer;

    public RetentionCleaner(AlertStore store, RetentionConfig config, Action<LogLevel, string> log)
    {
        _store = store;
        _config = config;
        _log = log;
        _timer = new Timer(_ => Run(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start()
    {
        Run();
        _timer.Change(TimeSpan.FromHours(6), TimeSpan.FromHours(6));
    }

    public (int Files, long Bytes) Run()
    {
        if (!_config.Enabled || _config.Days <= 0) return (0, 0);
        var cutoff = DateTime.Now.AddDays(-_config.Days);
        var files = 0;
        long bytes = 0;

        void Sweep(string dir, string[] patterns)
        {
            if (!Directory.Exists(dir)) return;
            foreach (var pattern in patterns)
            {
                foreach (var file in Directory.EnumerateFiles(dir, pattern, SearchOption.AllDirectories))
                {
                    try
                    {
                        var info = new FileInfo(file);
                        if (info.LastWriteTime >= cutoff) continue;
                        var size = info.Length;
                        info.Delete();
                        files++;
                        bytes += size;
                    }
                    catch { /* locked or already gone */ }
                }
            }
        }

        // Audio always. Text (.txt details) only when the parent chose not to keep the written record.
        var audio = _config.KeepTextRecord ? new[] { "*.wav" } : new[] { "*.wav", "*.txt" };
        Sweep(_store.AlertsDirectory, audio);
        Sweep(_store.UnclearDirectory, new[] { "*.wav" });
        if (_config.IncludeRecordings) Sweep(_store.RecordingsDirectory, new[] { "*.wav" });
        if (_config.IncludeScreenshots) Sweep(_store.ScreenshotsDirectory, new[] { "*.jpg", "*.jpeg", "*.png" });

        // Remove now-empty dated sub-folders under recordings/screenshots.
        foreach (var root in new[] { _store.RecordingsDirectory, _store.ScreenshotsDirectory })
        {
            if (!Directory.Exists(root)) continue;
            foreach (var sub in Directory.EnumerateDirectories(root))
            {
                try { if (!Directory.EnumerateFileSystemEntries(sub).Any()) Directory.Delete(sub); } catch { }
            }
        }

        if (files > 0)
        {
            _log(LogLevel.Info, $"Auto-delete removed {files} file(s) older than {_config.Days} days ({bytes / 1048576.0:F0} MB freed).");
        }
        return (files, bytes);
    }

    public void Dispose() => _timer.Dispose();
}
