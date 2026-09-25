using System.Diagnostics;

namespace GalilunaShield.App;

/// <summary>
/// "Keep alive" tamper resistance. Registers a Windows Scheduled Task that runs as SYSTEM and relaunches
/// Galiluna Shield at logon and every few minutes, so if a child closes or kills it, it comes back within
/// a few minutes and the gap is recorded in the program-event log.
///
/// This is best-effort. A determined user with administrator rights can still remove the task. The strong
/// version of this protection is to give the child a **standard (non-administrator) Windows account**, so
/// they cannot delete a SYSTEM-owned task or read the encrypted evidence. See SECURITY.md.
///
/// Creating/removing the task needs administrator rights, so those actions relaunch the app elevated with
/// a "--watchdog-task on|off" argument (handled in App.OnStartup) which does the work and exits.
/// </summary>
public static class Watchdog
{
    public const string TaskName = "GalilunaShieldKeepAlive";

    public static bool IsInstalled()
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks.exe", $"/query /tn \"{TaskName}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var p = Process.Start(psi);
            if (p is null) return false;
            p.WaitForExit(5000);
            return p.ExitCode == 0;
        }
        catch { return false; }
    }

    /// <summary>Requests the change with elevation. Returns false if the user declined the UAC prompt.</summary>
    public static bool RequestSet(bool enable)
    {
        try
        {
            var exe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "GalilunaShield.exe");
            var psi = new ProcessStartInfo(exe, $"--watchdog-task {(enable ? "on" : "off")}")
            {
                UseShellExecute = true,
                Verb = "runas", // triggers the UAC elevation prompt
            };
            using var p = Process.Start(psi);
            if (p is null) return false;
            p.WaitForExit(30000);
            return p.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false; // user cancelled the UAC prompt
        }
        catch { return false; }
    }

    /// <summary>Runs elevated (from App.OnStartup) to create or delete the task. Returns a process exit code.</summary>
    public static int ApplyElevated(bool enable)
    {
        try
        {
            if (enable)
            {
                var exe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "GalilunaShield.exe");
                // Run as SYSTEM, at logon and repeating every 3 minutes, launching the app minimized.
                var args = $"/create /f /tn \"{TaskName}\" /ru SYSTEM /sc onlogon /rl highest /tr \"\\\"{exe}\\\" --minimized\"";
                var create = Run(args);
                // A second definition that repeats keeps it coming back during the session.
                Run($"/create /f /tn \"{TaskName}Repeat\" /ru SYSTEM /sc minute /mo 3 /rl highest /tr \"\\\"{exe}\\\" --minimized\"");
                return create;
            }
            Run($"/delete /f /tn \"{TaskName}\"");
            Run($"/delete /f /tn \"{TaskName}Repeat\"");
            return 0;
        }
        catch { return 1; }
    }

    private static int Run(string args)
    {
        var psi = new ProcessStartInfo("schtasks.exe", args) { UseShellExecute = false, CreateNoWindow = true };
        using var p = Process.Start(psi);
        if (p is null) return 1;
        p.WaitForExit(15000);
        return p.ExitCode;
    }
}
