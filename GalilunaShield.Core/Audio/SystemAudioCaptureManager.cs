using System.Diagnostics;
using GalilunaShield.Configuration;
using NAudio.CoreAudioApi;

namespace GalilunaShield.Audio;

/// <summary>
/// "System audio" mode. Watches which programs have an audio session on the speakers, and opens a
/// per-process capture for each one that passes the Focus/Ignore filters. Browsers and media players
/// are ignored by default, so a Discord call or a game is transcribed but a YouTube video is not.
/// </summary>
public sealed class SystemAudioCaptureManager : IAudioSource
{
    private readonly SystemAudioConfig _config;
    private readonly Func<string, IAudioSink> _sinkFactory;
    private readonly Action<LogLevel, string> _log;
    private readonly HashSet<string> _ignore;
    private readonly HashSet<string> _focus;
    private readonly Dictionary<uint, ProcessLoopbackSource> _active = new();
    private readonly HashSet<uint> _failed = new();
    private readonly SemaphoreSlim _scanLock = new(1, 1);
    private readonly Timer _rescan;
    private WholeSystemLoopbackSource? _fallback;
    private bool _running;

    public string Name => "System audio (calls & games)";

    public IReadOnlyCollection<string> ActiveProcessNames
    {
        get { lock (_active) return _active.Values.Select(s => s.ProcessName).Distinct().ToList(); }
    }

    public SystemAudioCaptureManager(SystemAudioConfig config, Func<string, IAudioSink> sinkFactory, Action<LogLevel, string> log)
    {
        _config = config;
        _sinkFactory = sinkFactory;
        _log = log;
        _ignore = new HashSet<string>(config.IgnoreProcesses.Select(NormalizeName), StringComparer.OrdinalIgnoreCase);
        _focus = new HashSet<string>(config.FocusProcesses.Select(NormalizeName), StringComparer.OrdinalIgnoreCase);
        _rescan = new Timer(_ => _ = ScanAsync(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public static bool IsProcessLoopbackSupported => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348);

    public void Start()
    {
        _running = true;

        if (!IsProcessLoopbackSupported)
        {
            if (!_config.FallbackToWholeSystemAudio)
            {
                throw new PlatformNotSupportedException("per-program audio capture needs Windows 10 build 20348 or Windows 11");
            }
            _log(LogLevel.Warning, "Per-program audio capture is not supported on this Windows version; capturing ALL system audio (no video filtering).");
            _fallback = new WholeSystemLoopbackSource(_sinkFactory("System audio (all)"));
            _fallback.Start();
            return;
        }

        _ = ScanAsync();
        var period = TimeSpan.FromSeconds(Math.Max(0.5, _config.RescanSeconds));
        _rescan.Change(period, period);
    }

    public void Stop()
    {
        _running = false;
        _rescan.Change(Timeout.Infinite, Timeout.Infinite);
        _scanLock.Wait();
        try
        {
            lock (_active)
            {
                foreach (var s in _active.Values)
                {
                    SafeStopAndDispose(s);
                }
                _active.Clear();
            }
            _failed.Clear();
            _fallback?.Stop();
            _fallback?.Dispose();
            _fallback = null;
        }
        finally
        {
            _scanLock.Release();
        }
    }

    private async Task ScanAsync()
    {
        if (!_running || !await _scanLock.WaitAsync(0)) return;
        try
        {
            var sessions = EnumerateSessionProcesses();

            // Programs that started making sound
            foreach (var (pid, name) in sessions)
            {
                bool known;
                lock (_active) known = _active.ContainsKey(pid);
                if (known || _failed.Contains(pid) || !IsAllowed(name)) continue;

                try
                {
                    var friendly = AppIdentity.Friendly(pid, name);
                    var source = await ProcessLoopbackSource.CreateAsync(pid, name, _sinkFactory($"System audio: {friendly}"));
                    source.Ended += OnSourceEnded;
                    lock (_active)
                    {
                        if (!_running) { source.Dispose(); return; }
                        _active[pid] = source;
                    }
                    source.Start();
                    _log(LogLevel.Info, $"Now listening to: {friendly} ({name}, pid {pid})");
                }
                catch (Exception ex)
                {
                    _failed.Add(pid);
                    _log(LogLevel.Warning, $"Could not capture audio of {name} (pid {pid}): {ex.Message}");
                }
            }

            // Programs that exited or dropped their audio session
            List<ProcessLoopbackSource> gone;
            var livePids = sessions.Select(s => s.Pid).ToHashSet();
            lock (_active)
            {
                gone = _active.Values.Where(s => !livePids.Contains(s.ProcessId) || !ProcessExists(s.ProcessId)).ToList();
                foreach (var s in gone) _active.Remove(s.ProcessId);
            }
            foreach (var s in gone)
            {
                SafeStopAndDispose(s);
                _log(LogLevel.Info, $"Stopped listening to: {s.ProcessName} (pid {s.ProcessId})");
            }
            _failed.RemoveWhere(pid => !livePids.Contains(pid));
        }
        catch (Exception ex)
        {
            _log(LogLevel.Warning, $"System audio scan failed: {ex.Message}");
        }
        finally
        {
            _scanLock.Release();
        }
    }

    private void OnSourceEnded(ProcessLoopbackSource source)
    {
        // Windows ended the stream (program exited, device changed). Drop it; the next scan re-adds it if it's back.
        lock (_active)
        {
            if (_active.TryGetValue(source.ProcessId, out var current) && ReferenceEquals(current, source))
            {
                _active.Remove(source.ProcessId);
            }
        }
    }

    private bool IsAllowed(string processName)
    {
        var n = NormalizeName(processName);
        if (_focus.Count > 0) return _focus.Contains(n);
        return !_ignore.Contains(n);
    }

    private static string NormalizeName(string name) =>
        name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;

    private static List<(uint Pid, string Name)> EnumerateSessionProcesses()
    {
        var result = new Dictionary<uint, string>();
        using var enumerator = new MMDeviceEnumerator();
        // Look at EVERY active output device, not just the default: a game may be playing through a headset
        // while Windows' default device is the speakers. Process loopback then captures it regardless of device.
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            using (device)
            {
                var manager = device.AudioSessionManager;
                manager.RefreshSessions();
                var sessions = manager.Sessions;
                for (var i = 0; i < sessions.Count; i++)
                {
                    var session = sessions[i];
                    var pid = session.GetProcessID;
                    // pid 0 = system; our own process shows up too because the alert beep opens an audio session.
                    if (pid == 0 || pid == (uint)Environment.ProcessId || session.IsSystemSoundsSession ||
                        session.State == NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateExpired)
                    {
                        continue;
                    }
                    if (result.ContainsKey(pid)) continue;

                    try
                    {
                        result[pid] = Process.GetProcessById((int)pid).ProcessName;
                    }
                    catch
                    {
                        // process already gone
                    }
                }
            }
        }
        return result.Select(kv => (kv.Key, kv.Value)).ToList();
    }

    private static bool ProcessExists(uint pid)
    {
        try { return !Process.GetProcessById((int)pid).HasExited; }
        catch { return false; }
    }

    private static void SafeStopAndDispose(ProcessLoopbackSource s)
    {
        try { s.Stop(); } catch { /* already stopped */ }
        try { s.Dispose(); } catch { /* best effort */ }
    }

    public void Dispose()
    {
        Stop();
        _rescan.Dispose();
        _scanLock.Dispose();
    }
}
