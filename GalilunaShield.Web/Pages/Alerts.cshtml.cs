using GalilunaShield.Web.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GalilunaShield.Web.Pages;

public sealed class AlertsModel : PageModel
{
    private readonly ParentData _data;
    public AlertsModel(ParentData data) => _data = data;

    public List<AlertDto> Items { get; private set; } = new();
    public bool ShowDismissed { get; private set; }
    public int NewCount { get; private set; }
    public string ReturnUrl { get; private set; } = "/Alerts";

    public void OnGet(int? all)
    {
        ShowDismissed = all == 1;
        ReturnUrl = ShowDismissed ? "/Alerts?all=1" : "/Alerts";
        var alerts = _data.ReadAlerts(90);
        NewCount = alerts.Count(a => a.Status == AlertStatus.New);
        Items = ShowDismissed ? alerts : alerts.Where(a => a.Status != AlertStatus.Dismissed).ToList();
    }
}
