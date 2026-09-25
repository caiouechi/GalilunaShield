using System.Text.RegularExpressions;
using GalilunaShield.Web.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GalilunaShield.Web.Pages;

public sealed class TranscriptModel : PageModel
{
    private readonly ParentData _data;
    public TranscriptModel(ParentData data) => _data = data;

    public DateOnly Day { get; private set; }
    public List<string> Lines { get; private set; } = new();
    public List<(string Time, string Source, string Text, bool Unclear)> Rows { get; private set; } = new();

    public void OnGet(string? day)
    {
        Day = DateOnly.TryParse(day, out var d) ? d : DateOnly.FromDateTime(DateTime.Now);
        Lines = _data.ReadTranscript(Day);
        foreach (var line in Lines)
        {
            // [HH:mm:ss] [Source] (marker) text
            var m = Regex.Match(line, @"^\[(\d\d:\d\d:\d\d)\] \[([^\]]+)\](\s*\(unclear\))?\s*(.*)$");
            if (m.Success)
            {
                Rows.Add((m.Groups[1].Value, m.Groups[2].Value, m.Groups[4].Value, m.Groups[3].Success));
            }
        }
    }
}
