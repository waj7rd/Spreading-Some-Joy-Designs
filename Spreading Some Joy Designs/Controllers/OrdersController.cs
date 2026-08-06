using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SpreadingJoy.Domain.EntityModels;
using SpreadingJoy.Security;
using SpreadingJoy.ViewModels;

namespace SpreadingJoy.Controllers;

public class OrdersController : Controller
{
    private readonly IOrderLogic _orderLogic;
    private readonly IOrderRequestLogic _requestLogic;
    private readonly IDesignLogic _designLogic;
    private readonly IProductLogic _productLogic;
    private readonly IImageStore _imageStore;
    private readonly IStudioSettings _settings;
    private readonly IStudioClock _clock;

    public OrdersController(
        IOrderLogic orderLogic,
        IOrderRequestLogic requestLogic,
        IDesignLogic designLogic,
        IProductLogic productLogic,
        IImageStore imageStore,
        IStudioSettings settings,
        IStudioClock clock)
    {
        _orderLogic = orderLogic;
        _requestLogic = requestLogic;
        _designLogic = designLogic;
        _productLogic = productLogic;
        _imageStore = imageStore;
        _settings = settings;
        _clock = clock;
    }

    // ---- public ----

    // GET /Orders/Place?design={token}
    //
    // Addressed by the design's unguessable token rather than its primary key.
    // This page has to be anonymous — a customer has no account — so with a
    // sequential id anyone could count upwards and read every design ever made,
    // artwork included.
    public async Task<IActionResult> Place(Guid design)
    {
        var model = await BuildPlaceViewModelAsync(design);
        if (model == null)
            return NotFound();

        return View(model);
    }

