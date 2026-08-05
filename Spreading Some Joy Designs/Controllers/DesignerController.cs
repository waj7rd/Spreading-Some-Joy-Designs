using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SpreadingJoy.Domain.EntityModels;
using SpreadingJoy.Security;
using SpreadingJoy.ViewModels;

namespace SpreadingJoy.Controllers;

// The designer. Anonymous on purpose — the whole storefront pitch is that
// somebody can land on the site, put a picture on a shirt and ask for it
// without making an account first.
//
// Because it's anonymous, the in-progress design lives in the session rather
// than in a customer record. Nothing here creates a Customer; that only happens
// when a member of staff accepts the request.
public class DesignerController : Controller
{
    private const string SessionDesignKey = "designer.designId";

    private readonly IDesignLogic _designLogic;
    private readonly IProductLogic _productLogic;
    private readonly IArtworkLogic _artworkLogic;
    private readonly IImageStore _imageStore;

    public DesignerController(
        IDesignLogic designLogic,
        IProductLogic productLogic,
        IArtworkLogic artworkLogic,
        IImageStore imageStore)
    {
        _designLogic = designLogic;
        _productLogic = productLogic;
        _artworkLogic = artworkLogic;
        _imageStore = imageStore;
    }

    // GET /Designer?productId=1
    public async Task<IActionResult> Index(int? productId, int? designId)
    {
        var products = await LoadProductsAsync();
        if (products.Count == 0)
            return View("NoProducts");

        var model = new DesignerViewModel
        {
            Products = products,
            ProductId = productId ?? products[0].Id,
            SuccessMessage = TempData["DesignerSuccess"] as string,
            ErrorMessage = TempData["DesignerError"] as string,
            PendingNotice = TempData["DesignerPending"] as string
        };

        // Re-hydrate whichever artwork the visitor has already attached this
        // session. Held in TempData rather than a database row: an anonymous
        // visitor who wanders off shouldn't leave a half-made design behind.
        await HydrateSideAsync(model.Front, GetSessionArtwork("front"));
        await HydrateSideAsync(model.Back, GetSessionArtwork("back"));

        var product = products.FirstOrDefault(p => p.Id == model.ProductId) ?? products[0];
        DefaultPlacements(model, product);

        return View(model);
    }

    // POST /Designer/AddFromUrl
    //
    // The endpoint the whole site exists for: paste an address, and the server
    // goes and gets it. Rate limited hard — every call makes our server fetch
    // something a stranger chose.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(RateLimitPolicies.ArtworkFetch)]
    public async Task<IActionResult> AddFromUrl(AddArtworkViewModel model, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(model.Url))
        {
            TempData["DesignerError"] = "Paste the address of an image.";
            return RedirectToAction(nameof(Index), new { productId = model.ProductId });
        }

        var result = await _artworkLogic.AddFromUrlAsync(
            model.Url, customerId: null, approvedByUserId: StudioUserId(), cancellationToken);

