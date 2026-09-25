using GalilunaShield.Web.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GalilunaShield.Web.Pages;

public sealed class ReportsModel : PageModel
{
    private readonly ParentData _data;
    public ReportsModel(ParentData data) => _data = data;

    public List<FileInfo> Files { get; private set; } = new();

    public void OnGet() => Files = _data.Reports();
}
