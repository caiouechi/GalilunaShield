using System.Security.Claims;
using GalilunaShield.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GalilunaShield.Web.Pages;

[AllowAnonymous]
public sealed class LoginModel : PageModel
{
    private readonly ParentData _data;
    public LoginModel(ParentData data) => _data = data;

    public string? Error { get; private set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync(string? pin)
    {
        _data.Reload();
        var expected = _data.Settings.ParentPin;

        // If no PIN is set on the computer, the dashboard cannot be secured. Refuse rather than run open.
        if (string.IsNullOrEmpty(expected))
        {
            Error = "No parent PIN is set. Open Galiluna Shield on the computer and set a parent PIN in Settings first.";
            return Page();
        }
        if (pin != expected)
        {
            Error = "Wrong PIN.";
            return Page();
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, "Parent"),
            new("pin", pin), // used server-side to decrypt encrypted evidence for viewing
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
        return Redirect("/");
    }
}
