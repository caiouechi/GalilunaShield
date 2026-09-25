using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace GalilunaShield.App;

/// <summary>
/// Starts and stops the local parent web dashboard (GalilunaShield.Web) as a child process, and works out the
/// address a parent types on their phone. The dashboard reads the same settings file, so it needs no arguments
/// beyond the port.
/// </summary>
public sealed class WebDashboard : IDisposable
{
    private Process? _process;

    public bool IsRunning => _process is { HasExited: false };

    /// <summary>Path to the published web app, next to the desktop app under \web, or null if not installed.</summary>
    public static string? ExecutablePath
    {
        get
        {
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "web", "GalilunaShield.Web.exe"),
                Path.Combine(AppContext.BaseDirectory, "GalilunaShield.Web.exe"),
            };
            return candidates.FirstOrDefault(File.Exists);
        }
    }

    public static bool IsInstalled => ExecutablePath is not null;

    public bool Start(int port)
    {
        if (IsRunning) return true;
        var exe = ExecutablePath;
        if (exe is null) return false;
        try
        {
            // Redirecting all three standard streams means Windows does not attach a console to the child, so
            // no conhost.exe is created for the dashboard. We drain stdout/stderr so their pipe buffers never
            // fill (which would otherwise block the web server once it has logged enough).
            _process = Process.Start(new ProcessStartInfo(exe, $"--port {port}")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                WorkingDirectory = Path.GetDirectoryName(exe)!,
            });
            if (_process is null) return false;
            _process.OutputDataReceived += (_, _) => { };
            _process.ErrorDataReceived += (_, _) => { };
            try { _process.BeginOutputReadLine(); _process.BeginErrorReadLine(); } catch { }
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Stop()
    {
        try { if (IsRunning) _process!.Kill(entireProcessTree: true); } catch { }
        _process = null;
    }

    /// <summary>The address the parent opens on their phone, using this computer's LAN IP.</summary>
    public static string Url(int port) => $"http://{LocalIPv4()}:{port}";

    public static string LocalIPv4()
    {
        try
        {
            var best = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                            n.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address))
                .Select(a => a.Address.ToString())
                .FirstOrDefault();
            return best ?? "localhost";
        }
        catch
        {
            return "localhost";
        }
    }

    public void Dispose() => Stop();
}
