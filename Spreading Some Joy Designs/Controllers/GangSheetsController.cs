using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SpreadingJoy.Domain.EntityModels;
using SpreadingJoy.Domain.Production;
using SpreadingJoy.Security;
using SpreadingJoy.ViewModels;

namespace SpreadingJoy.Controllers;

// Building sheets of DTF film for the press.
//
// Gated on ManageOrders rather than a policy of its own: packing film is
// production work, the same job as moving orders across the board, and the same
// people do it. It is deliberately not tier-gated either — a gang sheet is how
// this shop gets shirts printed, not a capability it buys, which is the same
// reasoning that keeps shipping a switch on the studio rather than a Feature.
[Authorize(Policy = Policies.ManageOrders)]
public class GangSheetsController : Controller
{
    private readonly IGangSheetLogic _gangSheetLogic;
    private readonly IGangSheetRequestLogic _requestLogic;
    private readonly IGangSheetSizeLogic _sizeLogic;
    private readonly IImageStore _imageStore;

    public GangSheetsController(
        IGangSheetLogic gangSheetLogic,
        IGangSheetRequestLogic requestLogic,
        IGangSheetSizeLogic sizeLogic,
        IImageStore imageStore)
    {
        _gangSheetLogic = gangSheetLogic;
        _requestLogic = requestLogic;
        _sizeLogic = sizeLogic;
        _imageStore = imageStore;
    }

    // GET /GangSheets
    public async Task<IActionResult> Index() => View(await BuildListAsync());

    // POST /GangSheets/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    //
    // The form is rendered from the list page's model, so its fields are named
    // "NewSheet.*". Without the prefix the binder finds nothing to bind, every
    // field comes back empty, and the page reports a validation failure the
    // user can't see the cause of.
    public async Task<IActionResult> Create(
        [Bind(Prefix = nameof(GangSheetListViewModel.NewSheet))] EditGangSheetViewModel model)
    {
        if (!ModelState.IsValid)
        {
            var invalid = await BuildListAsync();
            invalid.NewSheet = model;
            invalid.ErrorMessage = "Check the sheet settings below.";
            return View(nameof(Index), invalid);
        }

        var result = await _gangSheetLogic.CreateAsync(ToDetails(model), User.UserId());

        if (!result.Success)
        {
            var failed = await BuildListAsync();
            failed.NewSheet = model;
            failed.ErrorMessage = result.ErrorMessage;
            return View(nameof(Index), failed);
        }

        TempData["GangSheetSuccess"] = "Sheet started — now put something on it.";
        return RedirectToAction(nameof(Build), new { id = result.GangSheetId });
    }

    // GET /GangSheets/Build/{id} — the sheet, what's on it, and what's waiting.
    public async Task<IActionResult> Build(int id)
    {
        var model = await BuildSheetAsync(id);
        if (model == null)
            return NotFound();

        model.SuccessMessage = TempData["GangSheetSuccess"] as string;
        model.ErrorMessage = TempData["GangSheetError"] as string;

        return View(model);
    }

    // POST /GangSheets/AddItems
    //
    // Only the identifying triple comes off the form — which order line, which
    // side, how many. The artwork and the printed size are looked up again on
    // this side, so a posted form can't put a different image or a different
    // size on the film than the screen was showing. Same reasoning as the
    // ordering path taking a design token rather than a price.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddItems(int id, List<SelectedTransferViewModel> selected)
    {
        var chosen = (selected ?? []).Where(s => s.Selected).ToList();

        if (chosen.Count == 0)
        {
            TempData["GangSheetError"] = "Tick something to add first.";
            return RedirectToAction(nameof(Build), new { id });
        }

        var candidates = await _gangSheetLogic.GetCandidatesAsync();

        var requests = new List<GangSheetItemRequest>();

        foreach (var choice in chosen)
        {
            var candidate = candidates.FirstOrDefault(c =>
                c.OrderLineId == choice.OrderLineId && c.Side == choice.Side);

            // Gone since the page was rendered — the order was cancelled, or the
            // design was changed. Skipped rather than guessed at.
            if (candidate == null)
                continue;

            requests.Add(new GangSheetItemRequest(
                ArtworkId: candidate.ArtworkId,
                OrderLineId: candidate.OrderLineId,
                DesignId: candidate.DesignId,
                Side: candidate.Side,
                Label: candidate.Label,
                WidthMm: candidate.WidthMm,
                HeightMm: candidate.HeightMm,
                Quantity: choice.Quantity));
        }

        if (requests.Count == 0)
        {
            TempData["GangSheetError"] = "Nothing you picked is still waiting to be printed.";
            return RedirectToAction(nameof(Build), new { id });
        }

        var result = await _gangSheetLogic.AddItemsAsync(id, requests);

        if (!result.Success)
            TempData["GangSheetError"] = result.ErrorMessage;
        else
            TempData["GangSheetSuccess"] = $"Added {requests.Sum(r => r.Quantity)} transfer(s) and repacked the sheet.";

        return RedirectToAction(nameof(Build), new { id });
    }

