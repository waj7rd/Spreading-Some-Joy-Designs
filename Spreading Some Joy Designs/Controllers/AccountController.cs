using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SpreadingJoy.ViewModels;

namespace SpreadingJoy.Controllers;

public class AccountController : Controller
{
    private readonly IUserLogic _userLogic;

    public AccountController(IUserLogic userLogic)
    {
        _userLogic = userLogic;
    }

    // GET /Account/Login
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    // POST /Account/Login
    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var result = await _userLogic.AuthenticateAsync(
            model.Email, model.Password, HttpContext.Connection.RemoteIpAddress?.ToString());

        if (!result.Succeeded)
        {
            // Every failure reads the same to the visitor except lockout, which
            // they need to be told about or they'll keep trying. Note that a
            // deactivated account is reported as bad credentials — saying
            // "that account is disabled" confirms the address exists.
            model.ErrorMessage = result.Outcome switch
            {
                AuthenticationOutcome.LockedOut =>
                    "Too many failed attempts. Try again in a few minutes.",
                _ => "Email or password wasn't right."
            };

            return View(model);
        }

        var user = result.User!;

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new(ClaimTypes.Name, user.FullName),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role)
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

        // IsLocalUrl or nothing. A returnUrl straight off the query string is
        // how a real, correctly-signed-in session gets handed to somebody
        // else's site.
        if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
            return Redirect(model.ReturnUrl);

        return RedirectToAction("Board", "Orders");
    }

    // POST /Account/Logout
    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        var userId = CurrentUserId();
        var email = User.FindFirstValue(ClaimTypes.Email) ?? string.Empty;

        if (userId != null)
            await _userLogic.RecordLogoutAsync(userId.Value, email, HttpContext.Connection.RemoteIpAddress?.ToString());

        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        return RedirectToAction("Index", "Home");
    }

    // GET /Account/Denied
    [Authorize]
    public IActionResult Denied() => View();

    // GET /Account/ChangePassword
    [Authorize]
    public IActionResult ChangePassword()
    {
        return View(new ChangePasswordViewModel
        {
            SuccessMessage = TempData["PasswordSuccess"] as string
        });
    }

    // POST /Account/ChangePassword
    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var userId = CurrentUserId();
        if (userId == null)
            return Forbid();

        var result = await _userLogic.ChangeOwnPasswordAsync(
            userId.Value, model.CurrentPassword, model.NewPassword,
            HttpContext.Connection.RemoteIpAddress?.ToString());

        if (!result.Success)
        {
            model.ErrorMessage = result.ErrorMessage;
            return View(model);
        }

        TempData["PasswordSuccess"] = "Password changed.";
        return RedirectToAction(nameof(ChangePassword));
    }

    private int? CurrentUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(raw, out var id) ? id : null;
    }
}
