using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SpreadingJoy.ViewModels;

namespace SpreadingJoy.Controllers;

// Admin only, throughout. Managing who has an account is the one thing a
// manager can't do.
[Authorize(Policy = Policies.ManageStaff)]
public class StaffController : Controller
{
    private readonly IUserLogic _userLogic;
    private readonly IStudioClock _clock;

    public StaffController(IUserLogic userLogic, IStudioClock clock)
    {
        _userLogic = userLogic;
        _clock = clock;
    }

    // GET /Staff
    public async Task<IActionResult> Index()
    {
        var staff = await _userLogic.GetStaffAsync();
        var now = _clock.UtcNow;

        return View(new StaffListViewModel
        {
            SuccessMessage = TempData["StaffSuccess"] as string,
            ErrorMessage = TempData["StaffError"] as string,
            Staff = staff.Select(u => new StaffRowViewModel
            {
                Id = u.UserId,
                FullName = u.FullName,
                Email = u.Email,
                Role = u.Role,
                IsActive = u.IsActive,
                IsLockedOut = u.LockedOutUntil.HasValue && u.LockedOutUntil.Value > now,
                LastLoginAt = u.LastLoginAt
            }).ToList()
        });
    }

    // GET /Staff/Create
    public IActionResult Create() => View(new CreateStaffViewModel());

    // POST /Staff/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateStaffViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var result = await _userLogic.CreateStaffAsync(model.FullName, model.Email, model.Role, model.Password);

        if (!result.Success)
        {
            model.ErrorMessage = result.ErrorMessage;
            return View(model);
        }

        TempData["StaffSuccess"] = $"Added {model.FullName.Trim()}.";
        return RedirectToAction(nameof(Index));
    }

    // GET /Staff/Edit/{id}
    public async Task<IActionResult> Edit(int id)
    {
        var user = await _userLogic.GetByIdAsync(id);
        if (user == null)
            return NotFound();

        return View(new EditStaffViewModel
        {
            UserId = user.UserId,
            FullName = user.FullName,
            Email = user.Email,
            Role = user.Role
        });
    }

    // POST /Staff/Edit
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(EditStaffViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var result = await _userLogic.UpdateStaffAsync(model.UserId, model.FullName, model.Email, model.Role);

        if (!result.Success)
        {
            model.ErrorMessage = result.ErrorMessage;
            return View(model);
        }

        TempData["StaffSuccess"] = $"Updated {model.FullName.Trim()}.";
        return RedirectToAction(nameof(Index));
    }

    // POST /Staff/SetActive
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetActive(int id, bool isActive)
    {
        var result = await _userLogic.SetActiveAsync(id, isActive);

        if (!result.Success)
            TempData["StaffError"] = result.ErrorMessage;
        else
            TempData["StaffSuccess"] = isActive ? "Account reactivated." : "Account deactivated.";

        return RedirectToAction(nameof(Index));
    }

    // POST /Staff/Unlock
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Unlock(int id)
    {
        var result = await _userLogic.UnlockAsync(id);

        if (!result.Success)
            TempData["StaffError"] = result.ErrorMessage;
        else
            TempData["StaffSuccess"] = "Lockout cleared — they can sign in again now.";

        return RedirectToAction(nameof(Index));
    }

    // GET /Staff/ResetPassword/{id}
    public async Task<IActionResult> ResetPassword(int id)
    {
        var user = await _userLogic.GetByIdAsync(id);
        if (user == null)
            return NotFound();

        return View(new ResetPasswordViewModel { UserId = user.UserId, FullName = user.FullName });
    }

    // POST /Staff/ResetPassword
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var result = await _userLogic.SetPasswordAsync(model.UserId, model.NewPassword);

        if (!result.Success)
        {
            model.ErrorMessage = result.ErrorMessage;
            return View(model);
        }

        TempData["StaffSuccess"] = $"Password reset for {model.FullName}.";
        return RedirectToAction(nameof(Index));
    }

    // GET /Staff/Activity — recent sign-in attempts.
    public async Task<IActionResult> Activity()
    {
        var audit = await _userLogic.GetRecentLoginActivityAsync(200);

        return View(audit.Select(a => new LoginActivityRowViewModel
        {
            OccurredAt = a.OccurredAt,
            Event = a.Event,
            EmailAttempted = a.EmailAttempted,
            IpAddress = a.IpAddress,
            UserName = a.User?.FullName
        }).ToList());
    }
}