        return await AfterArtworkAddedAsync(result, model.Side, model.ProductId);
    }

    // POST /Designer/AddFromUpload
    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(RateLimitPolicies.ArtworkFetch)]
    [RequestSizeLimit(ImageLimits.MaxBytes + 1024 * 1024)]
    public async Task<IActionResult> AddFromUpload(IFormFile? file, string side, int productId, CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0)
        {
            TempData["DesignerError"] = "Choose a file to upload.";
            return RedirectToAction(nameof(Index), new { productId });
        }

        if (file.Length > ImageLimits.MaxBytes)
        {
            TempData["DesignerError"] = $"That file is over {ImageLimits.MaxBytes / (1024 * 1024)}MB.";
            return RedirectToAction(nameof(Index), new { productId });
        }

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, cancellationToken);

        // file.FileName is display only. It never reaches a path — the store
        // builds the filename from the content hash.
        var result = await _artworkLogic.AddFromUploadAsync(
            buffer.ToArray(), file.FileName, customerId: null, approvedByUserId: StudioUserId(), cancellationToken);

        return await AfterArtworkAddedAsync(result, side, productId);
    }

    // POST /Designer/RemoveSide
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult RemoveSide(string side, int productId)
    {
        SetSessionArtwork(NormaliseSide(side), null);
        return RedirectToAction(nameof(Index), new { productId });
    }

    // POST /Designer/Save — turns the in-progress design into a Design row and
    // sends the visitor to the order form.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(DesignerViewModel model)
    {
        var front = BuildPlacement(GetSessionArtwork("front"), model.Front);
        var back = BuildPlacement(GetSessionArtwork("back"), model.Back);

        if (front == null && back == null)
        {
            TempData["DesignerError"] = "Add an image to the front or the back first.";
            return RedirectToAction(nameof(Index), new { productId = model.ProductId });
        }

        if (string.IsNullOrWhiteSpace(model.Name))
            model.Name = "My design";

        // Who's driving decides what this becomes. Signed-in staff are building
        // the studio's own catalogue; anyone else is designing their own shirt
        // and goes on to check out.
        var isStudio = StudioUserId().HasValue;

        var result = await _designLogic.CreateAsync(new DesignDetails(
            Name: model.Name,
            ProductId: model.ProductId,
            CustomerId: null,
            Front: front,
            Back: back,
            IsStudioDesign: isStudio));

        if (!result.Success)
        {
            TempData["DesignerError"] = result.ErrorMessage;
            return RedirectToAction(nameof(Index), new { productId = model.ProductId });
        }

        // Clear the session so the next design starts from a blank shirt rather
        // than inheriting the artwork just saved.
        SetSessionArtwork("front", null);
        SetSessionArtwork("back", null);

        if (isStudio)
        {
            TempData["StudioSuccess"] = $"\"{model.Name.Trim()}\" is in the shop.";
            return RedirectToAction("Manage", "Shop");
        }

        return RedirectToAction("Place", "Orders", new { designId = result.DesignId });
    }

    // The signed-in staff member, when there is one.
    //
    // Used for two things that travel together: marking a new design as the
    // studio's own, and approving its artwork on arrival. Both are gated on the
    // same fact — a member of staff is the one adding it — so they can't drift
    // apart into a studio design whose artwork is still waiting for review.
    private int? StudioUserId() =>
        User.Identity?.IsAuthenticated == true ? User.UserId() : null;

    // ---- internals ----

    private async Task<IActionResult> AfterArtworkAddedAsync(ArtworkResult result, string side, int productId)
    {
        if (!result.Success)
        {
            TempData["DesignerError"] = result.ErrorMessage;
            return RedirectToAction(nameof(Index), new { productId });
        }

        var artwork = await _artworkLogic.GetByIdAsync(result.ArtworkId);

        // A hash match against something a moderator already rejected. Telling
        // the visitor now is far better than letting them lay it out, name it,
        // fill in the order form and be refused at the end.
        if (artwork?.Status == ArtworkStatus.Rejected)
        {
            TempData["DesignerError"] =
                $"We can't print that image: {artwork.RejectionReason}";

            return RedirectToAction(nameof(Index), new { productId });
        }

        SetSessionArtwork(NormaliseSide(side), result.ArtworkId);

        // Staff are the reviewer, so telling them it's awaiting review would be
        // nonsense — theirs is approved the moment it lands.
        TempData["DesignerPending"] = StudioUserId().HasValue
            ? "Added and approved — it's yours, so there's nothing to review."
            : "Added. Every image gets a quick look from us before it goes to the press, " +
              "so you can place the order now and we'll confirm shortly.";

        return RedirectToAction(nameof(Index), new { productId });
    }

    private async Task HydrateSideAsync(SidePlacementViewModel sideModel, int? artworkId)
    {
        if (artworkId == null)
            return;

        var artwork = await _artworkLogic.GetByIdAsync(artworkId.Value);
        if (artwork == null)
            return;

        sideModel.ArtworkId = artwork.ArtworkId;
        sideModel.ImageUrl = _imageStore.PublicPath(artwork.StoredFileName);
        sideModel.ImageWidthPx = artwork.WidthPx;
        sideModel.ImageHeightPx = artwork.HeightPx;
        sideModel.Status = artwork.Status;
        sideModel.RejectionReason = artwork.RejectionReason;
    }

    // Starting placement: as wide as the print area allows without exceeding the
    // image's own 150-DPI ceiling, centred, a little down from the collar.
    // Chosen so the first thing a visitor sees is a sensible layout rather than
    // an image jammed into a corner.
    private static void DefaultPlacements(DesignerViewModel model, ProductRowViewModel product)
    {
        foreach (var side in new[] { model.Front, model.Back })
        {
            if (!side.HasArtwork || side.WidthMm > 0)
                continue;

            var maxByQuality = side.ImageWidthPx.HasValue
                ? ImageLimits.MaxPrintableWidthMm(side.ImageWidthPx.Value)
                : product.PrintAreaWidthMm;

            var width = Math.Min(product.PrintAreaWidthMm, Math.Max(20, maxByQuality));

            var aspect = side.ImageWidthPx is > 0 && side.ImageHeightPx is > 0
                ? (double)side.ImageHeightPx.Value / side.ImageWidthPx.Value
                : 1.0;

            var height = (int)Math.Round(width * aspect);

            // Keep it inside the box even for very tall images.
            if (height > product.PrintAreaHeightMm)
            {
                height = product.PrintAreaHeightMm;
                width = (int)Math.Round(height / aspect);
            }

            side.WidthMm = width;
            side.HeightMm = height;
            side.XMm = Math.Max(0, (product.PrintAreaWidthMm - width) / 2);
            side.YMm = Math.Max(0, Math.Min(30, product.PrintAreaHeightMm - height));
        }
    }

    private static Placement? BuildPlacement(int? artworkId, SidePlacementViewModel side)
    {
        if (artworkId == null)
            return null;

        return new Placement(artworkId.Value, side.XMm, side.YMm, side.WidthMm, side.HeightMm);
    }

    private async Task<IList<ProductRowViewModel>> LoadProductsAsync()
    {
        var products = await _productLogic.GetActiveAsync();

        return products.Select(p => new ProductRowViewModel
        {
            Id = p.ProductId,
            Name = p.Name,
            Description = p.Description,
            Colour = p.Colour,
            ColourHex = p.ColourHex,
            BasePrice = p.BasePrice,
            PrintSidePrice = p.PrintSidePrice,
            PrintAreaWidthMm = p.PrintAreaWidthMm,
            PrintAreaHeightMm = p.PrintAreaHeightMm,
            Sizes = p.Sizes,
            ExtendedSizeUpcharge = p.ExtendedSizeUpcharge,
            IsActive = p.IsActive
        }).ToList();
    }

    private static string NormaliseSide(string? side) =>
        string.Equals(side, "back", StringComparison.OrdinalIgnoreCase) ? "back" : "front";

    private int? GetSessionArtwork(string side)
    {
        var value = HttpContext.Session.GetInt32($"{SessionDesignKey}.{side}");
        return value == 0 ? null : value;
    }

    private void SetSessionArtwork(string side, int? artworkId)
    {
        var key = $"{SessionDesignKey}.{side}";

        if (artworkId == null)
            HttpContext.Session.Remove(key);
        else
            HttpContext.Session.SetInt32(key, artworkId.Value);
    }
}
