namespace GalilunaShield.Configuration;

/// <summary>
/// Sensible starting settings for a child's age, so a parent isn't faced with a wall of knobs. The
/// onboarding wizard applies one of these; the parent can still change anything afterwards.
/// </summary>
public static class AgeProfile
{
    public enum Band { Under5, Age5to8, Age9to12, Age13to15, Age16to17 }

    public static Band Parse(string? text) => text switch
    {
        "Under 5" => Band.Under5,
        "5 to 8" => Band.Age5to8,
        "9 to 12" => Band.Age9to12,
        "13 to 15" => Band.Age13to15,
        "16 to 17" => Band.Age16to17,
        _ => Band.Age9to12,
    };

    public static string Describe(Band band) => band switch
    {
        Band.Under5 => "Very young child: listen closely to everything, screenshots on every flag.",
        Band.Age5to8 => "Young child: broad protection, screenshots on medium and above.",
        Band.Age9to12 => "Pre-teen: balanced protection for games and calls.",
        Band.Age13to15 => "Teen: focus on the serious risks, less noise from everyday chatter.",
        Band.Age16to17 => "Older teen: the most serious risks only, minimal intrusion.",
    };

    /// <summary>
    /// Which optional topic packs to switch on by default for an age. Younger = more protective (includes
    /// scary/weapon/religion topics); older = only the safety-critical extras, to cut false alarms.
    /// </summary>
    public static IReadOnlyList<string> DefaultBuckets(Band band) => band switch
    {
        Band.Under5 => new[] { "self-harm-extended", "online-safety-extended", "horror-scary", "weapons-guns", "occult-supernatural" },
        Band.Age5to8 => new[] { "self-harm-extended", "online-safety-extended", "horror-scary", "gambling" },
        Band.Age9to12 => new[] { "self-harm-extended", "online-safety-extended", "gambling", "dating-romance", "money-scams-extended" },
        Band.Age13to15 => new[] { "self-harm-extended", "online-safety-extended", "drugs-slang-extended", "body-image-dieting" },
        Band.Age16to17 => new[] { "self-harm-extended", "online-safety-extended", "drugs-slang-extended" },
    };

    /// <summary>Applies age-appropriate defaults to a fresh (or existing) config in place.</summary>
    public static void Apply(AppConfig config, Band band, IEnumerable<string> availableBucketIds)
    {
        var available = new HashSet<string>(availableBucketIds, StringComparer.OrdinalIgnoreCase);
        config.EnabledBuckets = DefaultBuckets(band).Where(available.Contains).ToList();

        // Younger children: screenshot more, keep more recording; older teens: less intrusive.
        switch (band)
        {
            case Band.Under5:
            case Band.Age5to8:
                config.Mode = MonitorMode.Both;
                config.Screenshots.Enabled = true;
                config.Screenshots.MinSeverity = band == Band.Under5 ? Severity.Low : Severity.Medium;
                config.VoiceAnalysis.Enabled = true;
                config.VoiceAnalysis.FlagOnMicrophone = true; // an adult in the room matters at this age
                config.Recording.Mode = RecordingMode.Smart;
                break;
            case Band.Age9to12:
                config.Mode = MonitorMode.Both;
                config.Screenshots.MinSeverity = Severity.Medium;
                config.VoiceAnalysis.Enabled = true;
                config.Recording.Mode = RecordingMode.Smart;
                break;
            case Band.Age13to15:
                config.Mode = MonitorMode.Both;
                config.Screenshots.MinSeverity = Severity.High;
                config.VoiceAnalysis.Enabled = true;
                config.Recording.Mode = RecordingMode.Smart;
                break;
            case Band.Age16to17:
                config.Mode = MonitorMode.Both;
                config.Screenshots.MinSeverity = Severity.High;
                config.VoiceAnalysis.Enabled = false; // older teens talk to adults legitimately; avoid noise
                config.Recording.Mode = RecordingMode.Smart;
                break;
        }
    }
}
