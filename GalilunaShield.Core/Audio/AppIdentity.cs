using System.Diagnostics;

namespace GalilunaShield.Audio;

/// <summary>
/// Turns a process into a friendly name a parent recognises - "Discord", "Roblox", "Google Chrome" - instead
/// of a raw executable name like "javaw" or "RobloxPlayerBeta". Uses the product name from the file's version
/// info, with a small table for the games and chat apps whose product name is unhelpful.
/// </summary>
public static class AppIdentity
{
    // Executable (lower-case, no .exe) -> the name a parent knows it by.
    private static readonly Dictionary<string, string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["discord"] = "Discord", ["discordptb"] = "Discord PTB", ["discordcanary"] = "Discord Canary",
        ["robloxplayerbeta"] = "Roblox", ["robloxplayerlauncher"] = "Roblox",
        ["fortniteclient-win64-shipping"] = "Fortnite", ["javaw"] = "Minecraft (Java)", ["minecraft"] = "Minecraft",
        ["minecraftwindows"] = "Minecraft", ["valorant-win64-shipping"] = "Valorant", ["valorant"] = "Valorant",
        ["league of legends"] = "League of Legends", ["leagueclient"] = "League of Legends",
        ["csgo"] = "Counter-Strike", ["cs2"] = "Counter-Strike 2", ["gta5"] = "Grand Theft Auto V",
        ["overwatch"] = "Overwatch", ["steam"] = "Steam", ["steamwebhelper"] = "Steam",
        ["ts3client_win64"] = "TeamSpeak", ["ts3client"] = "TeamSpeak",
        ["zoom"] = "Zoom", ["teams"] = "Microsoft Teams", ["ms-teams"] = "Microsoft Teams",
        ["skype"] = "Skype", ["whatsapp"] = "WhatsApp", ["telegram"] = "Telegram", ["slack"] = "Slack",
        ["chrome"] = "Google Chrome", ["msedge"] = "Microsoft Edge", ["firefox"] = "Firefox",
        ["opera"] = "Opera", ["opera_gx"] = "Opera GX", ["brave"] = "Brave",
        ["vrchat"] = "VRChat", ["among us"] = "Among Us", ["amongus"] = "Among Us",
        ["rblx"] = "Roblox", ["epicgameslauncher"] = "Epic Games", ["battle.net"] = "Battle.net",
    };

    /// <summary>Friendly name for a process. Falls back to the product name, then a tidied executable name.</summary>
    public static string Friendly(uint pid, string processName)
    {
        var key = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? processName[..^4] : processName;
        if (Known.TryGetValue(key, out var known)) return known;

        try
        {
            var proc = Process.GetProcessById((int)pid);
            var product = proc.MainModule?.FileVersionInfo.ProductName;
            if (!string.IsNullOrWhiteSpace(product) && product.Trim().Length > 1)
            {
                return Clean(product.Trim());
            }
        }
        catch
        {
            // access denied or already exited; fall through to the tidied name
        }

        return Titleize(key);
    }

    private static string Clean(string s)
    {
        // Product names sometimes carry a version or a trademark tail; keep it short.
        var cut = s.IndexOfAny(new[] { '®', '™' });
        if (cut > 0) s = s[..cut];
        return s.Trim();
    }

    private static string Titleize(string exe)
    {
        var spaced = exe.Replace('_', ' ').Replace('-', ' ').Trim();
        if (spaced.Length == 0) return exe;
        return string.Join(' ', spaced.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Length <= 3 ? w : char.ToUpperInvariant(w[0]) + w[1..]));
    }
}
