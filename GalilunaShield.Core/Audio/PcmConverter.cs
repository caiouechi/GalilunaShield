using NAudio.Wave;

namespace GalilunaShield.Audio;

/// <summary>
/// Converts raw device buffers of any common PCM/float layout into 16 kHz mono float samples.
/// Stateful: carries the resampler position across buffers, so use one instance per stream.
/// </summary>
public sealed class PcmConverter
{
    private readonly WaveFormat _format;
    private readonly bool _isFloat;
    private double _resamplePosition;
    private float _lastSample;

    public PcmConverter(WaveFormat deviceFormat)
    {
        // WASAPI reports an "extensible" format; collapse it so Encoding tells us PCM vs IEEE float directly.
        _format = deviceFormat is WaveFormatExtensible ext ? ext.ToStandardWaveFormat() : deviceFormat;
        _isFloat = _format.Encoding == WaveFormatEncoding.IeeeFloat;
    }

    public float[] Convert(ReadOnlySpan<byte> buffer)
    {
        if (buffer.IsEmpty) return Array.Empty<float>();
        var mono = ToMonoFloat(buffer);
        return Resample(mono, _format.SampleRate, AudioChunk.SampleRate);
    }

    private float[] ToMonoFloat(ReadOnlySpan<byte> buffer)
    {
        var channels = _format.Channels;
        var bytesPerSample = _format.BitsPerSample / 8;
        var frames = buffer.Length / (bytesPerSample * channels);
        var result = new float[frames];

        for (var f = 0; f < frames; f++)
        {
            float sum = 0;
            for (var c = 0; c < channels; c++)
            {
                var idx = (f * channels + c) * bytesPerSample;
                sum += bytesPerSample switch
                {
                    4 when _isFloat => BitConverter.ToSingle(buffer.Slice(idx, 4)),
                    4 => BitConverter.ToInt32(buffer.Slice(idx, 4)) / 2147483648f,
                    3 => ((buffer[idx + 2] << 24) | (buffer[idx + 1] << 16) | (buffer[idx] << 8)) / 2147483648f,
                    2 => BitConverter.ToInt16(buffer.Slice(idx, 2)) / 32768f,
                    1 => (buffer[idx] - 128) / 128f,
                    _ => throw new NotSupportedException($"Unsupported audio format: {_format}"),
                };
            }
            result[f] = sum / channels;
        }

        return result;
    }

    /// <summary>Linear-interpolation resampler. Good enough for speech recognition.</summary>
    private float[] Resample(float[] input, int fromRate, int toRate)
    {
        if (fromRate == toRate || input.Length == 0)
        {
            return input;
        }

        var ratio = (double)fromRate / toRate;
        var output = new List<float>((int)(input.Length / ratio) + 2);

        var pos = _resamplePosition; // fractional read index carried over from the previous buffer
        while (pos < input.Length)
        {
            var i0 = (int)Math.Floor(pos);
            var frac = (float)(pos - i0);
            var s0 = i0 < 0 ? _lastSample : input[i0];
            var s1 = i0 + 1 < input.Length ? input[i0 + 1] : input[^1];
            output.Add(s0 + (s1 - s0) * frac);
            pos += ratio;
        }

        _resamplePosition = pos - input.Length;
        _lastSample = input[^1];
        return output.ToArray();
    }
}
