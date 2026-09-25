using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace GalilunaShield.Audio;

/// <summary>
/// Captures the sound of ONE program (and its child processes) via Windows process loopback.
/// This is how "system audio" mode hears a Discord call or a game while ignoring a YouTube tab.
/// Requires Windows 10 build 20348 / Windows 11.
/// </summary>
public sealed class ProcessLoopbackSource : IAudioSource
{
    private readonly WasapiRecorder _recorder;
    private readonly IAudioSink _sink;
    private readonly PcmConverter _converter;

    public string Name { get; }
    public uint ProcessId { get; }
    public string ProcessName { get; }

    /// <summary>Raised if Windows tears the capture down (e.g. the program exited).</summary>
    public event Action<ProcessLoopbackSource>? Ended;

    private ProcessLoopbackSource(uint pid, string processName, WasapiRecorder recorder, IAudioSink sink)
    {
        ProcessId = pid;
        ProcessName = processName;
        Name = $"System audio: {processName}";
        _recorder = recorder;
        _sink = sink;
        _converter = new PcmConverter(recorder.WaveFormat);
        _recorder.DataAvailable += OnDataAvailable;
        _recorder.RecordingStopped += (_, e) =>
        {
            if (e.Exception is not null)
            {
                Console.Error.WriteLine($"[{Name}] capture stopped: {e.Exception.Message}");
            }
            Ended?.Invoke(this);
        };
    }

    public static async Task<ProcessLoopbackSource> CreateAsync(uint pid, string processName, IAudioSink sink)
    {
        var recorder = await new WasapiRecorderBuilder()
            .WithProcessLoopback(pid, ProcessLoopbackMode.IncludeTargetProcessTree)
            .WithFormat(WaveFormat.CreateIeeeFloatWaveFormat(AudioChunk.SampleRate, 1)) // ask Windows to mix straight to 16 kHz mono
            .BuildAsync();
        return new ProcessLoopbackSource(pid, processName, recorder, sink);
    }

    public void Start() => _recorder.StartRecording();

    public void Stop()
    {
        if (_recorder.CaptureState != CaptureState.Stopped)
        {
            _recorder.StopRecording();
        }
        _sink.Flush();
    }

    private void OnDataAvailable(ReadOnlySpan<byte> buffer, AudioClientBufferFlags flags, long devicePosition, long qpcPosition)
    {
        if ((flags & AudioClientBufferFlags.Silent) != 0)
        {
            return; // Windows says this buffer is silence; skipping it lets idle-flush finish the current sentence
        }
        _sink.Push(_converter.Convert(buffer));
    }

    public void Dispose()
    {
        _recorder.DataAvailable -= OnDataAvailable;
        _recorder.Dispose();
        _sink.Dispose();
    }
}
