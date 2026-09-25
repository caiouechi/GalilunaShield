using GalilunaShield.Audio;
using Xunit;

namespace GalilunaShield.Tests;

public class SpeechChunkerTests
{
    private static float[] Tone(double seconds, float amplitude)
    {
        var n = (int)(seconds * AudioChunk.SampleRate);
        var s = new float[n];
        for (var i = 0; i < n; i++) s[i] = amplitude * MathF.Sin(2 * MathF.PI * 220 * i / AudioChunk.SampleRate);
        return s;
    }

    private static float[] Silence(double seconds) => new float[(int)(seconds * AudioChunk.SampleRate)];

    [Fact]
    public void Emits_a_chunk_after_speech_followed_by_a_pause()
    {
        var chunks = new List<AudioChunk>();
        using var c = new SpeechChunker("test", minSeconds: 1, maxSeconds: 10, silenceSeconds: 0.5, threshold: 0.01f, chunks.Add);
        c.Push(Tone(2, 0.3f));
        c.Push(Silence(1));
        Assert.Single(chunks);
        Assert.InRange(chunks[0].Duration.TotalSeconds, 2.4, 3.1); // speech + the trailing silence that closed it
        Assert.Equal("test", chunks[0].Source);
    }

    [Fact]
    public void Pure_silence_is_never_emitted()
    {
        var chunks = new List<AudioChunk>();
        using var c = new SpeechChunker("test", 1, 10, 0.5, 0.01f, chunks.Add);
        c.Push(Silence(5));
        c.Flush();
        Assert.Empty(chunks);
    }

    [Fact]
    public void Long_speech_is_split_at_max_length()
    {
        var chunks = new List<AudioChunk>();
        using var c = new SpeechChunker("test", 1, maxSeconds: 3, 0.5, 0.01f, chunks.Add);
        c.Push(Tone(7, 0.3f));
        Assert.Equal(2, chunks.Count);
        Assert.All(chunks, ch => Assert.InRange(ch.Duration.TotalSeconds, 2.9, 3.1));
    }

    [Fact]
    public void Flush_emits_whatever_speech_is_buffered()
    {
        var chunks = new List<AudioChunk>();
        using var c = new SpeechChunker("test", 3, 10, 0.7, 0.01f, chunks.Add);
        c.Push(Tone(1.5, 0.3f));
        Assert.Empty(chunks);
        c.Flush();
        Assert.Single(chunks);
    }

    [Fact]
    public void Blips_shorter_than_half_a_second_are_ignored()
    {
        var chunks = new List<AudioChunk>();
        using var c = new SpeechChunker("test", 1, 10, 0.5, 0.01f, chunks.Add);
        c.Push(Tone(0.2, 0.3f));
        c.Flush();
        Assert.Empty(chunks);
    }
}
