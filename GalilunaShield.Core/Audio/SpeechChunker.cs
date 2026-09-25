namespace GalilunaShield.Audio;

/// <summary>
/// Accumulates 16 kHz mono samples and emits a chunk when there is enough speech followed by
/// a pause, or when the maximum length is reached. Pure silence is discarded so Whisper
/// is never asked to transcribe nothing (which is when it tends to hallucinate).
/// </summary>
public sealed class SpeechChunker : IAudioSink
{
    private static readonly TimeSpan IdleFlushAfter = TimeSpan.FromMilliseconds(800);

    private readonly string _source;
    private readonly int _minSamples;
    private readonly int _maxSamples;
    private readonly int _silenceSamples;
    private readonly float _threshold;
    private readonly Action<AudioChunk> _emit;

    private readonly List<float> _buffer = new();
    private readonly object _gate = new();
    private readonly Timer _idleTimer;
    private int _trailingSilence;
    private bool _heardSpeech;
    private DateTimeOffset _chunkStart;
    private DateTimeOffset _lastPush = DateTimeOffset.Now;

    public SpeechChunker(string source, double minSeconds, double maxSeconds, double silenceSeconds,
        float threshold, Action<AudioChunk> emit)
    {
        _source = source;
        _minSamples = (int)(minSeconds * AudioChunk.SampleRate);
        _maxSamples = (int)(maxSeconds * AudioChunk.SampleRate);
        _silenceSamples = (int)(silenceSeconds * AudioChunk.SampleRate);
        _threshold = threshold;
        _emit = emit;
        // Loopback sources deliver no data at all while nothing is playing, so trailing silence is never
        // observed. The timer flushes speech that was simply followed by nothing.
        _idleTimer = new Timer(_ => FlushIfIdle(), null, TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(250));
    }

    public void Push(ReadOnlySpan<float> samples)
    {
        lock (_gate)
        {
            _lastPush = DateTimeOffset.Now;
            PushCore(samples);
        }
    }

    public void Flush()
    {
        lock (_gate) FlushCore();
    }

    private void FlushIfIdle()
    {
        lock (_gate)
        {
            if (_buffer.Count > 0 && _heardSpeech && DateTimeOffset.Now - _lastPush >= IdleFlushAfter)
            {
                FlushCore();
            }
        }
    }

    private void PushCore(ReadOnlySpan<float> samples)
    {
        // Evaluate loudness on small frames (20 ms) so silence detection is responsive.
        const int frame = AudioChunk.SampleRate / 50;
        for (var offset = 0; offset < samples.Length; offset += frame)
        {
            var slice = samples.Slice(offset, Math.Min(frame, samples.Length - offset));
            var rms = Rms(slice);

            if (_buffer.Count == 0)
            {
                if (rms < _threshold)
                {
                    continue; // drop leading silence entirely
                }
                _chunkStart = DateTimeOffset.Now;
            }

            _buffer.AddRange(slice);

            if (rms < _threshold)
            {
                _trailingSilence += slice.Length;
            }
            else
            {
                _trailingSilence = 0;
                _heardSpeech = true;
            }

            var pauseAfterSpeech = _heardSpeech && _trailingSilence >= _silenceSamples && _buffer.Count >= _minSamples;
            if (pauseAfterSpeech || _buffer.Count >= _maxSamples)
            {
                FlushCore();
            }
        }
    }

    private void FlushCore()
    {
        if (_buffer.Count == 0) return;

        if (_heardSpeech && _buffer.Count >= AudioChunk.SampleRate / 2) // ignore blips under 0.5 s
        {
            _emit(new AudioChunk(_source, _chunkStart, DateTimeOffset.Now, _buffer.ToArray()));
        }

        _buffer.Clear();
        _trailingSilence = 0;
        _heardSpeech = false;
    }

    private static float Rms(ReadOnlySpan<float> s)
    {
        if (s.IsEmpty) return 0;
        double sum = 0;
        foreach (var v in s) sum += v * v;
        return (float)Math.Sqrt(sum / s.Length);
    }

    public void Dispose() => _idleTimer.Dispose();
}
