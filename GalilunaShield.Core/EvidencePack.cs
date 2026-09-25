using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using GalilunaShield.Alerts;

namespace GalilunaShield;

/// <summary>
/// Exports a self-contained evidence pack (a ZIP) for a date range: the alert details, clips, screenshots,
/// transcripts, reports and the program-event log, plus a manifest listing every file with its SHA-256 so a
/// school, counsellor or the police can verify nothing was altered. Encrypted evidence is decrypted into the
/// pack (with the parent's authority), so the recipient can open it.
/// </summary>
public sealed class EvidencePack
{
    private readonly AlertStore _store;
    private readonly ActivityLog? _activity;
    private readonly EvidenceProtector? _protector;

    public EvidencePack(AlertStore store, ActivityLog? activity = null, EvidenceProtector? protector = null)
    {
        _store = store;
        _activity = activity;
        _protector = protector;
    }

    /// <summary>Builds the pack for [from, to). Returns the ZIP path.</summary>
    public string Build(string destinationZip, DateOnly from, DateOnly to)
    {
        var fromDt = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue));
        var toDt = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue));
        var alerts = _store.ReadAlerts(fromDt, toDt);
        var manifest = new StringBuilder();
        manifest.AppendLine("Galiluna Shield - evidence pack");
        manifest.AppendLine($"Generated: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        manifest.AppendLine($"Period: {from:yyyy-MM-dd} to {to:yyyy-MM-dd}");
        manifest.AppendLine($"Computer: {SafeMachine()}   User: {SafeUser()}");
        manifest.AppendLine($"Alerts in period: {alerts.Count}");
        manifest.AppendLine();
        manifest.AppendLine("Every file below is listed with its SHA-256 fingerprint. If a file is changed, its");
        manifest.AppendLine("fingerprint changes, so this manifest proves the contents are exactly as exported.");
        manifest.AppendLine(new string('-', 72));

        Directory.CreateDirectory(Path.GetDirectoryName(destinationZip)!);
        if (File.Exists(destinationZip)) File.Delete(destinationZip);

        using (var zip = ZipFile.Open(destinationZip, ZipArchiveMode.Create))
        {
            void Add(string entryPath, byte[] bytes)
            {
                var entry = zip.CreateEntry(entryPath, CompressionLevel.Optimal);
                using var s = entry.Open();
                s.Write(bytes, 0, bytes.Length);
                manifest.AppendLine($"{Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()}  {entryPath}");
            }

            byte[] Read(string path) => _protector is not null && EvidenceProtector.IsEncrypted(path) ? _protector.ReadBytes(path) : File.ReadAllBytes(path);
            string Plain(string path) => EvidenceProtector.IsEncrypted(path) ? Path.GetFileNameWithoutExtension(path) : Path.GetFileName(path);

            foreach (var a in alerts.OrderBy(a => a.At))
            {
                if (a.DetailsPath is not null && File.Exists(a.DetailsPath)) Add($"alerts/{Path.GetFileName(a.DetailsPath)}", File.ReadAllBytes(a.DetailsPath));
                if (a.ClipPath is not null && File.Exists(a.ClipPath)) Add($"clips/{Plain(a.ClipPath)}", Read(a.ClipPath));
                if (a.ScreenshotPath is not null && File.Exists(a.ScreenshotPath)) Add($"screenshots/{Plain(a.ScreenshotPath)}", Read(a.ScreenshotPath));
            }

            for (var d = from; d <= to; d = d.AddDays(1))
            {
                var tr = Path.Combine(_store.TranscriptsDirectory, $"{d:yyyy-MM-dd}.log");
                if (File.Exists(tr)) Add($"transcripts/{Path.GetFileName(tr)}", File.ReadAllBytes(tr));

                var report = Path.Combine(_store.ReportsDirectory, $"report-{d:yyyy-MM-dd}.html");
                if (File.Exists(report)) Add($"reports/{Path.GetFileName(report)}", File.ReadAllBytes(report));

                if (_activity is not null)
                {
                    var ev = _activity.Read(d);
                    if (ev.Count > 0)
                    {
                        var text = string.Join(Environment.NewLine, ev.Select(e => $"[{e.At:yyyy-MM-dd HH:mm:ss}] {e.Kind} | {e.Message}"));
                        Add($"events/{d:yyyy-MM-dd}.log", Encoding.UTF8.GetBytes(text));
                    }
                }
            }

            // The alert log (machine-readable) for the period
            var jsonl = string.Join(Environment.NewLine, alerts.Select(a => System.Text.Json.JsonSerializer.Serialize(a)));
            Add("alerts.jsonl", Encoding.UTF8.GetBytes(jsonl));

            // Manifest last so it lists everything above
            var manifestEntry = zip.CreateEntry("MANIFEST.txt", CompressionLevel.Optimal);
            using var ms = manifestEntry.Open();
            var mb = Encoding.UTF8.GetBytes(manifest.ToString());
            ms.Write(mb, 0, mb.Length);
        }

        return destinationZip;
    }

    private static string SafeMachine() { try { return Environment.MachineName; } catch { return "unknown"; } }
    private static string SafeUser() { try { return Environment.UserName; } catch { return "unknown"; } }
}
