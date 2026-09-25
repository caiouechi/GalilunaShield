namespace GalilunaShield.Audio;

/// <summary>Something that produces 16 kHz mono speech chunks while started.</summary>
public interface IAudioSource : IDisposable
{
    string Name { get; }
    void Start();
    void Stop();
}