    // POST /GangSheets/RemoveItem
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveItem(int id, int itemId)
    {
        var result = await _gangSheetLogic.RemoveItemAsync(id, itemId);

        if (!result.Success)
            TempData["GangSheetError"] = result.ErrorMessage;
        else
            TempData["GangSheetSuccess"] = "Taken off, and everything below it moved up.";

        return RedirectToAction(nameof(Build), new { id });
    }

    // POST /GangSheets/Repack
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Repack(int id)
    {
        var result = await _gangSheetLogic.RepackAsync(id);

        if (!result.Success)
            TempData["GangSheetError"] = result.ErrorMessage;
        else
            TempData["GangSheetSuccess"] = "Repacked.";

        return RedirectToAction(nameof(Build), new { id });
    }

    // POST /GangSheets/Settings — resize the film, change the spacing.
    [HttpPost]
    [ValidateAntiForgeryToken]
    //
    // Bound with a prefix for the same reason Create is: the settings form is
    // rendered from the build page's model, so its fields are named "Settings.*".
    public async Task<IActionResult> Settings(
        int id,
        [Bind(Prefix = nameof(GangSheetBuildViewModel.Settings))] EditGangSheetViewModel model)
    {
        if (!ModelState.IsValid)
        {
            TempData["GangSheetError"] = "Check the sheet settings.";
            return RedirectToAction(nameof(Build), new { id });
        }

        var result = await _gangSheetLogic.UpdateAsync(id, ToDetails(model));

        if (!result.Success)
            TempData["GangSheetError"] = result.ErrorMessage;
        else
            TempData["GangSheetSuccess"] = "Sheet updated and repacked.";

        return RedirectToAction(nameof(Build), new { id });
    }

    // POST /GangSheets/MarkReady
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkReady(int id)
    {
        var result = await _gangSheetLogic.MarkReadyAsync(id);

        if (!result.Success)
            TempData["GangSheetError"] = result.ErrorMessage;
        else
            TempData["GangSheetSuccess"] = "Locked and ready for the press.";

        return RedirectToAction(nameof(Build), new { id });
    }

    // POST /GangSheets/MarkPrinted
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkPrinted(int id)
    {
        var result = await _gangSheetLogic.MarkPrintedAsync(id);

        if (!result.Success)
            TempData["GangSheetError"] = result.ErrorMessage;
        else
            TempData["GangSheetSuccess"] = "Marked as printed.";

        return RedirectToAction(nameof(Build), new { id });
    }

    // POST /GangSheets/Reopen
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reopen(int id)
    {
        var result = await _gangSheetLogic.ReopenAsync(id);

        if (!result.Success)
            TempData["GangSheetError"] = result.ErrorMessage;
        else
            TempData["GangSheetSuccess"] = "Back to a draft.";

        return RedirectToAction(nameof(Build), new { id });
    }

    // POST /GangSheets/Delete
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await _gangSheetLogic.DeleteAsync(id);

        if (!result.Success)
            TempData["GangSheetError"] = result.ErrorMessage;
        else
            TempData["GangSheetSuccess"] = "Sheet deleted.";

