using GalilunaShield;
using GalilunaShield.Configuration;
using Xunit;

namespace GalilunaShield.Tests;

public class RedFlagFileTests
{
    [Fact]
    public void Parses_categories_severities_comments_and_duplicates()
    {
        var lines = new[]
        {
            "# header comment",
            "",
            "[Body safety | critical]",
            "private parts",
            "private parts",          // duplicate ignored
            "touch me   # inline comment",
            "[Profanity|low]",
            "shit",
            "[No severity]",
            "meh",
            "// slash comment",
        };
        var entries = RedFlagFile.Parse(lines);
        Assert.Equal(4, entries.Count);
        Assert.Equal(new RedFlagEntry("private parts", "Body safety", Severity.Critical), entries[0]);
        Assert.Equal(new RedFlagEntry("touch me", "Body safety", Severity.Critical), entries[1]);
        Assert.Equal(new RedFlagEntry("shit", "Profanity", Severity.Low), entries[2]);
        Assert.Equal(new RedFlagEntry("meh", "No severity", Severity.Medium), entries[3]);
    }

    [Fact]
    public void Shipped_list_loads_and_has_critical_child_safety_categories()
    {
        var entries = RedFlagFile.Load(Path.Combine(AppContext.BaseDirectory, "redflags.txt"));
        Assert.True(entries.Count > 100);
        Assert.Contains(entries, e => e.Category == "Body safety" && e.Severity == Severity.Critical);
        Assert.Contains(entries, e => e.Category == "Strangers / abduction" && e.Severity == Severity.Critical);
        Assert.Contains(entries, e => e.Category == "Self-harm" && e.Severity == Severity.Critical);
    }

    [Theory]
    [InlineData("critical", Severity.Critical)]
    [InlineData(" HIGH ", Severity.High)]
    [InlineData("low", Severity.Low)]
    [InlineData("whatever", Severity.Medium)]
    public void Severity_parsing(string text, Severity expected) => Assert.Equal(expected, RedFlagFile.ParseSeverity(text));
}
