using GalilunaShield;
using GalilunaShield.Alerts;
using GalilunaShield.Audio;
using GalilunaShield.Configuration;
using Xunit;

namespace GalilunaShield.Tests;

public class VoicePitchTests
{
    private static float[] Voice(float hz, double seconds, float amplitude = 0.3f)
    {
        var n = (int)(seconds * AudioChunk.SampleRate);
        var s = new float[n];
        for (var i = 0; i < n; i++)
        {
            var t = (double)i / AudioChunk.SampleRate;
            // a few harmonics, like a voice, not a pure tone
            s[i] = amplitude * (float)(Math.Sin(2 * Math.PI * hz * t) + 0.5 * Math.Sin(2 * Math.PI * 2 * hz * t) + 0.25 * Math.Sin(2 * Math.PI * 3 * hz * t)) / 1.75f;
        }
        return s;
    }

    [Theory]
    [InlineData(110f)]  // adult male
    [InlineData(200f)]  // adult female
    [InlineData(300f)]  // child
    public void Estimates_fundamental_frequency_within_ten_percent(float hz)
    {
        var est = VoicePitch.Estimate(Voice(hz, 2), 0.005f, 165f);
        Assert.NotNull(est);
        Assert.InRange(est!.MedianHz, hz * 0.9, hz * 1.1);
        Assert.InRange(est.VoicedSeconds, 1.5, 2.1);
    }

    [Fact]
    public void Adult_male_voice_is_mostly_below_threshold_and_child_is_not()
    {
        var adult = VoicePitch.Estimate(Voice(120f, 2), 0.005f, 165f)!;
        var child = VoicePitch.Estimate(Voice(300f, 2), 0.005f, 165f)!;
        Assert.True(adult.LowFraction > 0.9);
        Assert.True(child.LowFraction < 0.1);
    }

    [Fact]
    public void Silence_yields_no_estimate()
    {
        Assert.Null(VoicePitch.Estimate(new float[AudioChunk.SampleRate], 0.005f, 165f));
    }
}

public class BucketTests
{
    private static string ShippedBuckets => Path.Combine(AppContext.BaseDirectory, "buckets");

    [Fact]
    public void Shipped_buckets_load_with_names_and_descriptions()
    {
        var catalog = RedFlagBuckets.LoadCatalog(ShippedBuckets, Path.Combine(Path.GetTempPath(), "gs-none-" + Guid.NewGuid().ToString("N")));
        Assert.True(catalog.Count >= 15);
        Assert.All(catalog, b =>
        {
            Assert.False(string.IsNullOrWhiteSpace(b.Name));
            Assert.False(string.IsNullOrWhiteSpace(b.Description));
            Assert.True(b.Count > 5, $"{b.Id} has too few entries");
            Assert.True(b.Shipped);
        });
        Assert.Contains(catalog, b => b.Id == "heavy-cursing");
        Assert.Contains(catalog, b => b.Id == "religion-islam" && b.Entries.All(e => e.Severity == Severity.Low));
        Assert.Contains(catalog, b => b.Id == "self-harm-extended" && b.Entries.All(e => e.Severity == Severity.Critical));
    }

    [Fact]
    public void User_bucket_with_same_id_replaces_shipped_one()
    {
        var user = Path.Combine(Path.GetTempPath(), "gs-buckets-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(user);
        try
        {
            File.WriteAllText(Path.Combine(user, "gambling.txt"), "# name: My gambling list\n# description: custom\n[Gambling | high]\nonly this word\n");
            var catalog = RedFlagBuckets.LoadCatalog(ShippedBuckets, user);
            var g = catalog.Single(b => b.Id == "gambling");
            Assert.Equal("My gambling list", g.Name);
            Assert.False(g.Shipped);
            Assert.Single(g.Entries);
        }
        finally { Directory.Delete(user, true); }
    }

    [Fact]
    public void Combine_adds_enabled_buckets_without_duplicates()
    {
        var main = new[] { new RedFlagEntry("shit", "Profanity", Severity.Low) };
        var bucket = RedFlagBuckets.Load(Path.Combine(ShippedBuckets, "heavy-cursing.txt"), true);
        var combined = RedFlagBuckets.Combine(main, new[] { bucket });
        Assert.Equal(1 + bucket.Count, combined.Count);
        Assert.Contains(combined, e => e.Phrase == "bullshit" && e.Category == "Heavy cursing");
    }
}

public class ActivityLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gs-activity-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void Clean_exit_leaves_no_marker_and_unclean_exit_is_detected_next_start()
    {
        using (var log = new ActivityLog(_dir, "Test"))
        {
            log.Start();
            log.Stop("test");
        }
        using (var log = new ActivityLog(_dir, "Test"))
        {
            log.Start(); // marker was removed -> no UnexpectedEnd
            var today = log.Read(DateOnly.FromDateTime(DateTime.Now));
            Assert.DoesNotContain(today, e => e.Kind == ActivityKind.UnexpectedEnd);
            // simulate a kill: dispose without Stop, marker stays
        }
        using (var log = new ActivityLog(_dir, "Test"))
        {
            log.Start();
            var today = log.Read(DateOnly.FromDateTime(DateTime.Now));
            Assert.Contains(today, e => e.Kind == ActivityKind.UnexpectedEnd);
            Assert.Contains(today, e => e.Kind == ActivityKind.AppStarted);
            log.Stop("test");
        }
    }

    [Fact]
    public void Events_round_trip_through_the_daily_file()
    {
        using var log = new ActivityLog(_dir, "Test");
        log.Write(ActivityKind.MonitoringStopped, "Monitoring stopped (hotkey).");
        var today = log.Read(DateOnly.FromDateTime(DateTime.Now));
        var e = Assert.Single(today);
        Assert.Equal(ActivityKind.MonitoringStopped, e.Kind);
        Assert.Equal("Monitoring stopped (hotkey).", e.Message);
        Assert.True(e.IsNoteworthy);
    }
}

public class PerformancePresetTests
{
    [Fact]
    public void Presets_set_model_and_beam()
    {
        var c = new AppConfig { Performance = PerformancePreset.Light };
        c.ApplyPerformancePreset();
        Assert.Equal("tiny", c.ModelSize);
        Assert.Equal(1, c.BeamSize);

        c.Performance = PerformancePreset.Accurate;
        c.ApplyPerformancePreset();
        Assert.Equal("small", c.ModelSize);
        Assert.Equal(5, c.BeamSize);

        c.Performance = PerformancePreset.Custom;
        c.ModelSize = "medium";
        c.ApplyPerformancePreset();
        Assert.Equal("medium", c.ModelSize);
    }

    [Fact]
    public void Suggestion_is_a_valid_preset() =>
        Assert.Contains(AppConfig.SuggestedPreset(), new[] { PerformancePreset.Light, PerformancePreset.Balanced, PerformancePreset.Accurate });
}
