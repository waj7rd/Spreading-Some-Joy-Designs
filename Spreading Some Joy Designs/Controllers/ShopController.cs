using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SpreadingJoy.Domain.EntityModels;
using SpreadingJoy.ViewModels;

namespace SpreadingJoy.Controllers;

// The studio's own designs.
//
// This is the low-risk half of the business: artwork the studio made itself, so
// there's no provenance question and nothing to moderate. Customers browse and
// order straight from here without touching the designer.
public class ShopController : Controller
{
    private readonly IDesignLogic _designLogic;
    private readonly IImageStore _imageStore;

    public ShopController(IDesignLogic designLogic, IImageStore imageStore)
    {
        _designLogic = designLogic;
        _imageStore = imageStore;
    }

    // GET /Shop — the public catalogue.
    public async Task<IActionResult> Index()
    {
        var designs = await _designLogic.GetStudioDesignsAsync();

        return View(new ShopViewModel
        {
            Designs = designs.Select(d => ToRow(d, forStaff: false)).ToList()
        });
    }

    // GET /Shop/Manage — staff view, archived included.
    [Authorize(Policy = Policies.ManageCatalog)]
    public async Task<IActionResult> Manage()
    {
        var designs = await _designLogic.GetAllStudioDesignsAsync();

        return View(new ShopViewModel
        {
            SuccessMessage = TempData["StudioSuccess"] as string,
            ErrorMessage = TempData["StudioError"] as string,
            Designs = designs.Select(d => ToRow(d, forStaff: true)).ToList()
        });
    }

    // POST /Shop/SetActive
    //
    // Archive rather than delete: a studio design is referenced by every order
    // line placed against it.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = Policies.ManageCatalog)]
    public async Task<IActionResult> SetActive(int id, bool isActive)
    {
        var design = await _designLogic.GetByIdAsync(id);

        // Scoped to studio designs on purpose. Without this check the action
        // would happily archive a customer's own design given its id.
        if (design == null || !design.IsStudioDesign)
            return NotFound();

        var result = await _designLogic.SetActiveAsync(id, isActive);

        if (!result.Success)
            TempData["StudioError"] = result.ErrorMessage;
        else
            TempData["StudioSuccess"] = isActive
                ? "Back in the shop."
                : "Taken out of the shop — past orders are unaffected.";

        return RedirectToAction(nameof(Manage));
    }

    private StudioDesignViewModel ToRow(Design design, bool forStaff)
    {
        var product = design.Product;

        var row = new StudioDesignViewModel
        {
            Id = design.DesignId,
            PublicToken = design.PublicToken,
            Name = design.Name,
            GarmentName = product == null ? "—" : $"{product.Colour} {product.Name}",
            ColourHex = product?.ColourHex ?? "#ffffff",
            Sizes = product?.Sizes ?? [],
            IsActive = design.IsActive,
            CreatedAt = design.CreatedAt,
            PrintedSides = Pricing.PrintedSides(design),

            // The smallest size, which is what "from" means on a listing.
            Price = product == null
                ? 0
                : Pricing.UnitPrice(product, design, product.Sizes.FirstOrDefault() ?? string.Empty)
        };

        if (product != null)
        {
            row.Front = BuildPreview(design, product, "front");
            row.Back = BuildPreview(design, product, "back");
        }

        if (forStaff)
            row.ArtworkStatusWarning = DescribeArtworkProblem(design);

        return row;
    }

    // Null when everything is approved and orderable.
    private static string? DescribeArtworkProblem(Design design)
    {
        foreach (var (artwork, label) in new[]
                 {
                     (design.FrontArtwork, "front"),
                     (design.BackArtwork, "back")
                 })
        {
            if (artwork == null)
                continue;

            if (artwork.Status == ArtworkStatus.Rejected)
                return $"The {label} artwork was rejected, so this can't be ordered.";

            if (artwork.Status == ArtworkStatus.Pending)
                return $"The {label} artwork is still awaiting review, so this can't be ordered yet.";
        }

        return null;
    }

    private ShirtPreviewViewModel BuildPreview(Design design, Product product, string side)
    {
        var isFront = side == "front";
        var artwork = isFront ? design.FrontArtwork : design.BackArtwork;

        return new ShirtPreviewViewModel
        {
            Side = side,
            ColourHex = product.ColourHex,
            PrintAreaWidthMm = product.PrintAreaWidthMm,
            PrintAreaHeightMm = product.PrintAreaHeightMm,
            ImageUrl = artwork == null ? null : _imageStore.PublicPath(artwork.StoredFileName),
            IsPending = artwork?.Status == ArtworkStatus.Pending,
            ShowPrintAreaSize = false,
            XMm = (isFront ? design.FrontXMm : design.BackXMm) ?? 0,
            YMm = (isFront ? design.FrontYMm : design.BackYMm) ?? 0,
            WidthMm = (isFront ? design.FrontWidthMm : design.BackWidthMm) ?? 0,
            HeightMm = (isFront ? design.FrontHeightMm : design.BackHeightMm) ?? 0
        };
    }
}
