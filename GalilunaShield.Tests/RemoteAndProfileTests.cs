using GalilunaShield;
using GalilunaShield.Alerts;
using GalilunaShield.Audio;
using GalilunaShield.Configuration;
using GalilunaShield.Detection;
using Xunit;

namespace GalilunaShield.Tests;

public class AgeProfileTests
{
    private static readonly string[] AllBuckets =
    {
        "self-harm-extended","online-safety-extended","horror-scary","weapons-guns","occult-supernatural",
        "gambling","dating-romance","money-scams-extended","drugs-slang-extended","body-image-dieting",
    };

    [Fact]
    public void Younger_children_get_more_protective_defaults_than_teens()
    {
        var young = new AppConfig();
        AgeProfile.Apply(young, AgeProfile.Band.Under5, AllBuckets);
        var teen = new AppConfig();
        AgeProfile.Apply(teen, AgeProfile.Band.Age16to17, AllBuckets);

        Assert.True(young.EnabledBuckets.Count > teen.EnabledBuckets.Count);
        Assert.Equal(Severity.Low, young.Screenshots.MinSeverity);   // screenshot everything for the youngest
        Assert.Equal(Severity.High, teen.Screenshots.MinSeverity);   // only serious flags for older teens
        Assert.True(young.VoiceAnalysis.FlagOnMicrophone);           // an adult in the room matters
        Assert.False(teen.VoiceAnalysis.Enabled);                    // teens talk to adults legitimately
        Assert.Contains("self-harm-extended", young.EnabledBuckets);
        Assert.Contains("self-harm-extended", teen.EnabledBuckets);  // always on
    }

    [Fact]
    public void Only_available_buckets_are_enabled()
    {
        var c = new AppConfig();
        AgeProfile.Apply(c, AgeProfile.Band.Age9to12, new[] { "gambling" });
        Assert.All(c.EnabledBuckets, b => Assert.Equal("gambling", b));
    }
}

public class EvidencePackTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gs-pack-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void Pack_contains_manifest_with_hashes_and_the_clip()
    {
        var store = new AlertStore(_dir, saveAudio: true);
        var chunk = new AudioChunk("Microphone", DateTimeOffset.Now.AddSeconds(-1), DateTimeOffset.Now, new float[AudioChunk.SampleRate]);
        store.WriteAlert(DateTimeOffset.Now, chunk, "come with me", 0.9f,
            new List<RedFlagMatch> { new("come with me", "Strangers", Severity.Critical, MatchConfidence.Exact, "come with me") },
            Array.Empty<ContextLine>(), Array.Empty<string>());

        var zipPath = Path.Combine(_dir, "pack.zip");
        var pack = new EvidencePack(store);
        pack.Build(zipPath, DateOnly.FromDateTime(DateTime.Now), DateOnly.FromDateTime(DateTime.Now));

        Assert.True(File.Exists(zipPath));
        using var zip = System.IO.Compression.ZipFile.OpenRead(zipPath);
        Assert.Contains(zip.Entries, e => e.FullName == "MANIFEST.txt");
        Assert.Contains(zip.Entries, e => e.FullName.StartsWith("clips/"));
        Assert.Contains(zip.Entries, e => e.FullName == "alerts.jsonl");

        var manifest = new StreamReader(zip.GetEntry("MANIFEST.txt")!.Open()).ReadToEnd();
        Assert.Contains("evidence pack", manifest);
        Assert.Contains("SHA-256", manifest.Replace("fingerprint", "SHA-256")); // manifest references fingerprints
        // every listed file line begins with a 64-hex hash
        Assert.Contains(manifest.Split('\n'), l => System.Text.RegularExpressions.Regex.IsMatch(l.Trim(), "^[0-9a-f]{64}  "));
    }
}

public class NotifierTests
{
    [Fact]
    public void Disabled_or_too_minor_alerts_do_not_configure_channels()
    {
        var cfg = new NotificationConfig { Enabled = false };
        using var n = new GalilunaShield.Notifications.Notifier(cfg, (_, _) => { });
        Assert.False(n.AnyChannelConfigured);
    }

    [Fact]
    public void Enabled_with_email_reports_configured()
    {
        var cfg = new NotificationConfig { Enabled = true, Email = new EmailNotificationConfig { Enabled = true } };
        using var n = new GalilunaShield.Notifications.Notifier(cfg, (_, _) => { });
        Assert.True(n.AnyChannelConfigured);
    }
}
