using System.Runtime.InteropServices;
using GalilunaShield.Configuration;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace GalilunaShield.Audio;

/// <summary>
/// Captures the microphone: what the child says. Opens every active input device and forwards the one that
/// actually has sound, because the Windows default device is often not the headset the child is using.
/// Devices that are plugged in or removed while running are picked up automatically.
/// </summary>
public sealed class MicrophoneSource : IAudioSource
{
    private sealed class Device : IDisposable
    {
        public required string Id { get; init; }
        public required string FriendlyName { get; init; }
        public required WasapiRecorder Recorder { get; init; }
        public required PcmConverter Converter { get; init; }
        public DateTimeOffset LastSound { get; set; } = DateTimeOffset.MinValue;
        public float RecentLevel { get; set; } // smoothed RMS

        public void Dispose()
        {
            try { if (Recorder.CaptureState != CaptureState.Stopped) Recorder.StopRecording(); } catch { }
            Recorder.Dispose();
        }
    }

    private readonly IAudioSink _sink;
    private readonly MicrophoneConfig _config;
    private readonly float _silenceThreshold;
    private readonly Action<LogLevel, string> _log;
    private readonly Dictionary<string, Device> _devices = new();
    private readonly object _gate = new();
    private readonly Timer _rescan;
    private Device? _selected;
    private bool _running;

    public string Name => "Microphone";

    public MicrophoneSource(IAudioSink sink, MicrophoneConfig config, float silenceThreshold, Action<LogLevel, string> log)
    {
        _sink = sink;
        _config = config;
        _silenceThreshold = silenceThreshold;
        _log = log;
        _rescan = new Timer(_ => Rescan(), null, Timeout.Infinite, Timeout.Infinite);

        if (!Rescan(initial: true))
        {
            throw new InvalidOperationException("no microphone detected");
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            _running = true;
            foreach (var d in _devices.Values) StartDevice(d);
            var period = TimeSpan.FromSeconds(Math.Max(1, _config.RescanSeconds));
            _rescan.Change(period, period);
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _running = false;
            _rescan.Change(Timeout.Infinite, Timeout.Infinite);
            foreach (var d in _devices.Values)
            {
                try { d.Recorder.StopRecording(); } catch { /* already stopped */ }
            }
        }
        _sink.Flush();
    }

    private static void StartDevice(Device d)
    {
        if (d.Recorder.CaptureState == CaptureState.Stopped)
        {
            d.Recorder.StartRecording();
        }
    }

