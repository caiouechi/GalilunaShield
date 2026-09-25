using GalilunaShield.Web.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GalilunaShield.Web.Pages;

public sealed class IndexModel : PageModel
{
    private readonly ParentData _data;
    public IndexModel(ParentData data) => _data = data;

    public int Today { get; private set; }
    public int Week { get; private set; }
    public int CriticalWeek { get; private set; }
    public int NewCount { get; private set; }
    public bool Running { get; private set; }
    public List<AlertDto> Latest { get; private set; } = new();
    public List<(string Label, int Count, int Pct)> DayBars { get; private set; } = new();
    public List<(string Category, Severity Severity, int Count)> Categories { get; private set; } = new();

    public void OnGet()
    {
        var alerts = _data.ReadAlerts(60);
        var today = DateTime.Today;
        var weekAgo = DateTimeOffset.Now.AddDays(-7);

        Today = alerts.Count(a => a.At.Date == today && a.Status != AlertStatus.Dismissed);
        Week = alerts.Count(a => a.At >= weekAgo);
        CriticalWeek = alerts.Count(a => a.At >= weekAgo && a.Severity == Severity.Critical);
        NewCount = alerts.Count(a => a.Status == AlertStatus.New);
        Latest = alerts.Where(a => a.Status != AlertStatus.Dismissed).Take(5).ToList();
        Running = IsMonitoringLive();

        // 7-day bar chart
        var counts = new List<(string, int)>();
        for (var i = 6; i >= 0; i--)
        {
            var d = today.AddDays(-i);
            counts.Add((d.ToString("ddd"), alerts.Count(a => a.At.Date == d)));
        }
        var max = Math.Max(1, counts.Max(c => c.Item2));
        DayBars = counts.Select(c => (c.Item1, c.Item2, c.Item2 * 100 / max)).ToList();

        Categories = alerts.Where(a => a.At >= weekAgo)
            .SelectMany(a => a.Matches.Select(m => (m.Category, m.Severity)))
            .GroupBy(x => x.Category)
            .Select(g => (g.Key, g.Max(x => x.Severity), g.Count()))
            .OrderByDescending(x => x.Item2).ThenByDescending(x => x.Item3)
            .Take(6).ToList();
    }

    /// <summary>Heuristic: monitoring is "live" if the running marker was touched in the last ~3 minutes.</summary>
    private bool IsMonitoringLive()
    {
        try
        {
            var marker = Path.Combine(_data.EventsDir, "running.marker");
            return System.IO.File.Exists(marker) && DateTime.Now - System.IO.File.GetLastWriteTime(marker) < TimeSpan.FromMinutes(3);
        }
        catch { return false; }
    }
}
