using System.Security.Claims;
using GalilunaShield.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<ParentData>();
builder.Services.AddRazorPages(o => o.Conventions.AuthorizeFolder("/"));
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/Login";
        o.LogoutPath = "/Logout";
        o.AccessDeniedPath = "/Login";
        o.ExpireTimeSpan = TimeSpan.FromHours(12);
        o.SlidingExpiration = true;
        o.Cookie.Name = "GalilunaShield.Parent";
    });
builder.Services.AddAuthorization();

// Bind to the LAN so the parent's phone can reach it. Port from the shared settings, overridable by --port.
var settings = ParentSettings.Load();
var port = settings.Web.Port <= 0 ? 8787 : settings.Web.Port;
var portArg = args.SkipWhile(a => !a.Equals("--port", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();
if (portArg is not null && int.TryParse(portArg, out var p)) port = p;
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

var app = builder.Build();

app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.MapRazorPages();

// ---- media (decrypted on the fly, parent-authenticated) ----
app.MapGet("/media/clip/{id}", (string id, HttpContext ctx, ParentData data) => ServeEvidence(id, ctx, data, screenshot: false))
    .RequireAuthorization();
app.MapGet("/media/shot/{id}", (string id, HttpContext ctx, ParentData data) => ServeEvidence(id, ctx, data, screenshot: true))
    .RequireAuthorization();

static IResult ServeEvidence(string id, HttpContext ctx, ParentData data, bool screenshot)
{
    var alert = data.FindAlert(id);
    var path = screenshot ? alert?.ScreenshotPath : alert?.ClipPath;
    if (path is null || !File.Exists(path)) return Results.NotFound();
    var pin = ctx.User.FindFirstValue("pin");
    var bytes = data.ReadEvidence(path, pin);
    if (bytes is null) return Results.StatusCode(423); // locked (wrong PIN / other account)
    return Results.File(bytes, ParentData.ContentType(path));
}

// ---- report html (summary view; embedded media links resolve only on the PC) ----
app.MapGet("/report/{name}", (string name, ParentData data) =>
{
    if (name.Contains("..") || name.Contains('/') || name.Contains('\\')) return Results.BadRequest();
    var path = Path.Combine(data.ReportsDir, name);
    return File.Exists(path) ? Results.Content(File.ReadAllText(path), "text/html") : Results.NotFound();
}).RequireAuthorization();

// ---- triage action ----
app.MapPost("/action/status", async (HttpContext ctx, ParentData data) =>
{
    var form = await ctx.Request.ReadFormAsync();
    var id = form["id"].ToString();
    if (Enum.TryParse<AlertStatus>(form["status"], true, out var status) && !string.IsNullOrEmpty(id))
    {
        data.SetStatus(id, status);
    }
    return Results.Redirect(form["return"].ToString() is { Length: > 0 } r ? r : "/Alerts");
}).RequireAuthorization();

app.Run();
