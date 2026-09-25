namespace GalilunaShield.Configuration;

public sealed record RedFlagEntry(string Phrase, string Category, Severity Severity);

/// <summary>
/// Reads the plain-text red-flag list. Format:
///   # comment
///   [Category]                 (severity defaults to Medium)
///   [Category | critical]      (low | medium | high | critical)
///   one word or phrase per line
/// </summary>
public static class RedFlagFile
{
    public const string DefaultCategory = "Uncategorized";

    public static IReadOnlyList<RedFlagEntry> Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Red flag word list not found: {path}");
        }

        // The file may be mid-save when the watcher fires; retry briefly on sharing violations.
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return Parse(File.ReadAllLines(path));
            }
            catch (IOException) when (attempt < 5)
            {
                Thread.Sleep(100);
            }
        }
    }

    public static IReadOnlyList<RedFlagEntry> Parse(IEnumerable<string> lines)
    {
        var entries = new List<RedFlagEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var category = DefaultCategory;
        var severity = Severity.Medium;

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith("//")) continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                var header = line[1..^1];
                var parts = header.Split('|', 2, StringSplitOptions.TrimEntries);
                category = parts[0].Length == 0 ? DefaultCategory : parts[0];
                severity = parts.Length > 1 ? ParseSeverity(parts[1]) : Severity.Medium;
                continue;
            }

            // Allow an inline comment after the phrase:  weed   # slang for marijuana
            var hash = line.IndexOf(" #", StringComparison.Ordinal);
            if (hash > 0) line = line[..hash].Trim();

            if (seen.Add(line))
            {
                entries.Add(new RedFlagEntry(line, category, severity));
            }
        }

        return entries;
    }

    public static Severity ParseSeverity(string text) => text.Trim().ToLowerInvariant() switch
    {
        "critical" or "crit" or "4" => Severity.Critical,
        "high" or "3" => Severity.High,
        "low" or "1" => Severity.Low,
        _ => Severity.Medium,
    };
}
