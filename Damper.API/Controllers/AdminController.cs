using Damper.API.Security;
using Damper.API.ViewModels.Admin;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Damper.API.Controllers;

[Route("admin")]
public sealed class AdminController(IAdminCredentialValidator credentialValidator) : Controller
{
    [AllowAnonymous]
    [HttpGet("login")]
    public IActionResult Login()
    {
        return View(new AdminLoginModel());
    }

    [AllowAnonymous]
    [HttpPost("login")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(AdminLoginModel model, CancellationToken cancellationToken)
    {
        var result = await credentialValidator.ValidateAsync(
            model.Username,
            model.Password,
            cancellationToken);

        if (result == null)
        {
            ModelState.AddModelError(string.Empty, "Invalid username or password.");
            return View(model);
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, result.Username),
            new(ClaimTypes.Name, result.Username)
        };

        claims.AddRange(result.Roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

        return RedirectToAction("Index", "Integrations");
    }

    [Authorize(Policy = "DamperAdmin")]
    [HttpPost("logout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        return RedirectToAction(nameof(Login));
    }
}