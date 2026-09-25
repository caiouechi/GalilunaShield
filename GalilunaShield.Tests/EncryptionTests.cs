using GalilunaShield;
using GalilunaShield.Alerts;
using GalilunaShield.Audio;
using GalilunaShield.Detection;
using Xunit;

namespace GalilunaShield.Tests;

public class EncryptionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gs-enc-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void Protector_round_trips_bytes()
    {
        var p = new EvidenceProtector(enabled: true, parentPin: "1234");
        if (!p.Enabled) return; // non-Windows: skip
        var data = new byte[] { 1, 2, 3, 4, 5, 250, 128, 0 };
        var cipher = p.Protect(data);
        Assert.NotEqual(data, cipher);
        Assert.Equal(data, p.Unprotect(cipher));
    }

    [Fact]
    public void Disabled_protector_writes_plaintext()
    {
        var p = new EvidenceProtector(enabled: false, parentPin: null);
        var path = Path.Combine(_dir, "a.bin");
        var written = p.WriteBytes(path, new byte[] { 9, 9, 9 });
        Assert.Equal(path, written);
        Assert.False(EvidenceProtector.IsEncrypted(written));
    }

    [Fact]
    public void Encrypted_alert_clip_is_not_plaintext_but_decrypts_for_viewing()
    {
        var protector = new EvidenceProtector(enabled: true, parentPin: "4321");
        if (!protector.Enabled) return; // non-Windows: skip
        var store = new AlertStore(_dir, saveAudio: true, protector);
        var chunk = new AudioChunk("Microphone", DateTimeOffset.Now.AddSeconds(-1), DateTimeOffset.Now, new float[AudioChunk.SampleRate]);
        var rec = store.WriteAlert(DateTimeOffset.Now, chunk, "x", 0.9f,
            new List<RedFlagMatch> { new("x", "C", Severity.Low, MatchConfidence.Exact, "x") },
            Array.Empty<ContextLine>(), Array.Empty<string>());

        Assert.NotNull(rec.ClipPath);
        Assert.True(EvidenceProtector.IsEncrypted(rec.ClipPath!));
        // The raw file must not start with the "RIFF" WAV header (i.e. it is encrypted on disk)
        var raw = File.ReadAllBytes(rec.ClipPath!);
        Assert.False(raw.Length >= 4 && raw[0] == (byte)'R' && raw[1] == (byte)'I' && raw[2] == (byte)'F' && raw[3] == (byte)'F');
        // But it decrypts to a valid WAV for viewing
        var temp = store.PrepareForViewing(rec.ClipPath);
        Assert.NotNull(temp);
        var wav = File.ReadAllBytes(temp!);
        Assert.True(wav[0] == (byte)'R' && wav[1] == (byte)'I' && wav[2] == (byte)'F' && wav[3] == (byte)'F');
        try { File.Delete(temp!); } catch { }
    }
}