    /// <summary>Opens newly appeared input devices and drops removed ones. Returns whether any device is open.</summary>
    private bool Rescan(bool initial = false)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var wanted = new Dictionary<string, MMDevice>();
            foreach (var dev in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                if (!string.IsNullOrWhiteSpace(_config.DeviceName) &&
                    !dev.FriendlyName.Contains(_config.DeviceName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                wanted[dev.ID] = dev;
            }

            if (!_config.AutoSelectDevice && string.IsNullOrWhiteSpace(_config.DeviceName) && wanted.Count > 1)
            {
                // Classic behaviour: only the Windows default communications device.
                var def = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
                wanted = wanted.Where(kv => kv.Key == def.ID).ToDictionary(kv => kv.Key, kv => kv.Value);
            }

            lock (_gate)
            {
                foreach (var id in _devices.Keys.Where(id => !wanted.ContainsKey(id)).ToList())
                {
                    var gone = _devices[id];
                    _devices.Remove(id);
                    gone.Dispose();
                    _log(LogLevel.Info, $"Microphone removed: {gone.FriendlyName}");
                    if (ReferenceEquals(_selected, gone)) _selected = null;
                }

                foreach (var (id, mm) in wanted)
                {
                    if (_devices.ContainsKey(id)) continue;
                    try
                    {
                        var d = Open(mm);
                        _devices[id] = d;
                        if (_running) StartDevice(d);
                        if (!initial) _log(LogLevel.Info, $"Microphone found: {d.FriendlyName}");
                    }
                    catch (Exception ex)
                    {
                        _log(LogLevel.Warning, $"Could not open microphone \"{mm.FriendlyName}\": {ex.Message}");
                    }
                }

                if (_selected is null && _devices.Count > 0)
                {
                    // Prefer the Windows default until we hear sound somewhere else.
                    string? defId = null;
                    try { defId = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications).ID; } catch { }
                    _selected = (defId is not null && _devices.TryGetValue(defId, out var def)) ? def : _devices.Values.First();
                    _log(LogLevel.Info, $"Microphone in use: {_selected.FriendlyName}" +
                         (_devices.Count > 1 ? $"  (watching {_devices.Count} input devices, will follow the one with sound)" : ""));
                }

                return _devices.Count > 0;
            }
        }
        catch (Exception ex)
        {
            _log(LogLevel.Warning, $"Microphone scan failed: {ex.Message}");
            lock (_gate) return _devices.Count > 0;
        }
    }

    private Device Open(MMDevice mm)
    {
        WasapiRecorder recorder;
        try
        {
            recorder = new WasapiRecorderBuilder().WithDevice(mm).WithSharedMode()
                .WithFormat(WaveFormat.CreateIeeeFloatWaveFormat(AudioChunk.SampleRate, 1)).Build();
        }
        catch
        {
            recorder = new WasapiRecorderBuilder().WithDevice(mm).WithSharedMode().Build(); // device format, converted below
        }

        var device = new Device
        {
            Id = mm.ID,
            FriendlyName = mm.FriendlyName,
            Recorder = recorder,
            Converter = new PcmConverter(recorder.WaveFormat),
        };
        recorder.DataAvailable += (ReadOnlySpan<byte> buffer, AudioClientBufferFlags flags, long _, long _) => OnData(device, buffer, flags);
        recorder.RecordingStopped += (_, e) =>
        {
            if (e.Exception is not null) _log(LogLevel.Warning, $"Microphone \"{device.FriendlyName}\" stopped: {e.Exception.Message}");
        };
        return device;
    }

    private void OnData(Device device, ReadOnlySpan<byte> buffer, AudioClientBufferFlags flags)
    {
        if (buffer.IsEmpty) return;
        var samples = (flags & AudioClientBufferFlags.Silent) != 0 ? new float[buffer.Length / 4] : device.Converter.Convert(buffer);
        var rms = Rms(samples);
        var now = DateTimeOffset.Now;

        bool forward;
        lock (_gate)
        {
            device.RecentLevel = device.RecentLevel * 0.8f + rms * 0.2f;
            if (rms >= _silenceThreshold) device.LastSound = now;

            MaybeSwitch(now);
            forward = ReferenceEquals(_selected, device);
        }

        if (forward) _sink.Push(samples);
    }

    /// <summary>Follow the sound: if the selected device has been quiet a while and another one is live, switch.</summary>
    private void MaybeSwitch(DateTimeOffset now)
    {
        if (_devices.Count < 2 || _selected is null) return;
        var quietFor = TimeSpan.FromSeconds(_config.SwitchAfterSilentSeconds);
        if (now - _selected.LastSound < quietFor) return;

        var candidate = _devices.Values
            .Where(d => !ReferenceEquals(d, _selected) && now - d.LastSound < TimeSpan.FromSeconds(2))
            .OrderByDescending(d => d.RecentLevel)
            .FirstOrDefault();
        if (candidate is null) return;

        _log(LogLevel.Info, $"Microphone switched: \"{_selected.FriendlyName}\" is silent, following \"{candidate.FriendlyName}\"");
        _selected = candidate;
    }

    private static float Rms(ReadOnlySpan<float> s)
    {
        if (s.IsEmpty) return 0;
        double sum = 0;
        foreach (var v in s) sum += v * v;
        return (float)Math.Sqrt(sum / s.Length);
    }

    public void Dispose()
    {
        _rescan.Dispose();
        lock (_gate)
        {
            foreach (var d in _devices.Values) d.Dispose();
            _devices.Clear();
        }
        _sink.Dispose();
    }
}
