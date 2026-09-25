using System.Globalization;
using System.Net;
using System.Text;
using GalilunaShield.Alerts;

namespace GalilunaShield.Reports;

/// <summary>
/// Builds self-contained HTML reports (no internet, no scripts required) plus CSV exports from the alert
/// store, so a parent can review a day or a week at a glance and hand evidence to a school, counsellor or
/// the police. Audio is linked relative to the report so the folder can be copied as a whole.
/// </summary>
public sealed class ReportBuilder
{
    private readonly AlertStore _store;
    private readonly ActivityLog? _activity;
    private readonly bool _writeCsv;

    public ReportBuilder(AlertStore store, bool writeCsv = true) : this(store, null, writeCsv) { }

    public ReportBuilder(AlertStore store, ActivityLog? activity, bool writeCsv = true)
    {
        _store = store;
        _activity = activity;
        _writeCsv = writeCsv;
    }

    private IReadOnlyList<ActivityEvent> Events(DateOnly first, DateOnly last)
    {
        if (_activity is null) return Array.Empty<ActivityEvent>();
        var list = new List<ActivityEvent>();
        for (var d = first; d <= last; d = d.AddDays(1)) list.AddRange(_activity.Read(d));
        return list;
    }

    public string IndexPath => Path.Combine(_store.ReportsDirectory, "index.html");

    /// <summary>Report for one calendar day. Returns the HTML path.</summary>
    public string BuildDaily(DateOnly day)
    {
        var from = day.ToDateTime(TimeOnly.MinValue);
        var to = from.AddDays(1);
        var alerts = _store.ReadAlerts(new DateTimeOffset(from), new DateTimeOffset(to));
        var unclear = _store.ReadUnclear(day);
        var recordings = _store.ListRecordings(day);
        var transcriptLines = _store.ReadTranscript(day).Length();

        var path = Path.Combine(_store.ReportsDirectory, $"report-{day:yyyy-MM-dd}.html");
        File.WriteAllText(path, Render($"Daily report - {day:dddd, d MMMM yyyy}", from, to, alerts, unclear, recordings, transcriptLines, Events(day, day), hourly: true), Encoding.UTF8);
        if (_writeCsv) WriteCsv(Path.ChangeExtension(path, ".csv"), alerts);
        return path;
    }

    /// <summary>Report for the 7 days ending on <paramref name="lastDay"/> (inclusive).</summary>
    public string BuildWeekly(DateOnly lastDay)
    {
        var firstDay = lastDay.AddDays(-6);
        var from = firstDay.ToDateTime(TimeOnly.MinValue);
        var to = lastDay.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var alerts = _store.ReadAlerts(new DateTimeOffset(from), new DateTimeOffset(to));
        var unclear = new List<(DateTimeOffset At, string Source, string Note, string? WavPath)>();
        var recordings = new List<FileInfo>();
        var transcriptLines = 0;
        for (var d = firstDay; d <= lastDay; d = d.AddDays(1))
        {
            unclear.AddRange(_store.ReadUnclear(d));
            recordings.AddRange(_store.ListRecordings(d));
            transcriptLines += _store.ReadTranscript(d).Count;
        }

        var path = Path.Combine(_store.ReportsDirectory, $"week-{firstDay:yyyy-MM-dd}_to_{lastDay:yyyy-MM-dd}.html");
        File.WriteAllText(path, Render($"Weekly report - {firstDay:d MMM} to {lastDay:d MMM yyyy}", from, to, alerts, unclear, recordings, transcriptLines, Events(firstDay, lastDay), hourly: false), Encoding.UTF8);
        if (_writeCsv) WriteCsv(Path.ChangeExtension(path, ".csv"), alerts);
        return path;
    }

