using GalilunaShield.Audio;
using NAudio.Wave;
using Xunit;

namespace GalilunaShield.Tests;

public class PcmConverterTests
{
    [Fact]
    public void Pcm16_mono_16k_passes_through()
    {
        var conv = new PcmConverter(new WaveFormat(16000, 16, 1));
        var bytes = new byte[] { 0x00, 0x40, 0x00, 0xC0 }; // +16384, -16384
        var f = conv.Convert(bytes);
        Assert.Equal(2, f.Length);
        Assert.Equal(0.5f, f[0], 3);
        Assert.Equal(-0.5f, f[1], 3);
    }

    [Fact]
    public void Float_stereo_48k_is_downmixed_and_resampled_to_16k()
    {
        var conv = new PcmConverter(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2));
        var frames = 48000; // one second
        var bytes = new byte[frames * 2 * 4];
        for (var i = 0; i < frames; i++)
        {
            BitConverter.GetBytes(0.8f).CopyTo(bytes, (i * 2) * 4);     // left
            BitConverter.GetBytes(0.2f).CopyTo(bytes, (i * 2 + 1) * 4); // right
        }
        var f = conv.Convert(bytes);
        Assert.InRange(f.Length, 15990, 16010);
        Assert.All(f, v => Assert.Equal(0.5f, v, 2)); // (0.8 + 0.2) / 2
    }

    [Fact]
    public void Resampling_is_continuous_across_buffers()
    {
        var conv = new PcmConverter(WaveFormat.CreateIeeeFloatWaveFormat(44100, 1));
        var total = 0;
        for (var b = 0; b < 10; b++)
        {
            var bytes = new byte[4410 * 4]; // 0.1 s per buffer
            total += conv.Convert(bytes).Length;
        }
        Assert.InRange(total, 15990, 16010); // 1 s at 16 kHz, no drift
    }
}
