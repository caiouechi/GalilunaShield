namespace GalilunaShield.Audio;

/// <summary>A run of speech captured from one source, 16 kHz mono float PCM.</summary>
public sealed record AudioChunk(string Source, DateTimeOffset StartedAt, DateTimeOffset EndedAt, float[] Samples)
{
    public const int SampleRate = 16000;
    public TimeSpan Duration => TimeSpan.FromSeconds(Samples.Length / (double)SampleRate);
}
