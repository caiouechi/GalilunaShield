using GalilunaShield;
using GalilunaShield.Alerts;
using GalilunaShield.Audio;
using GalilunaShield.Configuration;
using GalilunaShield.Detection;
using Xunit;

namespace GalilunaShield.Tests;

public class MuteTests
{
    [Fact]
    public void Muted_words_are_not_returned_even_when_they_match()
    {
        var entries = new[]
        {
            new RedFlagEntry("shoot", "Weapons", Severity.Medium),
            new RedFlagEntry("home alone", "Strangers", Severity.Critical),
        };
        using var d = new RedFlagDetector(entries);
        Assert.Equal(2, d.Detect("I will shoot you, are you home alone").Count);

        d.SetMutedWords(new[] { "shoot" });
        var m = d.Detect("I will shoot you, are you home alone");
        Assert.Single(m);
        Assert.Equal("home alone", m[0].Word);
    }

    [Fact]
    public void Unmuting_restores_matching()
    {
        using var d = new RedFlagDetector(new[] { new RedFlagEntry("shoot", "Weapons", Severity.Medium) });
        d.SetMutedWords(new[] { "shoot" });
        Assert.Empty(d.Detect("shoot"));
        d.SetMutedWords(System.Array.Empty<string>());
        Assert.NotEmpty(d.Detect("shoot"));
    }

    [Theory]
    [InlineData("System audio: Discord", "Discord")]
    [InlineData("System audio: Roblox", "Roblox")]
    [InlineData("Microphone", null)]
    public void App_name_is_extracted_from_source(string source, string? expected) =>
        Assert.Equal(expected, MonitorService.AppNameOf(source));
}

public class AlertStatusTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gs-status-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void Alert_status_persists_and_reads_back()
    {
        var store = new AlertStore(_dir, saveAudio: false);
        var chunk = new AudioChunk("Microphone", DateTimeOffset.Now.AddSeconds(-2), DateTimeOffset.Now, new float[AudioChunk.SampleRate]);
        var rec = store.WriteAlert(DateTimeOffset.Now, chunk, "bad word", 0.9f,
            new List<RedFlagMatch> { new("bad", "X", Severity.Low, MatchConfidence.Exact, "bad") },
            Array.Empty<ContextLine>(), Array.Empty<string>());

        Assert.Empty(store.ReadStatuses());
        store.SetAlertStatus(rec.Id, AlertStatus.Dismissed, "false alarm");
        var statuses = store.ReadStatuses();
        Assert.Equal(AlertStatus.Dismissed, statuses[rec.Id].Status);
        Assert.Equal("false alarm", statuses[rec.Id].Note);

        store.SetAlertStatus(rec.Id, AlertStatus.Reviewed);
        Assert.Equal(AlertStatus.Reviewed, store.ReadStatuses()[rec.Id].Status);
    }
}

public class ConsentTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "gs-consent-" + Guid.NewGuid().ToString("N") + ".json");
    public void Dispose() { try { File.Delete(_path); } catch { } }

    [Fact]
    public void Needs_consent_until_current_version_is_accepted()
    {
        var mgr = new ConsentManager(_path);
        Assert.True(mgr.NeedsConsent());
        var rec = mgr.Save(new Dictionary<string, bool> { ["I am the guardian"] = true }, "9 to 12", "1.2.3");
        Assert.False(mgr.NeedsConsent());
        Assert.Equal(LegalVersions.Combined, rec.CombinedVersion);
        Assert.Equal("9 to 12", mgr.Read()!.ChildAgeBand);
    }

    [Fact]
    public void Old_version_forces_reconsent()
    {
        var mgr = new ConsentManager(_path);
        // Write a record with a stale combined version by hand
        File.WriteAllText(_path, System.Text.Json.JsonSerializer.Serialize(new ConsentRecord { CombinedVersion = "eula:0.1;privacy:0.1;aup:0.1" }));
        Assert.True(mgr.NeedsConsent());
    }
}
