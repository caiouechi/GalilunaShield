using GalilunaShield;
using GalilunaShield.Alerts;
using GalilunaShield.Audio;
using GalilunaShield.Detection;
using GalilunaShield.Reports;
using Xunit;

namespace GalilunaShield.Tests;

public class AlertStoreAndReportTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "GalilunaShieldTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private static AudioChunk Chunk(string source) =>
        new(source, DateTimeOffset.Now.AddSeconds(-3), DateTimeOffset.Now, new float[AudioChunk.SampleRate * 2]);

    [Fact]
    public void Alert_is_persisted_with_clip_hash_and_can_be_read_back()
    {
        var store = new AlertStore(_dir, saveAudio: true);
        var matches = new List<RedFlagMatch>
        {
            new("don't tell mommy", "Body safety", Severity.Critical, MatchConfidence.Exact, "don't tell mommy"),
            new("our secret", "Body safety", Severity.Critical, MatchConfidence.Possible, "our secrete"),
        };
        var context = new List<ContextLine> { new(DateTimeOffset.Now.AddSeconds(-10), "Microphone", "hello there") };

        var record = store.WriteAlert(DateTimeOffset.Now, Chunk("Microphone"), "our secrete, don't tell mommy", 0.81f, matches, context, new[] { @"C:\rec\a.wav" });

        Assert.True(File.Exists(record.DetailsPath));
        Assert.True(File.Exists(record.ClipPath));
        Assert.Equal(AlertStore.Sha256(record.ClipPath!), record.ClipSha256);
        Assert.Equal(Severity.Critical, record.Severity);
        Assert.False(record.PossibleOnly);

        var all = store.ReadAlerts();
        Assert.Single(all);
        Assert.Equal(record.Id, all[0].Id);
        Assert.Equal(2, all[0].Matches.Count);
        Assert.Single(all[0].Recordings);

        var details = File.ReadAllText(record.DetailsPath!);
        Assert.Contains("CRITICAL", details);
        Assert.Contains("SHA-256", details);
        Assert.Contains("hello there", details);
    }

    [Fact]
    public void Corrupt_log_lines_are_skipped()
    {
        var store = new AlertStore(_dir, saveAudio: false);
        Directory.CreateDirectory(Path.GetDirectoryName(store.AlertLogPath)!);
        File.WriteAllText(store.AlertLogPath, "not json\n");
        store.WriteAlert(DateTimeOffset.Now, Chunk("Microphone"), "x", 0.9f,
            new List<RedFlagMatch> { new("shit", "Profanity", Severity.Low, MatchConfidence.Exact, "shit") }, Array.Empty<ContextLine>(), Array.Empty<string>());
        Assert.Single(store.ReadAlerts());
    }

    [Fact]
    public void Reports_are_generated_with_summary_timeline_and_csv()
    {
        var store = new AlertStore(_dir, saveAudio: true);
        store.WriteAlert(DateTimeOffset.Now, Chunk("System audio: Discord"), "are you home alone", 0.9f,
            new List<RedFlagMatch> { new("home alone", "Strangers / abduction", Severity.Critical, MatchConfidence.Exact, "home alone") },
            Array.Empty<ContextLine>(), Array.Empty<string>());
        store.WriteUnclear(Chunk("Microphone"), "<no words recognized>", 0f);

        var reports = new ReportBuilder(store, writeCsv: true);
        var daily = reports.BuildDaily(DateOnly.FromDateTime(DateTime.Now));
        var weekly = reports.BuildWeekly(DateOnly.FromDateTime(DateTime.Now));
        var index = reports.BuildIndex();

        Assert.True(File.Exists(daily));
        Assert.True(File.Exists(weekly));
        Assert.True(File.Exists(index));
        Assert.True(File.Exists(Path.ChangeExtension(daily, ".csv")));

        var html = File.ReadAllText(daily);
        Assert.Contains("<mark>home alone</mark>", html); // matched words are highlighted in the flagged sentence
        Assert.Contains("CRITICAL", html);
        Assert.Contains("<audio", html);
        Assert.Contains("SHA-256", html);
        Assert.Contains("Unclear speech", html);
        Assert.Contains("no words recognized", html);
        Assert.DoesNotContain("<script", html); // self-contained, no scripting

        var csv = File.ReadAllLines(Path.ChangeExtension(daily, ".csv"));
        Assert.Equal(2, csv.Length);
        Assert.StartsWith("Time,Severity,Source", csv[0]);
        Assert.Contains("Critical", csv[1]);
    }

    [Fact]
    public void Empty_period_report_says_no_red_flags()
    {
        var store = new AlertStore(_dir, saveAudio: false);
        var reports = new ReportBuilder(store);
        var path = reports.BuildDaily(new DateOnly(2020, 1, 1));
        Assert.Contains("No red flags in this period", File.ReadAllText(path));
    }
}
