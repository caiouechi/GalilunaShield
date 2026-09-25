namespace GalilunaShield.Audio;

/// <summary>Result of a pitch scan over one speech chunk.</summary>
/// <param name="MedianHz">Median fundamental frequency of the voiced frames.</param>
/// <param name="VoicedSeconds">How much of the chunk was voiced speech.</param>
/// <param name="LowFraction">Share of voiced frames below the adult threshold that was passed in.</param>
public sealed record PitchEstimate(float MedianHz, double VoicedSeconds, double LowFraction)
{
    public string Describe() => $"~{MedianHz:0} Hz over {VoicedSeconds:0.0}s of speech";
}

/// <summary>
/// Cheap fundamental-frequency estimator (normalised autocorrelation per 40 ms frame). It cannot tell who
/// is speaking, but a median pitch well below ~165 Hz is characteristic of adult (especially male) voices,
/// while children's voices sit around 250-400 Hz. Used to warn a parent when an adult-sounding voice talks
/// to the child in a game or call.
/// </summary>
public static class VoicePitch
{
    private const int SampleRate = AudioChunk.SampleRate;
    private const int FrameSize = SampleRate * 40 / 1000;   // 40 ms
    private const int Hop = SampleRate * 20 / 1000;         // 20 ms
    private const float MinHz = 60f, MaxHz = 500f;
    private const float VoicingThreshold = 0.55f;           // autocorrelation peak needed to call a frame voiced

    public static PitchEstimate? Estimate(ReadOnlySpan<float> samples, float energyThreshold, float lowHz)
    {
        if (samples.Length < FrameSize) return null;

        var minLag = (int)(SampleRate / MaxHz);
        var maxLag = (int)(SampleRate / MinHz);
        var pitches = new List<float>();

        for (var start = 0; start + FrameSize <= samples.Length; start += Hop)
        {
            var frame = samples.Slice(start, FrameSize);

            // Skip quiet frames
            double energy = 0;
            foreach (var v in frame) energy += v * v;
            var rms = Math.Sqrt(energy / FrameSize);
            if (rms < energyThreshold * 2) continue;

            // Remove DC
            float mean = 0;
            foreach (var v in frame) mean += v;
            mean /= FrameSize;

            var lastLag = Math.Min(maxLag, FrameSize / 2 - 1);
            var corr = new float[lastLag + 1];
            var bestLag = 0;
            var bestCorr = 0f;
            for (var lag = minLag; lag <= lastLag; lag++)
            {
                double num = 0, den1 = 0, den2 = 0;
                for (var i = 0; i + lag < FrameSize; i++)
                {
                    var a = frame[i] - mean;
                    var b = frame[i + lag] - mean;
                    num += a * b; den1 += a * a; den2 += b * b;
                }
                corr[lag] = den1 > 0 && den2 > 0 ? (float)(num / Math.Sqrt(den1 * den2)) : 0f;
                if (corr[lag] > bestCorr) { bestCorr = corr[lag]; bestLag = lag; }
            }

            if (bestCorr >= VoicingThreshold && bestLag > 0)
            {
                // A periodic signal correlates at the true period AND its multiples, so the strongest peak may
                // sit at 2x or 3x the period (an octave/twelfth too low). Take the SHORTEST lag that still
                // correlates nearly as well as the peak: that is the fundamental.
                var period = bestLag;
                for (var lag = minLag; lag < bestLag; lag++)
                {
                    if (corr[lag] >= bestCorr * 0.85f) { period = lag; break; }
                }
                pitches.Add((float)SampleRate / period);
            }
        }

        if (pitches.Count == 0) return null;
        pitches.Sort();
        var median = pitches[pitches.Count / 2];
        var voicedSeconds = pitches.Count * Hop / (double)SampleRate;
        var low = pitches.Count(p => p < lowHz) / (double)pitches.Count;
        return new PitchEstimate(median, voicedSeconds, low);
    }
}