    // POST /Orders/Place
    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(RateLimitPolicies.PublicOrdering)]
    public async Task<IActionResult> Place(PlaceOrderViewModel model)
    {
        // Re-checked server-side rather than trusted from the form. A checkbox
        // is one line to remove from a POST, and this one is the record that
        // somebody claimed the right to use the artwork.
        if (!model.RightsAttested)
        {
            ModelState.AddModelError(nameof(model.RightsAttested),
                "Please confirm you have the right to use this artwork.");
        }

        // Resolved from the token, so the posted form can't be repointed at
        // another design by editing a number.
        var design = await _designLogic.GetByPublicTokenAsync(model.DesignToken);
        if (design == null || !design.IsActive)
            return NotFound();

        if (!ModelState.IsValid)
            return await RedisplayAsync(model);

        var result = await _requestLogic.SubmitAsync(new SubmitOrderRequest(
            CustomerName: model.CustomerName,
            Email: model.Email,
            Phone: model.Phone,
            DesignId: design.DesignId,
            SizeCode: model.SizeCode,
            Quantity: model.Quantity,
            RequestedFor: model.RequestedFor,
            RightsAttested: model.RightsAttested,
            Notes: model.Notes));

        if (!result.Success)
            return await RedisplayAsync(model, result.ErrorMessage);

        return RedirectToAction(nameof(Submitted));
    }

    // Re-renders the order form after a refusal, keeping what the customer
    // typed and rebuilding the read-only summary from the database.
    //
    // The summary — garment, colours, print area, placements, price — is never
    // taken from the post. It's display data about a design the customer
    // doesn't own, and accepting it back from the browser would let anyone
    // render whatever preview and price they liked.
    private async Task<IActionResult> RedisplayAsync(PlaceOrderViewModel model, string? errorMessage = null)
    {
        var fresh = await BuildPlaceViewModelAsync(model.DesignToken);
        if (fresh == null)
            return NotFound();

        model.DesignName = fresh.DesignName;
        model.GarmentName = fresh.GarmentName;
        model.AvailableSizes = fresh.AvailableSizes;
        model.Front = fresh.Front;
        model.Back = fresh.Back;
        model.ExtendedSizeUpcharge = fresh.ExtendedSizeUpcharge;
        model.PrintedSides = fresh.PrintedSides;

        // Priced at the size they actually chose, not the default, so a
        // rejected form still shows the right figure.
        model.UnitPrice = await UnitPriceForAsync(model.DesignToken, model.SizeCode, fresh.UnitPrice);

        model.ErrorMessage = errorMessage;

        return View(model);
    }

    private async Task<decimal> UnitPriceForAsync(Guid designToken, string? sizeCode, decimal fallback)
    {
        if (string.IsNullOrWhiteSpace(sizeCode))
            return fallback;

        var design = await _designLogic.GetByPublicTokenAsync(designToken);
        if (design == null)
            return fallback;

        var product = await _productLogic.GetByIdAsync(design.ProductId);
        if (product == null)
            return fallback;

        var size = sizeCode.Trim().ToUpperInvariant();

        return product.Sizes.Contains(size)
            ? Pricing.UnitPrice(product, design, size)
            : fallback;
    }

    // GET /Orders/Submitted
    public IActionResult Submitted() => View();

    // ---- staff ----

    // GET /Orders/Board — the production board.
    [Authorize(Policy = Policies.ManageOrders)]
    public async Task<IActionResult> Board()
    {
        var orders = await _orderLogic.GetOpenAsync();
        var today = _clock.Today;

        return View(new OrderBoardViewModel
        {
            SuccessMessage = TempData["OrderSuccess"] as string,
            ErrorMessage = TempData["OrderError"] as string,
            Statuses = OrderStatus.All,
            Orders = orders.Select(o => new OrderRowViewModel
            {
                Id = o.OrderId,
                CustomerName = o.Customer?.FullName ?? "—",
                Status = o.Status,
                DueOn = o.DueOn,
                GarmentCount = o.OrderLines.Sum(l => l.Quantity),
                Total = o.OrderLines.Sum(l => l.UnitPrice * l.Quantity),
                CreatedAt = o.CreatedAt,
                Notes = o.Notes,

                // Judged against the studio's today, not the server's.
                IsOverdue = o.DueOn < today
            }).ToList()
        });
    }

    // GET /Orders/Details/{id}
    [Authorize(Policy = Policies.ManageOrders)]
    public async Task<IActionResult> Details(int id)
    {
        var order = await _orderLogic.GetByIdAsync(id);
        if (order == null)
            return NotFound();

        return View(new OrderDetailsViewModel
        {
            Id = order.OrderId,
            CustomerName = order.Customer?.FullName ?? "—",
            CustomerEmail = order.Customer?.Email,
            CustomerPhone = order.Customer?.Phone,
            Status = order.Status,
            DueOn = order.DueOn,
            Notes = order.Notes,
            RightsAttested = order.RightsAttested,
            RightsAttestedAt = order.RightsAttestedAt,
            CancellationReason = order.CancellationReason,
            CreatedAt = order.CreatedAt,
            Statuses = OrderStatus.All,
            SuccessMessage = TempData["OrderSuccess"] as string,
            ErrorMessage = TempData["OrderError"] as string,
            Lines = order.OrderLines.Select(l => new OrderLineViewModel
            {
                DesignId = l.DesignId,
                DesignName = l.Design?.Name ?? "—",
                GarmentName = l.Design?.Product == null
                    ? "—"
                    : $"{l.Design.Product.Colour} {l.Design.Product.Name}",
                SizeCode = l.SizeCode,
                Quantity = l.Quantity,
                UnitPrice = l.UnitPrice
            }).ToList()
        });
    }

    // POST /Orders/SetStatus
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = Policies.ManageOrders)]
    public async Task<IActionResult> SetStatus(int id, string status, string? returnTo)
    {
        var result = await _orderLogic.SetStatusAsync(id, status);

        if (!result.Success)
            TempData["OrderError"] = result.ErrorMessage;
        else
            TempData["OrderSuccess"] = $"Order #{id} is now {status}.";

        return returnTo == "details"
            ? RedirectToAction(nameof(Details), new { id })
            : RedirectToAction(nameof(Board));
    }

    // POST /Orders/Cancel
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = Policies.ManageOrders)]
    public async Task<IActionResult> Cancel(int id, string reason)
    {
        var result = await _orderLogic.CancelAsync(id, reason);

        if (!result.Success)
            TempData["OrderError"] = result.ErrorMessage;
        else
            TempData["OrderSuccess"] = $"Order #{id} cancelled.";

        return RedirectToAction(nameof(Details), new { id });
    }

    // GET /Orders/Requests — the queue of anonymous submissions.
    [Authorize(Policy = Policies.ManageOrders)]
    public async Task<IActionResult> Requests() => View(await BuildRequestQueueAsync());

    // POST /Orders/AcceptRequest
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = Policies.ManageOrders)]
    public async Task<IActionResult> AcceptRequest(int id, DateTime dueOn)
    {
        var userId = User.UserId();
        if (userId == null)
            return Forbid();

        var result = await _requestLogic.AcceptAsync(id, userId.Value, dueOn);

        if (!result.Success)
        {
            TempData["RequestError"] = result.ErrorMessage;
            return RedirectToAction(nameof(Requests));
        }

        TempData["OrderSuccess"] = "Request accepted and the order is on the board.";
        return RedirectToAction(nameof(Details), new { id = result.OrderId });
    }

    // POST /Orders/DeclineRequest
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = Policies.ManageOrders)]
    public async Task<IActionResult> DeclineRequest(int id, string reason)
    {
        var userId = User.UserId();
        if (userId == null)
            return Forbid();

        var result = await _requestLogic.DeclineAsync(id, userId.Value, reason);

        if (!result.Success)
            TempData["RequestError"] = result.ErrorMessage;
        else
            TempData["RequestSuccess"] = "Request declined.";

        return RedirectToAction(nameof(Requests));
    }

    // ---- internals ----

    private async Task<PlaceOrderViewModel?> BuildPlaceViewModelAsync(Guid designToken)
    {
        var design = await _designLogic.GetByPublicTokenAsync(designToken);
        if (design == null || !design.IsActive)
            return null;

        var product = await _productLogic.GetByIdAsync(design.ProductId);
        if (product == null)
            return null;

        var defaultSize = product.Sizes.FirstOrDefault() ?? string.Empty;

        return new PlaceOrderViewModel
        {
            DesignToken = design.PublicToken,
            DesignName = design.Name,
            GarmentName = $"{product.Colour} {product.Name}",
            AvailableSizes = product.Sizes.ToList(),
            SizeCode = defaultSize,

            Front = BuildPreview(design, product, "front"),
            Back = BuildPreview(design, product, "back"),

            // Shown as "each" beside the preview. Read from the same Pricing
            // function the order itself uses, so the figure the customer is
            // quoted and the figure snapshotted onto the line can't disagree.
            UnitPrice = Pricing.UnitPrice(product, design, defaultSize),
            ExtendedSizeUpcharge = product.ExtendedSizeUpcharge,
            PrintedSides = Pricing.PrintedSides(design),

            // Prefilled with a date the studio could actually hit, rather than
            // today — a default the rules would immediately reject makes the
            // customer fix an error they didn't cause.
            RequestedFor = StudioCalendar.EarliestDueDate(_settings, _clock.Today)
        };
    }

    private ShirtPreviewViewModel BuildPreview(Design design, Product product, string side, bool showPrintAreaSize = true)
    {
        var isFront = side == "front";
        var artwork = isFront ? design.FrontArtwork : design.BackArtwork;

        return new ShirtPreviewViewModel
        {
            Side = side,
            ShowPrintAreaSize = showPrintAreaSize,
            ColourHex = product.ColourHex,
            PrintAreaWidthMm = product.PrintAreaWidthMm,
            PrintAreaHeightMm = product.PrintAreaHeightMm,
            ImageUrl = artwork == null ? null : _imageStore.PublicPath(artwork.StoredFileName),
            IsPending = artwork?.Status == ArtworkStatus.Pending,
            XMm = (isFront ? design.FrontXMm : design.BackXMm) ?? 0,
            YMm = (isFront ? design.FrontYMm : design.BackYMm) ?? 0,
            WidthMm = (isFront ? design.FrontWidthMm : design.BackWidthMm) ?? 0,
            HeightMm = (isFront ? design.FrontHeightMm : design.BackHeightMm) ?? 0
        };
    }

    private async Task<OrderRequestQueueViewModel> BuildRequestQueueAsync()
    {
        var requests = await _requestLogic.GetPendingAsync();
        var earliest = StudioCalendar.EarliestDueDate(_settings, _clock.Today);

        return new OrderRequestQueueViewModel
        {
            SuccessMessage = TempData["RequestSuccess"] as string,
            ErrorMessage = TempData["RequestError"] as string,
            Requests = requests.Select(r => new OrderRequestRowViewModel
            {
                Id = r.OrderRequestId,
                CustomerName = r.CustomerName,
                Email = r.Email,
                Phone = r.Phone,
                DesignName = r.Design?.Name ?? "—",
                GarmentName = r.Design?.Product == null
                    ? "—"
                    : $"{r.Design.Product.Colour} {r.Design.Product.Name}",

                // The repository Includes the design, its product and both
                // artworks, so these need no further round trips.
                // No print-area caption in the queue: staff are judging the
                // artwork here, not the garment spec, and several requests share
                // one screen.
                Front = r.Design?.Product == null
                    ? new ShirtPreviewViewModel { Side = "front" }
                    : BuildPreview(r.Design, r.Design.Product, "front", showPrintAreaSize: false),
                Back = r.Design?.Product == null
                    ? new ShirtPreviewViewModel { Side = "back" }
                    : BuildPreview(r.Design, r.Design.Product, "back", showPrintAreaSize: false),

                SizeCode = r.SizeCode,
                Quantity = r.Quantity,
                RequestedFor = r.RequestedFor,
                Notes = r.Notes,
                RightsAttested = r.RightsAttested,
                CreatedAt = r.CreatedAt,

                // Whichever is later: what the customer asked for, or the
                // soonest the studio could actually manage.
                SuggestedDueOn = r.RequestedFor > earliest ? r.RequestedFor : earliest
            }).ToList()
        };
    }
}
