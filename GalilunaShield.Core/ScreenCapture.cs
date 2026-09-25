using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace GalilunaShield;

/// <summary>
/// Captures a single image spanning every monitor (the Windows "virtual screen"), so a parent sees exactly
/// what was on screen when a red flag happened, even with two or three monitors. Best-effort: returns null
/// if there is no desktop session (e.g. a service) or capture is blocked.
/// </summary>
[SupportedOSPlatform("windows")]
public static class ScreenCapture
{
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    private const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77, SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;

    [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();

    private static bool _dpiSet;

    /// <summary>Captures a JPEG of all monitors as raw bytes, or null on failure. The caller decides how to store it (encrypted or not).</summary>
    public static byte[]? CaptureAllScreens(int jpegQuality = 80)
    {
        try
        {
            if (!_dpiSet) { try { SetProcessDPIAware(); } catch { } _dpiSet = true; }

            int x = GetSystemMetrics(SM_XVIRTUALSCREEN);
            int y = GetSystemMetrics(SM_YVIRTUALSCREEN);
            int w = GetSystemMetrics(SM_CXVIRTUALSCREEN);
            int h = GetSystemMetrics(SM_CYVIRTUALSCREEN);
            if (w <= 0 || h <= 0) return null;

            using var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(x, y, 0, 0, new Size(w, h), CopyPixelOperation.SourceCopy);
            }

            var encoder = ImageCodecInfo.GetImageEncoders().First(e => e.MimeType == "image/jpeg");
            using var ep = new EncoderParameters(1);
            ep.Param[0] = new EncoderParameter(Encoder.Quality, (long)Math.Clamp(jpegQuality, 1, 100));
            using var ms = new MemoryStream();
            bmp.Save(ms, encoder, ep);
            return ms.ToArray();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>How many monitors are attached (for the report note).</summary>
    public static int MonitorCount()
    {
        try { return GetSystemMetrics(80 /* SM_CMONITORS */); } catch { return 1; }
    }
}