    /// <summary>Rebuilds today's daily report, this week's report and the index. Cheap enough to run after every alert.</summary>
    public string BuildCurrent()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var daily = BuildDaily(today);
        BuildWeekly(today);
        BuildIndex();
        return daily;
    }

    /// <summary>Landing page listing every report plus an all-time summary.</summary>
    public string BuildIndex()
    {
        var all = _store.ReadAlerts();
        var reports = new DirectoryInfo(_store.ReportsDirectory).GetFiles("*.html")
            .Where(f => !f.Name.Equals("index.html", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => f.Name).ToList();

        var sb = new StringBuilder();
        Head(sb, "Galiluna Shield - Reports");
        sb.Append("<header><div class=\"brand\">").Append(Logo()).Append("<div><h1>Galiluna Shield</h1><p class=\"sub\">Reports</p></div></div></header><main>");

        sb.Append("<section class=\"cards\">");
        Card(sb, all.Count.ToString(), "alerts, all time", "");
        Card(sb, all.Count(a => a.Severity == Severity.Critical).ToString(), "critical", "crit");
        Card(sb, all.Count(a => a.At >= DateTimeOffset.Now.AddDays(-7)).ToString(), "in the last 7 days", "");
        Card(sb, all.Count > 0 ? all.Max(a => a.At).ToString("d MMM HH:mm") : "-", "most recent alert", "");
        sb.Append("</section>");

        sb.Append("<section><h2>Reports</h2>");
        if (reports.Count == 0) sb.Append("<p class=\"muted\">No reports yet. A daily report is created automatically after the first alert.</p>");
        sb.Append("<ul class=\"reports\">");
        foreach (var f in reports)
        {
            var kind = f.Name.StartsWith("week-") ? "Weekly" : "Daily";
            var label = Path.GetFileNameWithoutExtension(f.Name).Replace("report-", "").Replace("week-", "").Replace("_to_", " to ");
            sb.Append($"<li><span class=\"tag\">{kind}</span> <a href=\"{H(f.Name)}\">{H(label)}</a> <span class=\"muted\">updated {f.LastWriteTime:d MMM HH:mm}</span></li>");
        }
        sb.Append("</ul></section>");

        if (all.Count > 0)
        {
            sb.Append("<section><h2>Alerts by category (all time)</h2>");
            CategoryTable(sb, all);
            sb.Append("</section>");
        }
        Footer(sb);
        File.WriteAllText(IndexPath, sb.ToString(), Encoding.UTF8);
        return IndexPath;
    }

    // ------------------------------------------------------------------------------------------------

    private string Render(string title, DateTime from, DateTime to, IReadOnlyList<AlertRecord> alerts,
        IReadOnlyList<(DateTimeOffset At, string Source, string Note, string? WavPath)> unclear,
        IReadOnlyList<FileInfo> recordings, int transcriptLines, IReadOnlyList<ActivityEvent> events, bool hourly)
    {
        var stops = events.Count(e => e.Kind == ActivityKind.MonitoringStopped);
        var unexpected = events.Count(e => e.Kind == ActivityKind.UnexpectedEnd);
        var adultVoices = alerts.Count(a => a.Matches.Any(m => m.Category == MonitorService.AdultVoiceCategory));
        var sb = new StringBuilder();
        Head(sb, $"Galiluna Shield - {title}");
        sb.Append("<header><div class=\"brand\">").Append(Logo())
          .Append($"<div><h1>{H(title)}</h1><p class=\"sub\">Generated {DateTime.Now:d MMMM yyyy HH:mm} &middot; <a href=\"index.html\">all reports</a></p></div></div></header><main>");

        // Summary cards
        var critical = alerts.Count(a => a.Severity == Severity.Critical);
        var high = alerts.Count(a => a.Severity == Severity.High);
        var possibleOnly = alerts.Count(a => a.PossibleOnly);
        sb.Append("<section class=\"cards\">");
        Card(sb, alerts.Count.ToString(), "alerts", alerts.Count > 0 ? "warn" : "ok");
        Card(sb, critical.ToString(), "critical", critical > 0 ? "crit" : "");
        Card(sb, high.ToString(), "high", high > 0 ? "high" : "");
        Card(sb, possibleOnly.ToString(), "possible matches only", "");
        Card(sb, unclear.Count.ToString(), "unclear clips to listen to", unclear.Count > 0 ? "warn" : "");
        Card(sb, recordings.Count.ToString(), "recordings", "");
        Card(sb, transcriptLines.ToString(), "transcribed utterances", "");
        if (_activity is not null)
        {
            Card(sb, adultVoices.ToString(), "adult-sounding voices", adultVoices > 0 ? "high" : "");
            Card(sb, stops.ToString(), "times monitoring was stopped", stops > 0 ? "warn" : "");
            Card(sb, unexpected.ToString(), "unexpected shutdowns", unexpected > 0 ? "crit" : "");
        }
        sb.Append("</section>");

        if (unexpected > 0 || stops > 0)
        {
            sb.Append($"<section class=\"callout warn\"><strong>The shield was interrupted.</strong> Monitoring was stopped {stops} time{(stops == 1 ? "" : "s")}" +
                      (unexpected > 0 ? $" and the program ended without closing properly {unexpected} time{(unexpected == 1 ? "" : "s")} (computer turned off, or the program was killed)" : "") +
                      ". See <a href=\"#events\">program events</a> below for exactly when.</section>");
        }

        if (alerts.Count == 0)
        {
            sb.Append("<section class=\"callout ok\"><strong>No red flags in this period.</strong> Monitoring ran normally; see the unclear clips below if you want to double-check what the recognizer could not make out.</section>");
        }
        else if (critical > 0)
        {
            sb.Append($"<section class=\"callout crit\"><strong>{critical} critical alert{(critical == 1 ? "" : "s")}.</strong> Listen to the clips below and, if the concern is confirmed, keep this folder as evidence. The SHA-256 fingerprint next to each clip proves the audio has not been altered since it was saved.</section>");
        }

        // Charts
        sb.Append("<section class=\"grid2\">");
        sb.Append("<div><h2>").Append(hourly ? "Alerts by hour" : "Alerts by day").Append("</h2>");
        if (hourly) HourBars(sb, alerts); else DayBars(sb, alerts, from, to);
        sb.Append("</div><div><h2>By category</h2>");
        CategoryTable(sb, alerts);
        sb.Append("</div></section>");

        // Sources
        if (alerts.Count > 0)
        {
            sb.Append("<section><h2>Where the words came from</h2><table><tr><th>Source</th><th>Alerts</th><th>Critical</th></tr>");
            foreach (var g in alerts.GroupBy(a => a.Source).OrderByDescending(g => g.Count()))
            {
                sb.Append($"<tr><td>{H(g.Key)}</td><td>{g.Count()}</td><td>{g.Count(a => a.Severity == Severity.Critical)}</td></tr>");
            }
            sb.Append("</table></section>");
        }

        // Timeline
        sb.Append("<section><h2>Alert timeline</h2>");
        foreach (var a in alerts.OrderBy(a => a.At))
        {
            var sev = a.PossibleOnly ? "possible" : a.Severity.ToString().ToLowerInvariant();
            sb.Append($"<article class=\"alert {sev}\" id=\"{H(a.Id)}\">");
            sb.Append($"<div class=\"alert-head\"><span class=\"badge {sev}\">{(a.PossibleOnly ? "POSSIBLE" : a.Severity.ToString().ToUpperInvariant())}</span>");
            sb.Append($"<time>{a.At:ddd d MMM HH:mm:ss}</time><span class=\"conf\">{a.Confidence:P0} confident</span></div>");
            sb.Append($"<p class=\"heard\">Heard on <b>{H(a.HeardWhere ?? a.Source)}</b></p>");
            sb.Append("<p class=\"said\">&ldquo;").Append(Highlight(a)).Append("&rdquo;</p>");
            sb.Append("<ul class=\"matches\">");
            foreach (var m in a.Matches)
            {
                var how = m.Confidence == "Exact" ? "" : $" <span class=\"muted\">(possible &ndash; heard as &ldquo;{H(m.HeardAs)}&rdquo;)</span>";
                sb.Append($"<li><b>{H(m.Word)}</b> <span class=\"cat\">{H(m.Category)}</span>{how}</li>");
            }
            sb.Append("</ul>");
            if (a.Context.Count > 0)
            {
                sb.Append("<details><summary>What was heard just before</summary><ul class=\"ctx\">");
                foreach (var c in a.Context) sb.Append($"<li><time>{c.At:HH:mm:ss}</time> <span class=\"src\">{H(c.Source)}</span> {H(c.Text)}</li>");
                sb.Append("</ul></details>");
            }
            if (a.ClipPath is not null && File.Exists(a.ClipPath))
            {
                if (Embeddable(a.ClipPath))
                {
                    sb.Append($"<div class=\"audio\"><audio controls preload=\"none\" src=\"{Rel(a.ClipPath)}\"></audio>");
                    sb.Append($"<div class=\"files\"><a href=\"{Rel(a.ClipPath)}\">{H(Path.GetFileName(a.ClipPath))}</a>");
                    if (a.DetailsPath is not null) sb.Append($" &middot; <a href=\"{Rel(a.DetailsPath)}\">details</a>");
                    if (a.ClipSha256 is not null) sb.Append($"<br><span class=\"hash\">SHA-256 {H(a.ClipSha256)}</span>");
                    sb.Append("</div></div>");
                }
                else
                {
                    sb.Append("<div class=\"locked\">&#128274; Audio clip is encrypted. Open it in the Galiluna Shield app (parent PIN) to listen.");
                    if (a.ClipSha256 is not null) sb.Append($"<br><span class=\"hash\">SHA-256 {H(a.ClipSha256)}</span>");
                    sb.Append("</div>");
                }
            }
            if (a.ScreenshotPath is not null && File.Exists(a.ScreenshotPath))
            {
                if (Embeddable(a.ScreenshotPath))
                {
                    sb.Append($"<a class=\"shot\" href=\"{Rel(a.ScreenshotPath)}\" title=\"What was on screen (all monitors)\">" +
                              $"<img loading=\"lazy\" src=\"{Rel(a.ScreenshotPath)}\" alt=\"Screen at the time of the alert\" /></a>");
                    sb.Append("<div class=\"muted small\">Screen capture of all monitors at this moment. Click to enlarge.</div>");
                }
                else
                {
                    sb.Append("<div class=\"locked\">&#128274; Screenshot of all monitors is encrypted. Open it in the Galiluna Shield app to view.</div>");
                }
            }
            if (a.Recordings.Count > 0)
            {
                sb.Append("<div class=\"rec\">Inside full recording: ");
                sb.Append(string.Join(", ", a.Recordings.Select(r => $"<a href=\"{Rel(r)}\">{H(Path.GetFileName(r))}</a>")));
                sb.Append("</div>");
            }
            sb.Append("</article>");
        }
        if (alerts.Count == 0) sb.Append("<p class=\"muted\">Nothing to show.</p>");
        sb.Append("</section>");

        // Unclear
        sb.Append("<section><h2>Unclear speech</h2><p class=\"muted\">Speech-like sound the recognizer was not confident about or could not transcribe. Worth a listen if you have a specific worry.</p>");
        if (unclear.Count == 0) sb.Append("<p class=\"muted\">None.</p>");
        else
        {
            sb.Append("<table><tr><th>Time</th><th>Source</th><th>Recognizer said</th><th>Listen</th></tr>");
            foreach (var u in unclear.OrderBy(u => u.At))
            {
                var audio = u.WavPath is not null && File.Exists(u.WavPath)
                    ? (Embeddable(u.WavPath) ? $"<audio controls preload=\"none\" src=\"{Rel(u.WavPath)}\"></audio>" : "<span class=\"muted\">&#128274; encrypted</span>")
                    : "<span class=\"muted\">not saved</span>";
                sb.Append($"<tr><td>{u.At:ddd HH:mm:ss}</td><td>{H(u.Source)}</td><td>{H(u.Note)}</td><td>{audio}</td></tr>");
            }
            sb.Append("</table>");
        }
        sb.Append("</section>");

        // Program events
        if (_activity is not null)
        {
            sb.Append("<section id=\"events\"><h2>Program events</h2><p class=\"muted\">When the shield was started, stopped, or interrupted. A child switching it off shows up here.</p>");
            if (events.Count == 0) sb.Append("<p class=\"muted\">None recorded.</p>");
            else
            {
                sb.Append("<table><tr><th>Time</th><th>Event</th><th>Details</th></tr>");
                foreach (var e in events.OrderBy(e => e.At))
                {
                    var cls = e.Kind switch
                    {
                        ActivityKind.UnexpectedEnd => "critical",
                        ActivityKind.MonitoringStopped or ActivityKind.AppExited or ActivityKind.WindowsShutdown => "medium",
                        ActivityKind.MonitoringStarted or ActivityKind.AppStarted => "ok",
                        _ => "low",
                    };
                    var label = e.Kind switch
                    {
                        ActivityKind.AppStarted => "App started",
                        ActivityKind.AppExited => "App exited",
                        ActivityKind.MonitoringStarted => "Monitoring on",
                        ActivityKind.MonitoringStopped => "Monitoring OFF",
                        ActivityKind.WindowsShutdown => "Windows shutdown",
                        ActivityKind.UnexpectedEnd => "Unexpected end",
                        ActivityKind.SettingsChanged => "Settings changed",
                        ActivityKind.RecordingStarted => "Recording on",
                        ActivityKind.RecordingStopped => "Recording off",
                        _ => e.Kind.ToString(),
                    };
                    sb.Append($"<tr><td>{e.At:ddd HH:mm:ss}</td><td><span class=\"badge {cls}\">{H(label)}</span></td><td>{H(e.Message)}</td></tr>");
                }
                sb.Append("</table>");
            }
            sb.Append("</section>");
        }

        // Recordings
        sb.Append("<section><h2>Recordings</h2>");
        if (recordings.Count == 0) sb.Append("<p class=\"muted\">None in this period.</p>");
        else
        {
            sb.Append("<table><tr><th>File</th><th>Length</th><th>Size</th></tr>");
            foreach (var r in recordings)
            {
                var seconds = Math.Max(0, (r.Length - 44) / 32000.0);
                sb.Append($"<tr><td><a href=\"{Rel(r.FullName)}\">{H(r.Name)}</a></td><td>{TimeSpan.FromSeconds(seconds):hh\\:mm\\:ss}</td><td>{r.Length / 1048576.0:F1} MB</td></tr>");
            }
            sb.Append("</table>");
        }
        sb.Append("</section>");

        Footer(sb);
        return sb.ToString();
    }

    private static void HourBars(StringBuilder sb, IReadOnlyList<AlertRecord> alerts)
    {
        var counts = new int[24];
        foreach (var a in alerts) counts[a.At.Hour]++;
        var max = Math.Max(1, counts.Max());
        sb.Append("<div class=\"bars\">");
        for (var h = 0; h < 24; h++)
        {
            var pct = counts[h] * 100 / max;
            sb.Append($"<div class=\"bar\" title=\"{h:00}:00 - {counts[h]} alert(s)\"><div class=\"fill\" style=\"height:{pct}%\"></div><span>{h:00}</span></div>");
        }
        sb.Append("</div>");
    }

    private static void DayBars(StringBuilder sb, IReadOnlyList<AlertRecord> alerts, DateTime from, DateTime to)
    {
        var days = new List<(DateTime Day, int Count)>();
        for (var d = from; d < to; d = d.AddDays(1)) days.Add((d, alerts.Count(a => a.At.Date == d.Date)));
        var max = Math.Max(1, days.Max(x => x.Count));
        sb.Append("<div class=\"bars wide\">");
        foreach (var (day, count) in days)
        {
            sb.Append($"<div class=\"bar\" title=\"{day:d MMM} - {count} alert(s)\"><div class=\"fill\" style=\"height:{count * 100 / max}%\"></div><span>{day:ddd}</span></div>");
        }
        sb.Append("</div>");
    }

    private static void CategoryTable(StringBuilder sb, IReadOnlyList<AlertRecord> alerts)
    {
        var rows = alerts.SelectMany(a => a.Matches.Select(m => (m.Category, m.Severity)))
            .GroupBy(x => x.Category)
            .Select(g => (Category: g.Key, Severity: g.Max(x => x.Severity), Count: g.Count()))
            .OrderByDescending(x => x.Severity).ThenByDescending(x => x.Count).ToList();
        if (rows.Count == 0) { sb.Append("<p class=\"muted\">None.</p>"); return; }
        sb.Append("<table><tr><th>Category</th><th>Severity</th><th>Matches</th></tr>");
        foreach (var r in rows)
        {
            var sev = r.Severity.ToString().ToLowerInvariant();
            sb.Append($"<tr><td>{H(r.Category)}</td><td><span class=\"badge {sev}\">{r.Severity}</span></td><td>{r.Count}</td></tr>");
        }
        sb.Append("</table>");
    }

    /// <summary>The flagged sentence with matched words wrapped in &lt;mark&gt;.</summary>
    private static string Highlight(AlertRecord a)
    {
        var text = a.Text;
        var spans = new List<(int Start, int Len)>();
        foreach (var m in a.Matches)
        {
            var needle = m.HeardAs.Length > 0 ? m.HeardAs : m.Word;
            var idx = text.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                // heardAs is normalized (no apostrophes/punctuation); try a loose search word by word
                var first = needle.Split(' ')[0];
                idx = text.IndexOf(first, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) continue;
                needle = first;
            }
            spans.Add((idx, needle.Length));
        }
        var sb = new StringBuilder();
        var pos = 0;
        foreach (var (start, len) in spans.OrderBy(s => s.Start))
        {
            if (start < pos) continue;
            sb.Append(H(text[pos..start])).Append("<mark>").Append(H(text.Substring(start, len))).Append("</mark>");
            pos = start + len;
        }
        sb.Append(H(text[pos..]));
        return sb.ToString();
    }

    private void WriteCsv(string path, IReadOnlyList<AlertRecord> alerts)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Time,Severity,Source,Confidence,PossibleOnly,Words,Categories,Text,Clip,ClipSHA256,Recordings");
        foreach (var a in alerts.OrderBy(a => a.At))
        {
            sb.AppendLine(string.Join(",",
                Csv(a.At.ToString("yyyy-MM-dd HH:mm:ss")),
                Csv(a.Severity.ToString()),
                Csv(a.Source),
                Csv(a.Confidence.ToString("P0", CultureInfo.InvariantCulture)),
                Csv(a.PossibleOnly ? "yes" : "no"),
                Csv(string.Join("; ", a.Matches.Select(m => m.Word))),
                Csv(string.Join("; ", a.Matches.Select(m => m.Category).Distinct())),
                Csv(a.Text),
                Csv(a.ClipPath ?? ""),
                Csv(a.ClipSha256 ?? ""),
                Csv(string.Join("; ", a.Recordings))));
        }
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
    }

    private static string Csv(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";

    /// <summary>Path relative to the reports folder, URL-encoded for href.</summary>
    private string Rel(string absolute)
    {
        var rel = Path.GetRelativePath(_store.ReportsDirectory, absolute).Replace('\\', '/');
        return string.Join("/", rel.Split('/').Select(Uri.EscapeDataString));
    }

    /// <summary>A media file can be embedded in the browser only if it exists and is not encrypted.</summary>
    private static bool Embeddable(string path) => File.Exists(path) && !EvidenceProtector.IsEncrypted(path);

    private static string H(string s) => WebUtility.HtmlEncode(s);

    private static void Card(StringBuilder sb, string value, string label, string cls) =>
        sb.Append($"<div class=\"card {cls}\"><div class=\"v\">{H(value)}</div><div class=\"l\">{H(label)}</div></div>");

    private static string Logo() => $"<img class=\"logo\" src=\"{ReportLogo.DataUri}\" alt=\"\" aria-hidden=\"true\" />";

    private static void Head(StringBuilder sb, string title)
    {
        sb.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        sb.Append($"<title>{H(title)}</title><style>{Css}</style></head><body>");
    }

    private static void Footer(StringBuilder sb) =>
        sb.Append("</main><footer>Galiluna Shield &middot; All processing happened on this computer; nothing was uploaded. " +
                  "Audio files are linked relative to this report, so copy the whole <b>Galiluna Shield</b> folder to keep the links working.</footer></body></html>");

    private const string Css = """
        :root{--bg:#f4f6fb;--card:#fff;--ink:#1b1533;--muted:#6b7390;--line:#e3e7f0;--brand:#7c3aed;--crit:#d7263d;--high:#f2711c;--med:#e0a800;--low:#6b7390;--ok:#2e9e5b;--poss:#c026d3}
        @media (prefers-color-scheme:dark){:root{--bg:#0e0a29;--card:#1a1440;--ink:#eef1fb;--muted:#9aa3c7;--line:#2a3158}}
        *{box-sizing:border-box}body{margin:0;font:15px/1.5 system-ui,Segoe UI,Roboto,sans-serif;background:var(--bg);color:var(--ink)}
        header{background:linear-gradient(135deg,#241263,#7c3aed);color:#fff;padding:28px 32px}.brand{display:flex;gap:16px;align-items:center;max-width:1100px;margin:auto}
        .logo{width:56px;height:56px;flex:none}h1{margin:0;font-size:26px}.sub{margin:2px 0 0;opacity:.85}.sub a{color:#fff}
        main{max-width:1100px;margin:auto;padding:24px 32px}h2{font-size:18px;margin:28px 0 12px}
        .cards{display:grid;grid-template-columns:repeat(auto-fit,minmax(140px,1fr));gap:12px}.card{background:var(--card);border:1px solid var(--line);border-radius:12px;padding:14px 16px}
        .card .v{font-size:28px;font-weight:700}.card .l{color:var(--muted);font-size:13px}.card.crit .v{color:var(--crit)}.card.high .v{color:var(--high)}.card.warn .v{color:var(--med)}.card.ok .v{color:var(--ok)}
        .callout{margin-top:18px;padding:14px 18px;border-radius:12px;border-left:6px solid var(--ok);background:var(--card)}.callout.crit{border-color:var(--crit)}.callout.warn{border-color:var(--med)}.badge.ok{background:var(--ok)}
        .grid2{display:grid;grid-template-columns:1fr 1fr;gap:24px}@media(max-width:800px){.grid2{grid-template-columns:1fr}}
        table{width:100%;border-collapse:collapse;background:var(--card);border:1px solid var(--line);border-radius:12px;overflow:hidden}th,td{padding:8px 12px;text-align:left;border-bottom:1px solid var(--line);vertical-align:middle}th{font-size:12px;text-transform:uppercase;color:var(--muted)}
        .bars{display:flex;gap:4px;align-items:flex-end;height:140px;background:var(--card);border:1px solid var(--line);border-radius:12px;padding:12px 10px 6px}.bar{flex:1;display:flex;flex-direction:column;justify-content:flex-end;align-items:center;height:100%}
        .bar .fill{width:100%;background:var(--brand);border-radius:3px 3px 0 0;min-height:2px}.bar span{font-size:10px;color:var(--muted);margin-top:4px}.bars.wide .bar span{font-size:12px}
        .badge{display:inline-block;padding:2px 8px;border-radius:999px;font-size:11px;font-weight:700;color:#fff;background:var(--low)}.badge.critical{background:var(--crit)}.badge.high{background:var(--high)}.badge.medium{background:var(--med)}.badge.possible{background:var(--poss)}
        .alert{background:var(--card);border:1px solid var(--line);border-left:6px solid var(--low);border-radius:12px;padding:14px 18px;margin:12px 0}.alert.critical{border-left-color:var(--crit)}.alert.high{border-left-color:var(--high)}.alert.medium{border-left-color:var(--med)}.alert.possible{border-left-color:var(--poss)}
        .alert-head{display:flex;gap:12px;align-items:center;flex-wrap:wrap;color:var(--muted);font-size:13px}.said{font-size:17px;margin:10px 0}mark{background:#ffe08a;color:#1b1533;padding:0 3px;border-radius:3px}
        .matches{margin:0;padding-left:18px}.cat{color:var(--muted);font-size:12px;margin-left:6px}.ctx{padding-left:18px;color:var(--muted)}.ctx time,.src{font-family:ui-monospace,Consolas,monospace;font-size:12px}
        .audio{display:flex;gap:14px;align-items:center;flex-wrap:wrap;margin-top:10px}audio{height:36px}.files{font-size:13px}.hash{font-family:ui-monospace,Consolas,monospace;font-size:11px;color:var(--muted)}
        .rec{font-size:13px;color:var(--muted);margin-top:8px}.muted{color:var(--muted)}.small{font-size:12px}.locked{margin-top:10px;padding:8px 12px;border-radius:8px;background:var(--bg);border:1px dashed var(--line);font-size:13px;color:var(--muted)}.heard{margin:2px 0 0;font-size:13px}.shot{display:inline-block;margin-top:10px;max-width:420px}.shot img{width:100%;border-radius:8px;border:1px solid var(--line)}.reports{list-style:none;padding:0}.reports li{padding:8px 0;border-bottom:1px solid var(--line)}
        .tag{display:inline-block;font-size:11px;font-weight:700;padding:2px 8px;border-radius:999px;background:var(--brand);color:#fff;margin-right:6px}footer{max-width:1100px;margin:32px auto;padding:0 32px 32px;color:var(--muted);font-size:13px}
        a{color:var(--brand)}details summary{cursor:pointer;color:var(--muted);font-size:13px}
        """;
}

file static class Extensions
{
    public static int Length(this IReadOnlyList<string> list) => list.Count;
}
