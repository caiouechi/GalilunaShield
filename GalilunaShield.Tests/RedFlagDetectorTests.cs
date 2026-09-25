using GalilunaShield;
using GalilunaShield.Configuration;
using GalilunaShield.Detection;
using Xunit;

namespace GalilunaShield.Tests;

public class RedFlagDetectorTests
{
    private static RedFlagDetector ShippedList(bool fuzzy = true)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "redflags.txt");
        return new RedFlagDetector(RedFlagFile.Load(path), fuzzy, 0.2);
    }

    [Theory]
    [InlineData("I'm going to kill myself tonight", "kill myself")]
    [InlineData("Dont tell mommy about this", "don't tell mommy")]
    [InlineData("Don’t tell Daddy, okay?", "don't tell daddy")]
    [InlineData("he was suicidal", "suicid*")]
    [InlineData("they murdered him", "murder*")]
    [InlineData("what the fucking hell", "fuck*")]
    [InlineData("ARE YOU HOME ALONE?", "are you home alone")]
    public void Exact_matches_whole_words_case_and_punctuation_insensitively(string text, string expectedWord)
    {
        using var d = ShippedList();
        var m = d.Detect(text);
        Assert.Contains(m, x => x.Word == expectedWord && x.Confidence == MatchConfidence.Exact);
    }

    [Theory]
    [InlineData("I want to kill my sale", "kill myself")]
    [InlineData("I wanna kill my self", "kill myself")]
    [InlineData("I want to dye", "want to die")]
    [InlineData("keep it a secrete okay", "keep it a secret")]
    [InlineData("get in the cart now", "get in the car")]
    [InlineData("show me your privet parts", "private parts")]
    [InlineData("are you home a lone", "home alone")]
    public void Fuzzy_matches_near_mishearings_as_possible(string text, string expectedWord)
    {
        using var d = ShippedList();
        var m = d.Detect(text);
        Assert.Contains(m, x => x.Word == expectedWord && x.Confidence == MatchConfidence.Possible);
    }

    [Theory]
    [InlineData("I have six apples")]
    [InlineData("the healer is over there")]
    [InlineData("we went to the shitake mushroom farm")]
    [InlineData("I love the weather today")]
    [InlineData("she had a lovely bath with her ducks")]
    [InlineData("the movie was so sexist")]
    [InlineData("hello how are you doing today my friend")]
    [InlineData("we need to meet the teacher")]
    public void Innocent_sentences_do_not_match(string text)
    {
        using var d = ShippedList();
        Assert.Empty(d.Detect(text));
    }

    [Fact]
    public void Fuzzy_can_be_disabled()
    {
        using var d = ShippedList(fuzzy: false);
        Assert.Empty(d.Detect("I want to kill my sale"));
    }

    [Fact]
    public void Matches_are_ordered_by_severity_then_exactness()
    {
        var entries = new[]
        {
            new RedFlagEntry("shit", "Profanity", Severity.Low),
            new RedFlagEntry("get in the car", "Strangers", Severity.Critical),
        };
        using var d = new RedFlagDetector(entries);
        var m = d.Detect("oh shit, get in the car");
        Assert.Equal("get in the car", m[0].Word);
        Assert.Equal(Severity.Critical, m[0].Severity);
    }

    [Fact]
    public void Load_replaces_rules_at_runtime()
    {
        using var d = new RedFlagDetector(new[] { new RedFlagEntry("purple elephant", "Test", Severity.Low) });
        Assert.NotEmpty(d.Detect("a purple elephant"));
        d.Load(new[] { new RedFlagEntry("green giraffe", "Test", Severity.Low) });
        Assert.Empty(d.Detect("a purple elephant"));
        Assert.NotEmpty(d.Detect("a green giraffe"));
    }

    [Fact]
    public void Accents_are_ignored()
    {
        using var d = new RedFlagDetector(new[] { new RedFlagEntry("não conte para a mamãe", "Body safety", Severity.Critical) });
        Assert.NotEmpty(d.Detect("Nao conte para a mamae, ok?"));
    }
}
