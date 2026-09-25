using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using GalilunaShield.Configuration;

namespace GalilunaShield.Detection;

public enum MatchConfidence
{
    /// <summary>The exact word/phrase appeared in the transcript.</summary>
    Exact,
    /// <summary>Something very close appeared; the recognizer probably mis-heard a letter or two.</summary>
    Possible,
}

public sealed record RedFlagMatch(string Word, string Category, Severity Severity, MatchConfidence Confidence, string HeardAs);

/// <summary>
/// Matches the red-flag list against transcripts. Exact matching is whole-word, case- and accent-insensitive
/// and tolerates punctuation. Fuzzy matching additionally catches near mis-hearings using edit distance.
/// The rule set can be reloaded at runtime (used for live edits of redflags.txt).
/// </summary>
public sealed class RedFlagDetector : IDisposable
{
    private sealed record Rule(Regex Exact, string Word, string Category, Severity Severity, string[] Tokens, string Joined, bool WildcardEnd);

    // Single words need more letters before fuzzy matching kicks in: "dealer" vs "healer" is one letter apart,
    // and short false alarms like that would erode trust in the alerts. Phrases have more context.
    private const int MinFuzzyPhraseLength = 6;
    private const int MinFuzzySingleWordLength = 8;

    private readonly bool _fuzzy;
    private readonly double _tolerance;
    private volatile Rule[] _rules = Array.Empty<Rule>();
    private readonly List<FileSystemWatcher> _watchers = new();
    private Timer? _reloadDebounce;
    private Func<IEnumerable<RedFlagEntry>>? _loader;

    private volatile HashSet<string> _muted = new(StringComparer.OrdinalIgnoreCase);

    public int RuleCount => _rules.Length;
    public IReadOnlyList<string> Categories => _rules.Select(r => r.Category).Distinct().ToList();

    /// <summary>Words/phrases to suppress even when they match (the parent muted them as false alarms).</summary>
    public void SetMutedWords(IEnumerable<string> phrases) =>
        _muted = new HashSet<string>(phrases ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
    public event Action<int>? Reloaded;
    public event Action<Exception>? ReloadFailed;

    public RedFlagDetector(IEnumerable<RedFlagEntry> entries, bool fuzzyMatching = true, double fuzzyTolerance = 0.2)
    {
        _fuzzy = fuzzyMatching;
        _tolerance = Math.Clamp(fuzzyTolerance, 0, 0.5);
        Load(entries);
    }

    /// <summary>Builds the detector from a loader that can be re-run (main list + enabled buckets).</summary>
    public RedFlagDetector(Func<IEnumerable<RedFlagEntry>> loader, bool fuzzyMatching = true, double fuzzyTolerance = 0.2)
        : this(loader(), fuzzyMatching, fuzzyTolerance)
    {
        _loader = loader;
    }

    /// <summary>Re-runs the loader (after buckets were switched on/off, or a file changed).</summary>
    public void Reload()
    {
        if (_loader is null) return;
        try
        {
            Load(_loader());
            Reloaded?.Invoke(RuleCount);
        }
        catch (Exception ex)
        {
            ReloadFailed?.Invoke(ex);
        }
    }

    public void Load(IEnumerable<RedFlagEntry> entries)
    {
        var rules = new List<Rule>();
        foreach (var entry in entries)
        {
            var normalized = Normalize(entry.Phrase);
            var wildcard = normalized.EndsWith('*');
            var tokens = normalized.TrimEnd('*').Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Replace("'", "")).Where(t => t.Length > 0).ToArray();
            if (tokens.Length == 0) continue;

            // Tokens may be separated by any punctuation/whitespace; apostrophes inside a token are optional
            // so "don't tell" also matches "dont tell".
            var escaped = tokens.Select(t => string.Join("'?", t.Select(c => Regex.Escape(c.ToString()))));
            var pattern = @"(?<![\p{L}\p{N}])" + string.Join(@"[\W_]*", escaped) + (wildcard ? @"[\p{L}]*" : "") + @"(?![\p{L}\p{N}])";
            var regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
            rules.Add(new Rule(regex, entry.Phrase, entry.Category, entry.Severity, tokens, string.Concat(tokens), wildcard));
        }
        _rules = rules.ToArray();
    }

    /// <summary>Reloads the rules automatically whenever the given file is saved.</summary>
    public void WatchFile(string path)
    {
        var full = Path.GetFullPath(path);
        _loader ??= () => RedFlagFile.Load(full);
        Watch(Path.GetDirectoryName(full)!, Path.GetFileName(full));
    }