        return RedirectToAction(nameof(Index));
    }

    // GET /GangSheets/CutList/{id} — what to write on, and cut apart, at the
    // bench. Deliberately plain: this gets printed on paper.
    public async Task<IActionResult> CutList(int id)
    {
        var model = await BuildSheetAsync(id);
        if (model == null)
            return NotFound();

        return View(model);
    }

    // ---- Sheets people asked us to print --------------------------------

    // GET /GangSheets/Requests — the queue of sheets built on the public side.
    public async Task<IActionResult> Requests(string status = GangSheetRequestStatus.Pending)
    {
        if (!GangSheetRequestStatus.All.Contains(status))
            status = GangSheetRequestStatus.Pending;

        return View(await BuildRequestsAsync(status));
    }

    // POST /GangSheets/AcceptRequest
    //
    // Creates a customer and a real sheet, in one transaction. Refuses while any
    // of the artwork is still waiting for review — the approval gate lives in
    // GangSheetLogic.AddItemsAsync, which this goes through rather than around.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AcceptRequest(int id, string? returnStatus)
    {
        var userId = User.UserId();
        if (userId == null)
            return Forbid();

        var result = await _requestLogic.AcceptAsync(id, userId.Value);

        if (!result.Success)
        {
            TempData["GangSheetError"] = result.ErrorMessage;
            return RedirectToAction(nameof(Requests), new { status = returnStatus ?? GangSheetRequestStatus.Pending });
        }

        TempData["GangSheetSuccess"] = "Accepted — the sheet is packed and waiting as a draft.";
        return RedirectToAction(nameof(Build), new { id = result.GangSheetId });
    }

    // POST /GangSheets/DeclineRequest
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeclineRequest(int id, string? reason, string? returnStatus)
    {
        var userId = User.UserId();
        if (userId == null)
            return Forbid();

        var result = await _requestLogic.DeclineAsync(id, userId.Value, reason ?? string.Empty);

        TempData[result.Success ? "GangSheetSuccess" : "GangSheetError"] = result.Success
            ? "Declined. The customer sees the reason you gave."
            : result.ErrorMessage;

        return RedirectToAction(nameof(Requests), new { status = returnStatus ?? GangSheetRequestStatus.Pending });
    }

    // ---- What the studio sells ------------------------------------------

    // GET /GangSheets/Sizes — the gang sheet catalogue and its prices.
    //
    // Catalogue work, so it needs ManageCatalog on top of the ManageOrders this
    // controller already carries. An associate packs film; deciding what it
    // costs is not their job.
    [Authorize(Policy = Policies.ManageCatalog)]
    public async Task<IActionResult> Sizes() => View(await BuildSizesAsync());

    // POST /GangSheets/CreateSize
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = Policies.ManageCatalog)]
    public async Task<IActionResult> CreateSize(
        [Bind(Prefix = nameof(GangSheetSizeListViewModel.NewSize))] EditGangSheetSizeViewModel model)
    {
        if (!ModelState.IsValid)
        {
            var invalid = await BuildSizesAsync();
            invalid.NewSize = model;
            invalid.ErrorMessage = "Check the sheet below.";
            return View(nameof(Sizes), invalid);
        }

        var result = await _sizeLogic.CreateAsync(ToSizeDetails(model));

        if (!result.Success)
        {
            var failed = await BuildSizesAsync();
            failed.NewSize = model;
            failed.ErrorMessage = result.ErrorMessage;
            return View(nameof(Sizes), failed);
        }

        TempData["GangSheetSuccess"] = $"\"{model.Name.Trim()}\" is on offer.";
        return RedirectToAction(nameof(Sizes));
    }

    // POST /GangSheets/UpdateSize
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = Policies.ManageCatalog)]
    public async Task<IActionResult> UpdateSize(int id, EditGangSheetSizeViewModel model)
    {
        var result = await _sizeLogic.UpdateAsync(id, ToSizeDetails(model));

        TempData[result.Success ? "GangSheetSuccess" : "GangSheetError"] = result.Success
            ? "Updated. Sheets already sold keep the price they were sold at."
            : result.ErrorMessage;

        return RedirectToAction(nameof(Sizes));
    }

    // POST /GangSheets/SetSizeActive
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = Policies.ManageCatalog)]
    public async Task<IActionResult> SetSizeActive(int id, bool isActive)
    {
        var result = await _sizeLogic.SetActiveAsync(id, isActive);

        TempData[result.Success ? "GangSheetSuccess" : "GangSheetError"] = result.Success
            ? (isActive ? "Back on offer." : "Withdrawn — it's off the public builder.")
            : result.ErrorMessage;

        return RedirectToAction(nameof(Sizes));
    }

    private async Task<GangSheetSizeListViewModel> BuildSizesAsync()
    {
        var sizes = await _sizeLogic.GetAllAsync();

        return new GangSheetSizeListViewModel
        {
            SuccessMessage = TempData["GangSheetSuccess"] as string,
            ErrorMessage = TempData["GangSheetError"] as string,
            Sizes = sizes.Select(s => new EditGangSheetSizeViewModel
            {
                GangSheetSizeId = s.GangSheetSizeId,
                Name = s.Name,
                WidthMm = s.WidthMm,
                LengthMm = s.LengthMm,
                Price = s.Price,
                IsActive = s.IsActive
            }).ToList()
        };
    }

    private async Task<GangSheetRequestListViewModel> BuildRequestsAsync(string status)
    {
        var requests = await _requestLogic.GetByStatusAsync(status);

        return new GangSheetRequestListViewModel
        {
            Status = status,
            PendingCount = await _requestLogic.CountPendingAsync(),
            SuccessMessage = TempData["GangSheetSuccess"] as string,
            ErrorMessage = TempData["GangSheetError"] as string,
            Requests = requests.Select(r => new GangSheetRequestRowViewModel
            {
                Id = r.GangSheetRequestId,
                CustomerName = r.CustomerName,
                Email = r.Email,
                Phone = r.Phone,
                SizeName = r.GangSheetSize?.Name ?? "—",
                PriceQuoted = r.PriceQuoted,
                TransferCount = r.TransferCount,
                Notes = r.Notes,
                Status = r.Status,
                CreatedAt = r.CreatedAt,
                HandledAt = r.HandledAt,
                HandledBy = r.HandledByUser?.FullName,
                DeclineReason = r.DeclineReason,
                GangSheetId = r.GangSheetId,
                Items = r.Items.Select(i => new GangSheetRequestItemViewModel
                {
                    Label = i.Label,
                    ImageUrl = i.Artwork == null ? string.Empty : _imageStore.PublicPath(i.Artwork.StoredFileName),
                    WidthMm = i.WidthMm,
                    HeightMm = i.HeightMm,
                    Quantity = i.Quantity,
                    ArtworkStatus = i.Artwork?.Status ?? ArtworkStatus.Pending,
                    Dpi = i.Artwork == null ? 0 : ImageLimits.EffectiveDpi(i.Artwork.WidthPx, i.WidthMm)
                }).ToList()
            }).ToList()
        };
    }

    private static GangSheetSizeDetails ToSizeDetails(EditGangSheetSizeViewModel model) => new(
        Name: model.Name,
        WidthMm: model.WidthMm,
        LengthMm: model.LengthMm,
        Price: model.Price);

    private async Task<GangSheetListViewModel> BuildListAsync()
    {
        var sheets = await _gangSheetLogic.GetAllAsync();

        return new GangSheetListViewModel
        {
            SuccessMessage = TempData["GangSheetSuccess"] as string,
            ErrorMessage = TempData["GangSheetError"] as string,
            Sheets = sheets.Select(ToRow).ToList()
        };
    }

    private async Task<GangSheetBuildViewModel?> BuildSheetAsync(int id)
    {
        var sheet = await _gangSheetLogic.GetAsync(id);
        if (sheet == null)
            return null;

        var candidates = await _gangSheetLogic.GetCandidatesAsync();

        // The preview is drawn against the film the packer was working to while
        // the sheet is open, and against what it actually used once it isn't —
        // a locked sheet shown against its ceiling would be mostly empty space
        // that no longer exists.
        var canvasLengthMm = sheet.IsEditable
            ? sheet.MaxLengthMm
            : Math.Max(sheet.UsedLengthMm, 1);

        return new GangSheetBuildViewModel
        {
            Sheet = ToRow(sheet),
            IsEditable = sheet.IsEditable,
            AllowRotation = sheet.AllowRotation,
            Notes = sheet.Notes,
            Settings = new EditGangSheetViewModel
            {
                GangSheetId = sheet.GangSheetId,
                Name = sheet.Name,
                WidthMm = sheet.WidthMm,
                MaxLengthMm = sheet.MaxLengthMm,
                GutterMm = sheet.GutterMm,
                MarginMm = sheet.MarginMm,
                AllowRotation = sheet.AllowRotation,
                Notes = sheet.Notes
            },
            Items = sheet.Items
                .OrderBy(i => i.YMm)
                .ThenBy(i => i.XMm)
                .Select(i => new GangSheetItemViewModel
                {
                    Id = i.GangSheetItemId,
                    Label = i.Label,
                    Side = i.Side,
                    ImageUrl = i.Artwork == null ? string.Empty : _imageStore.PublicPath(i.Artwork.StoredFileName),
                    WidthMm = i.WidthMm,
                    HeightMm = i.HeightMm,
                    XMm = i.XMm,
                    YMm = i.YMm,
                    Rotated = i.Rotated,
                    IsPlaced = i.IsPlaced,
                    Dpi = i.Artwork == null ? 0 : ImageLimits.EffectiveDpi(i.Artwork.WidthPx, i.WidthMm),
                    SheetWidthMm = sheet.WidthMm,
                    SheetLengthMm = canvasLengthMm
                })
                .ToList(),
            Candidates = candidates.Select(c => new TransferCandidateViewModel
            {
                OrderLineId = c.OrderLineId,
                OrderId = c.OrderId,
                DesignId = c.DesignId,
                ArtworkId = c.ArtworkId,
                Side = c.Side,
                Label = c.Label,
                DesignName = c.DesignName,
                CustomerName = c.CustomerName,
                SizeCode = c.SizeCode,
                DueOn = c.DueOn,
                Quantity = c.Quantity,
                WidthMm = c.WidthMm,
                HeightMm = c.HeightMm,
                AlreadyPlaced = c.AlreadyPlaced,
                ArtworkStatus = c.ArtworkStatus,
                ImageUrl = _imageStore.PublicPath(c.StoredFileName),
                Dpi = ImageLimits.EffectiveDpi(c.ArtworkWidthPx, c.WidthMm)
            }).ToList()
        };
    }

    private static GangSheetDetails ToDetails(EditGangSheetViewModel model) => new(
        Name: model.Name,
        WidthMm: model.WidthMm,
        MaxLengthMm: model.MaxLengthMm,
        GutterMm: model.GutterMm,
        MarginMm: model.MarginMm,
        AllowRotation: model.AllowRotation,
        Notes: model.Notes);

    private static GangSheetRowViewModel ToRow(GangSheet sheet) => new()
    {
        Id = sheet.GangSheetId,
        Name = sheet.Name,
        Status = sheet.Status,
        WidthMm = sheet.WidthMm,
        MaxLengthMm = sheet.MaxLengthMm,
        UsedLengthMm = sheet.UsedLengthMm,
        ItemCount = sheet.Items.Count,
        PlacedCount = sheet.PlacedCount,
        UnplacedCount = sheet.UnplacedCount,
        CoveragePercent = sheet.CoveragePercent,
        CreatedAt = sheet.CreatedAt,
        PrintedAt = sheet.PrintedAt,
        CreatedBy = sheet.CreatedByUser?.FullName,
        Origin = sheet.Origin,
        CustomerName = sheet.Customer?.FullName,
        Price = sheet.Price
    };
}
