namespace GalilunaShield.Configuration;

/// <summary>
/// An optional themed word list a parent can switch on: "heavy cursing", a religion they don't want
/// discussed with their child, gambling, dating... Each bucket is one .txt file in the same format as
/// redflags.txt, with header comments:
///   # name: Heavy cursing
///   # description: Everyday swearing and crude slang, beyond the core profanity list.
/// </summary>
public sealed record RedFlagBucket(string Id, string Name, string Description, string Path, IReadOnlyList<RedFlagEntry> Entries, bool Shipped)
{
    public int Count => Entries.Count;
}

public static class RedFlagBuckets
{
    /// <summary>
    /// Loads buckets from the program folder (shipped) and the user's config folder (custom). A custom file
    /// with the same name as a shipped one replaces it, so parents can edit a copy.
    /// </summary>
    public static IReadOnlyList<RedFlagBucket> LoadCatalog(string shippedDirectory, string userDirectory)
    {
        var byId = new Dictionary<string, RedFlagBucket>(StringComparer.OrdinalIgnoreCase);
        foreach (var (dir, shipped) in new[] { (shippedDirectory, true), (userDirectory, false) })
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.GetFiles(dir, "*.txt").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var bucket = Load(file, shipped);
                    byId[bucket.Id] = bucket;
                }
                catch
                {
                    // a malformed custom file must not take the app down
                }
            }
        }
        return byId.Values.OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static RedFlagBucket Load(string path, bool shipped)
    {
        var id = System.IO.Path.GetFileNameWithoutExtension(path);
        var lines = File.ReadAllLines(path);
        var name = id.Replace('-', ' ');
        var description = "";
        foreach (var raw in lines.Take(20))
        {
            var line = raw.Trim();
            if (line.StartsWith("# name:", StringComparison.OrdinalIgnoreCase)) name = line[7..].Trim();
            else if (line.StartsWith("# description:", StringComparison.OrdinalIgnoreCase)) description = line[14..].Trim();
        }
        var entries = RedFlagFile.Parse(lines);
        return new RedFlagBucket(id, name, description, path, entries, shipped);
    }

    /// <summary>The main list plus every enabled bucket, with bucket entries tagged by bucket name.</summary>
    public static IReadOnlyList<RedFlagEntry> Combine(IReadOnlyList<RedFlagEntry> main, IEnumerable<RedFlagBucket> enabled)
    {
        var all = new List<RedFlagEntry>(main);
        var seen = new HashSet<string>(main.Select(e => e.Phrase), StringComparer.OrdinalIgnoreCase);
        foreach (var bucket in enabled)
        {
            foreach (var e in bucket.Entries)
            {
                if (seen.Add(e.Phrase))
                {
                    // Keep the bucket's own category text; prefix makes the report show where a word came from.
                    all.Add(e with { Category = e.Category == RedFlagFile.DefaultCategory ? bucket.Name : e.Category });
                }
            }
        }
        return all;
    }
}
