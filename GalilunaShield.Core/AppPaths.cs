namespace GalilunaShield;

/// <summary>
/// Where things live. The program folder (Program Files) is read-only for normal users, so user-editable
/// files are copied to a per-user folder on first run, the big speech model goes to local app data, and
/// alerts/recordings/reports go to Documents where parents can find them.
/// </summary>
public static class AppPaths
{
    public const string ProductName = "Galiluna Shield";

    /// <summary>Folder the executable runs from. Holds the default appsettings.json / redflags.txt.</summary>
    public static string ProgramDirectory => AppContext.BaseDirectory;

    /// <summary>%APPDATA%\Galiluna Shield — user-editable configuration.</summary>
    public static string ConfigDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ProductName);

    /// <summary>%LOCALAPPDATA%\Galiluna Shield\models — downloaded speech models (large, machine-local).</summary>
    public static string ModelDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ProductName, "models");

    public static string ConfigFile => Path.Combine(ConfigDirectory, "appsettings.json");
    public static string RedFlagsFile => Path.Combine(ConfigDirectory, "redflags.txt");
    public static string ConsentFile => Path.Combine(ConfigDirectory, "consent.json");
    /// <summary>Legal documents shipped with the program (Privacy Policy, EULA, Acceptable Use, Cookie Policy).</summary>
    public static string LegalDirectory => Path.Combine(ProgramDirectory, "legal");

    /// <summary>Optional word buckets shipped with the program.</summary>
    public static string ShippedBucketsDirectory => Path.Combine(ProgramDirectory, "buckets");
    /// <summary>Parents' own or edited buckets; a file here with a shipped file's name replaces it.</summary>
    public static string UserBucketsDirectory => Path.Combine(ConfigDirectory, "buckets");

    /// <summary>Resolves the output directory setting: relative paths are under Documents.</summary>
    public static string ResolveOutputDirectory(string configured) =>
        Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), configured);

    /// <summary>
    /// Where the activity/event log and diagnostic logs go. Uses the configured logs directory when set,
    /// otherwise falls back to the output directory. A relative logs path is placed under Documents.
    /// </summary>
    public static string ResolveLogsDirectory(string configuredLogs, string configuredOutput)
    {
        if (string.IsNullOrWhiteSpace(configuredLogs))
        {
            return ResolveOutputDirectory(configuredOutput);
        }
        return Path.IsPathRooted(configuredLogs)
            ? configuredLogs
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), configuredLogs);
    }

    /// <summary>
    /// Makes sure the per-user config folder exists and contains appsettings.json and redflags.txt,
    /// copying the defaults shipped with the program on first run. Never overwrites the user's edits.
    /// </summary>
    public static void EnsureUserConfig()
    {
        Directory.CreateDirectory(ConfigDirectory);
        Directory.CreateDirectory(UserBucketsDirectory);
        CopyDefaultIfMissing("appsettings.json", ConfigFile);
        CopyDefaultIfMissing("redflags.txt", RedFlagsFile);
        // Keep a pristine copy of the shipped word list next to the user's, so they can see what's new after an update.
        var shipped = Path.Combine(ProgramDirectory, "redflags.txt");
        if (File.Exists(shipped))
        {
            try { File.Copy(shipped, Path.Combine(ConfigDirectory, "redflags.default.txt"), overwrite: true); } catch { /* best effort */ }
        }
    }

    private static void CopyDefaultIfMissing(string fileName, string destination)
    {
        if (File.Exists(destination)) return;
        var source = Path.Combine(ProgramDirectory, fileName);
        if (File.Exists(source))
        {
            File.Copy(source, destination);
        }
    }
}
