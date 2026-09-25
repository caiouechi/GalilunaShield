using NAudio.Wave;

namespace GalilunaShield.Audio;

/// <summary>
/// Captures everything the computer plays, unfiltered. Used only as a fallback when per-process
/// capture (<see cref="ProcessLoopbackSource"/>) is not supported by the OS.
/// </summary>
public sealed class WholeSystemLoopbackSource : IAudioSource
{
#pragma warning disable CS0618 // marked obsolete in NAudio 3.1 but still the simplest system-wide loopback
    private readonly WasapiLoopbackCapture _capture = new();
#pragma warning restore CS0618
    private readonly IAudioSink _sink;
    private readonly PcmConverter _converter;

    public string Name => "System audio (all)";

    public WholeSystemLoopbackSource(IAudioSink sink)
    {
        _sink = sink;
        _converter = new PcmConverter(_capture.WaveFormat);
        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += (_, e) =>
        {
            if (e.Exception is not null)
            {
                Console.Error.WriteLine($"[{Name}] capture stopped with error: {e.Exception.Message}");
            }
        };
    }

    public void Start() => _capture.StartRecording();

    public void Stop()
    {
        _capture.StopRecording();
        _sink.Flush();
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        _sink.Push(_converter.Convert(e.Buffer.AsSpan(0, e.BytesRecorded)));
    }

    public void Dispose()
    {
        _capture.DataAvailable -= OnDataAvailable;
        _capture.Dispose();
        _sink.Dispose();
    }
}