    /// <summary>Reloads whenever any .txt in the folder changes (bucket files).</summary>
    public void WatchDirectory(string directory)
    {
        if (!Directory.Exists(directory)) return;
        Watch(directory, "*.txt");
    }

    private void Watch(string directory, string filter)
    {
        _reloadDebounce ??= new Timer(_ => Reload(), null, Timeout.Infinite, Timeout.Infinite);
        var watcher = new FileSystemWatcher(directory, filter)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime,
        };
        FileSystemEventHandler onChange = (_, _) => _reloadDebounce.Change(400, Timeout.Infinite); // editors save in bursts
        watcher.Changed += onChange;
        watcher.Created += onChange;
        watcher.Deleted += onChange;
        watcher.Renamed += (_, _) => _reloadDebounce.Change(400, Timeout.Infinite);
        watcher.EnableRaisingEvents = true;
        _watchers.Add(watcher);
    }

    public IReadOnlyList<RedFlagMatch> Detect(string text)
    {
        var rules = _rules;
        var normalized = Normalize(text);
        var matches = new List<RedFlagMatch>();
        var matchedWords = new HashSet<string>();

        foreach (var rule in rules)
        {
            var m = rule.Exact.Match(normalized);
            if (m.Success)
            {
                matches.Add(new RedFlagMatch(rule.Word, rule.Category, rule.Severity, MatchConfidence.Exact, m.Value));
                matchedWords.Add(rule.Word);
            }
        }

        if (_fuzzy)
        {
            var words = Tokenize(normalized);
            foreach (var rule in rules)
            {
                var minLength = rule.Tokens.Length == 1 ? MinFuzzySingleWordLength : MinFuzzyPhraseLength;
                if (matchedWords.Contains(rule.Word) || rule.Joined.Length < minLength) continue;
                var heard = FuzzyFind(rule, words);
                if (heard is not null)
                {
                    matches.Add(new RedFlagMatch(rule.Word, rule.Category, rule.Severity, MatchConfidence.Possible, heard));
                }
            }
        }

        // Drop anything the parent muted, then order most-severe, exact-before-possible first.
        var muted = _muted;
        return matches
            .Where(m => !muted.Contains(m.Word))
            .OrderByDescending(m => m.Severity).ThenBy(m => m.Confidence).ToList();
    }

    /// <summary>
    /// Slides a window over the transcript words and compares the joined letters against the phrase's joined
    /// letters, so split/merged words ("my self" vs "myself") and small mis-hearings both count.
    /// </summary>
    private string? FuzzyFind(Rule rule, string[] words)
    {
        var n = rule.Tokens.Length;
        var maxDistance = Math.Max(1, (int)Math.Floor(rule.Joined.Length * _tolerance));
        string? best = null;
        var bestDistance = int.MaxValue;

        for (var size = Math.Max(1, n - 1); size <= n + 1; size++)
        {
            for (var start = 0; start + size <= words.Length; start++)
            {
                var window = words.AsSpan(start, size);
                var joined = string.Concat(window.ToArray());
                if (Math.Abs(joined.Length - rule.Joined.Length) > maxDistance && !rule.WildcardEnd) continue;

                var candidate = rule.WildcardEnd && joined.Length > rule.Joined.Length ? joined[..rule.Joined.Length] : joined;
                var distance = Levenshtein(candidate, rule.Joined, maxDistance);
                if (distance <= maxDistance && distance < bestDistance)
                {
                    bestDistance = distance;
                    best = string.Join(' ', window.ToArray());
                }
            }
        }

        return best;
    }

    private static string[] Tokenize(string normalized) =>
        Regex.Split(normalized.Replace("'", ""), @"[^\p{L}\p{N}]+").Where(w => w.Length > 0).ToArray();

    /// <summary>Edit distance with an early exit once it exceeds <paramref name="limit"/>.</summary>
    private static int Levenshtein(string a, string b, int limit)
    {
        if (Math.Abs(a.Length - b.Length) > limit) return limit + 1;
        var prev = new int[b.Length + 1];
        var curr = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) prev[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            var rowMin = curr[0];
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
                rowMin = Math.Min(rowMin, curr[j]);
            }
            if (rowMin > limit) return limit + 1;
            (prev, curr) = (curr, prev);
        }
        return prev[b.Length];
    }

    /// <summary>Lower-cases, strips accents and collapses curly apostrophes so "Don’t" matches "don't".</summary>
    private static string Normalize(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(c is '’' or '‘' or '`' ? '\'' : char.ToLowerInvariant(c));
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    public void Dispose()
    {
        foreach (var w in _watchers) w.Dispose();
        _watchers.Clear();
        _reloadDebounce?.Dispose();
    }
}
