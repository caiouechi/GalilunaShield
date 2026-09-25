namespace GalilunaShield.Audio;

/// <summary>Receives 16 kHz mono float samples from an <see cref="IAudioSource"/>.</summary>
public interface IAudioSink : IDisposable
{
    void Push(ReadOnlySpan<float> samples);
    /// <summary>The source is stopping; emit anything buffered.</summary>
    void Flush();
}

/// <summary>Fans one source's samples out to several sinks (e.g. the speech chunker and the recorder).</summary>
public sealed class AudioSinkGroup : IAudioSink
{
    private readonly IAudioSink[] _sinks;

    public AudioSinkGroup(params IAudioSink[] sinks) => _sinks = sinks;

    public void Push(ReadOnlySpan<float> samples)
    {
        foreach (var s in _sinks) s.Push(samples);
    }

    public void Flush()
    {
        foreach (var s in _sinks) s.Flush();
    }

    public void Dispose()
    {
        foreach (var s in _sinks) s.Dispose();
    }
}
