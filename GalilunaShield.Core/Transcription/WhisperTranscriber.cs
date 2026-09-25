using Whisper.net;
using Whisper.net.Ggml;

namespace GalilunaShield.Transcription;

/// <param name="Probability">Average per-word probability, 0..1. Low values mean the recognizer was guessing.</param>
public sealed record TranscriptSegment(string Text, TimeSpan Start, TimeSpan End, float Probability);

/// <param name="Segments">Usable speech segments.</param>
/// <param name="DroppedText">Whatever the recognizer emitted that was not usable speech (e.g. "[BLANK_AUDIO]", "(mumbling)"), for diagnostics.</param>
public sealed record TranscriptionResult(IReadOnlyList<TranscriptSegment> Segments, string DroppedText)
{
    public bool HasSpeech => Segments.Count > 0;
}

/// <summary>Progress of the one-time model download.</summary>
public sealed record DownloadProgress(long BytesReceived, long? TotalBytes)
{
    public double? Fraction => TotalBytes is > 0 ? (double)BytesReceived / TotalBytes.Value : null;
}

/// <summary>Offline speech-to-text using whisper.cpp. Nothing leaves the machine except the one-time model download.</summary>
public sealed class WhisperTranscriber : IDisposable
{
    private readonly WhisperFactory _factory;
    private readonly WhisperProcessor _processor;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public string ModelPath { get; }

    private WhisperTranscriber(WhisperFactory factory, WhisperProcessor processor, string modelPath)
    {
        _factory = factory;
        _processor = processor;
        ModelPath = modelPath;
    }

    public static GgmlType ParseModelSize(string modelSize) => modelSize.ToLowerInvariant() switch
    {
        "tiny" => GgmlType.Tiny,
        "base" => GgmlType.Base,
        "small" => GgmlType.Small,
        "medium" => GgmlType.Medium,
        "large" => GgmlType.LargeV3,
        _ => throw new ArgumentException($"Unknown model size '{modelSize}'. Use tiny, base, small, medium or large."),
    };

    public static string ModelFilePath(string modelDirectory, string modelSize) =>
        Path.Combine(modelDirectory, $"ggml-{ParseModelSize(modelSize).ToString().ToLowerInvariant()}.bin");

    public static bool IsModelDownloaded(string modelDirectory, string modelSize) => File.Exists(ModelFilePath(modelDirectory, modelSize));

    /// <summary>Approximate download size per model, for the UI.</summary>
    public static string ModelSizeDescription(string modelSize) => modelSize.ToLowerInvariant() switch
    {
        "tiny" => "~75 MB, fastest, least accurate",
        "base" => "~150 MB, good balance (recommended)",
        "small" => "~490 MB, more accurate, needs a reasonably fast PC",
        "medium" => "~1.5 GB, very accurate, slow without a strong CPU",
        "large" => "~3 GB, best accuracy, needs a powerful PC",
        _ => "",
    };

    /// <summary>Downloads the model if it is not present yet, reporting progress.</summary>
    public static async Task EnsureModelAsync(string modelDirectory, string modelSize, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        var type = ParseModelSize(modelSize);
        Directory.CreateDirectory(modelDirectory);
        var modelPath = ModelFilePath(modelDirectory, modelSize);
        if (File.Exists(modelPath)) return;

        var tempPath = modelPath + ".partial";
        var name = type.ToString().ToLowerInvariant();
        var url = $"https://huggingface.co/sandrohanea/whisper.net/resolve/v3/classic/ggml-{name}.bin";

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;

            await using (var source = await response.Content.ReadAsStreamAsync(ct))
            await using (var file = File.Create(tempPath))
            {
                var buffer = new byte[1 << 16];
                long received = 0;
                var lastReport = DateTime.UtcNow;
                int read;
                while ((read = await source.ReadAsync(buffer, ct)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), ct);
                    received += read;
                    if (progress is not null && (DateTime.UtcNow - lastReport).TotalMilliseconds > 200)
                    {
                        progress.Report(new DownloadProgress(received, total));
                        lastReport = DateTime.UtcNow;
                    }
                }
                progress?.Report(new DownloadProgress(received, total ?? received));
            }
        }
        catch (HttpRequestException)
        {
            // Mirror layout changed? Fall back to the library's own downloader (no progress information).
            await using var stream = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(type, cancellationToken: ct);
            await using var file = File.Create(tempPath);
            await stream.CopyToAsync(file, ct);
        }

        File.Move(tempPath, modelPath, overwrite: true);
    }

    public static async Task<WhisperTranscriber> CreateAsync(string modelSize, string language, int beamSize, string modelDirectory,
        IProgress<DownloadProgress>? downloadProgress, CancellationToken ct, int threads = 0)
    {
        await EnsureModelAsync(modelDirectory, modelSize, downloadProgress, ct);
        var modelPath = ModelFilePath(modelDirectory, modelSize);

        var factory = WhisperFactory.FromPath(modelPath);
        var builder = factory.CreateBuilder()
            .WithThreads(threads > 0 ? threads : Math.Max(1, Environment.ProcessorCount / 2))
            .WithProbabilities(); // needed for per-segment confidence

        builder = string.Equals(language, "auto", StringComparison.OrdinalIgnoreCase)
            ? builder.WithLanguageDetection()
            : builder.WithLanguage(language);

        if (beamSize > 1)
        {
            // Beam search considers several candidate transcriptions instead of the single most likely word
            // at each step; noticeably fewer mis-hearings on children's speech and game audio.
            builder = builder.WithBeamSearchSamplingStrategy(b => b.WithBeamSize(beamSize));
        }

        return new WhisperTranscriber(factory, builder.Build(), modelPath);
    }

    public async Task<TranscriptionResult> TranscribeAsync(float[] samples16k, CancellationToken ct)
    {
        var segments = new List<TranscriptSegment>();
        var dropped = new List<string>();
        await _lock.WaitAsync(ct);
        try
        {
            await foreach (var seg in _processor.ProcessAsync(samples16k, ct))
            {
                var text = seg.Text.Trim();
                if (text.Length == 0) continue;
                if (IsNoise(text))
                {
                    dropped.Add(text);
                }
                else
                {
                    segments.Add(new TranscriptSegment(text, seg.Start, seg.End, seg.Probability));
                }
            }
        }
        finally
        {
            _lock.Release();
        }
        return new TranscriptionResult(segments, string.Join(" ", dropped));
    }

    /// <summary>Whisper emits bracketed tags like [BLANK_AUDIO], (music) or ♪ on non-speech.</summary>
    private static bool IsNoise(string text) =>
        (text.StartsWith('[') && text.EndsWith(']')) ||
        (text.StartsWith('(') && text.EndsWith(')')) ||
        text.All(c => !char.IsLetterOrDigit(c));

    public void Dispose()
    {
        _processor.Dispose();
        _factory.Dispose();
        _lock.Dispose();
    }
}
