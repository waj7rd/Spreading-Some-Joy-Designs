using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SpreadingJoy.ViewModels;

namespace SpreadingJoy.Controllers;

[Authorize(Policy = Policies.ManageStudioSettings)]
public class SettingsController : Controller
{
    private readonly IStudioLogic _studioLogic;

    public SettingsController(IStudioLogic studioLogic)
    {
        _studioLogic = studioLogic;
    }

    // GET /Settings
    public async Task<IActionResult> Index()
    {
        var studio = await _studioLogic.GetAsync();
        if (studio == null)
            return NotFound();

        return View(new StudioSettingsViewModel
        {
            Name = studio.Name,
            Phone = studio.Phone,
            Email = studio.Email,
            AddressLine = studio.AddressLine,
            City = studio.City,
            State = studio.State,
            PostalCode = studio.PostalCode,
            TimeZoneId = studio.TimeZoneId,
            DailyPrintCapacity = studio.DailyPrintCapacity,
            TurnaroundDays = studio.TurnaroundDays,
            ClosedDays = studio.ClosedDays.ToList(),
            CurrentTier = studio.Tier.ToString(),
            SuccessMessage = TempData["SettingsSuccess"] as string
        });
    }

    // POST /Settings
    //
    // Note there's no tier here. The view model has no property for it, so the
    // model binder has nothing to bind even if somebody posts one — which is
    // stronger than hiding the field, and matches IStudioLogic having no
    // operation for it either.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(StudioSettingsViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var result = await _studioLogic.UpdateAsync(
            model.Name,
            model.Phone,
            model.Email,
            model.AddressLine,
            model.City,
            model.State,
            model.PostalCode,
            model.TimeZoneId,
            model.DailyPrintCapacity,
            model.TurnaroundDays,
            model.ClosedDays);

        if (!result.Success)
        {
            model.ErrorMessage = result.ErrorMessage;
            return View(model);
        }

        TempData["SettingsSuccess"] = "Settings saved.";
        return RedirectToAction(nameof(Index));
    }
}
