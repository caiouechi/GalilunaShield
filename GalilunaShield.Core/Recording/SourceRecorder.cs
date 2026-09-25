using GalilunaShield.Audio;
using NAudio.Wave;

namespace GalilunaShield.Recording;

/// <summary>
/// Writes one source's audio to WAV files while the <see cref="RecordingController"/> says recording is active.
/// While inactive (Smart mode waiting), it keeps a rolling pre-roll buffer that is written at the head of the
/// file when recording starts, so the proof includes what led up to the trigger.
/// </summary>
public sealed class SourceRecorder : IAudioSink
{
    private readonly string _sourceName;
    private readonly RecordingController _controller;
    private readonly string _recordingsDir;
    private readonly int _preRollSamples;
    private readonly long _maxFileSamples;
    private readonly Action<LogLevel, string> _log;
    private readonly object _gate = new();

    private readonly Queue<float[]> _preRoll = new();
    private int _preRollCount;
    private WaveFileWriter? _writer;
    private string? _currentPath;
    private long _writtenSamples;

    private readonly EvidenceProtector? _protector;

    public SourceRecorder(string sourceName, RecordingController controller, string recordingsDir,
        int preRollSeconds, int maxFileMinutes, Action<LogLevel, string> log, EvidenceProtector? protector = null)
    {
        _sourceName = sourceName;
        _controller = controller;
        _recordingsDir = recordingsDir;
        _preRollSamples = Math.Max(0, preRollSeconds) * AudioChunk.SampleRate;
        _maxFileSamples = (long)Math.Max(1, maxFileMinutes) * 60 * AudioChunk.SampleRate;
        _log = log;
        _protector = protector;
        _controller.Activated += OpenNow;
        _controller.Deactivated += CloseFile;
    }

    /// <summary>Smart trigger fired: open the file and write the pre-roll immediately (before the next audio buffer).</summary>
    private void OpenNow()
    {
        lock (_gate)
        {
            if (_writer is null && _controller.IsActive)
            {
                OpenFile();
                WritePreRoll();
            }
        }
    }

    public void Push(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty) return;
        lock (_gate)
        {
            if (_controller.IsActive)
            {
                if (_writer is null)
                {
                    OpenFile();
                    WritePreRoll();
                }
                _writer!.WriteSamples(samples.ToArray(), 0, samples.Length);
                _writtenSamples += samples.Length;
                if (_writtenSamples >= _maxFileSamples)
                {
                    CloseFileCore();
                    OpenFile();
                }
            }
            else
            {
                if (_writer is not null) CloseFileCore();
                if (_preRollSamples > 0) AddToPreRoll(samples);
            }
        }
    }

    private void AddToPreRoll(ReadOnlySpan<float> samples)
    {
        _preRoll.Enqueue(samples.ToArray());
        _preRollCount += samples.Length;
        while (_preRollCount > _preRollSamples && _preRoll.Count > 1)
        {
            _preRollCount -= _preRoll.Dequeue().Length;
        }
    }

    private void WritePreRoll()
    {
        while (_preRoll.Count > 0)
        {
            var block = _preRoll.Dequeue();
            _writer!.WriteSamples(block, 0, block.Length);
            _writtenSamples += block.Length;
        }
        _preRollCount = 0;
    }

    private void OpenFile()
    {
        var now = DateTime.Now;
        var dir = Path.Combine(_recordingsDir, now.ToString("yyyy-MM-dd"));
        Directory.CreateDirectory(dir);
        var safe = string.Concat(_sourceName.Select(c => char.IsLetterOrDigit(c) ? c : '-')).Trim('-');
        _currentPath = Path.Combine(dir, $"{now:HH-mm-ss}_{safe}.wav");
        _writer = new WaveFileWriter(_currentPath, new WaveFormat(AudioChunk.SampleRate, 16, 1));
        _writtenSamples = 0;
        _controller.FileOpened(_currentPath);
        _log(LogLevel.Info, $"Recording {_sourceName} -> {_currentPath}");
    }

    private void CloseFile()
    {
        lock (_gate) CloseFileCore();
    }

    private void CloseFileCore()
    {
        if (_writer is null) return;
        _writer.Dispose();
        _writer = null;
        if (_currentPath is not null)
        {
            _controller.FileClosed(_currentPath);
            var duration = TimeSpan.FromSeconds(_writtenSamples / (double)AudioChunk.SampleRate);
            // Encrypt the finished recording at rest, if evidence protection is on.
            var finalPath = _protector?.Enabled == true ? _protector.EncryptFileInPlace(_currentPath) : _currentPath;
            _log(LogLevel.Info, $"Saved recording {Path.GetFileName(finalPath)} ({duration:mm\\:ss})");
        }
        _currentPath = null;
        _writtenSamples = 0;
    }

    public void Flush() { }

    public void Dispose()
    {
        _controller.Activated -= OpenNow;
        _controller.Deactivated -= CloseFile;
        CloseFile();
    }
}
