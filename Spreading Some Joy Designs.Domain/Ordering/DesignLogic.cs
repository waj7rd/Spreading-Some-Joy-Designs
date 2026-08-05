using SpreadingJoy.Domain.Artworks;
using SpreadingJoy.Domain.EntityModels;
using SpreadingJoy.Domain.IRepositories;
using SpreadingJoy.Domain.Shared;

namespace SpreadingJoy.Domain.Ordering;

public class DesignLogic : IDesignLogic
{
    // Below this the print is a smudge and the press operator will ring the
    // customer anyway.
    private const int MinPlacementMm = 20;

    private readonly IDesignRepository _designRepository;
    private readonly IProductRepository _productRepository;
    private readonly IArtworkRepository _artworkRepository;
    private readonly IStudioClock _clock;

    public DesignLogic(
        IDesignRepository designRepository,
        IProductRepository productRepository,
        IArtworkRepository artworkRepository,
        IStudioClock clock)
    {
        _designRepository = designRepository;
        _productRepository = productRepository;
        _artworkRepository = artworkRepository;
        _clock = clock;
    }

    public async Task<Design?> GetByIdAsync(int designId) =>
        await _designRepository.GetWithArtworkAsync(designId);

    public async Task<IList<Design>> GetForCustomerAsync(int customerId)
    {
        var designs = await _designRepository.FindByAsync(d => d.CustomerId == customerId && d.IsActive);
        return designs.OrderByDescending(d => d.CreatedAt).ToList();
    }

    public async Task<DesignResult> CreateAsync(DesignDetails details)
    {
        var (error, product) = await ValidateAsync(details);
        if (error != null)
            return DesignResult.Fail(error);

        var design = new Design
        {
            ProductId = product!.ProductId,
            CustomerId = details.CustomerId,
            Name = details.Name.Trim(),
            IsActive = true,
            CreatedAt = _clock.UtcNow
        };

        ApplyPlacements(design, details);

        await _designRepository.AddAsync(design);
        await _designRepository.SaveChangesAsync();

        return DesignResult.Ok(design.DesignId);
    }

    public async Task<DesignResult> UpdateAsync(int designId, DesignDetails details)
    {
        var design = await _designRepository.GetAsync(d => d.DesignId == designId);
        if (design == null)
            return DesignResult.Fail("Design not found.");

        var (error, product) = await ValidateAsync(details);
        if (error != null)
            return DesignResult.Fail(error);

        design.ProductId = product!.ProductId;
        design.Name = details.Name.Trim();
        ApplyPlacements(design, details);

        await _designRepository.SaveChangesAsync();
        return DesignResult.Ok(design.DesignId);
    }

    public async Task<DesignResult> SetActiveAsync(int designId, bool isActive)
    {
        var design = await _designRepository.GetAsync(d => d.DesignId == designId);
        if (design == null)
            return DesignResult.Fail("Design not found.");

        design.IsActive = isActive;
        await _designRepository.SaveChangesAsync();

        return DesignResult.Ok(design.DesignId);
    }

    public async Task<DesignResult> ValidateForOrderAsync(int designId)
    {
        var design = await _designRepository.GetWithArtworkAsync(designId);
        if (design == null)
            return DesignResult.Fail("Design not found.");

        if (!design.IsActive)
            return DesignResult.Fail("That design has been archived.");

        var product = await _productRepository.GetAsync(p => p.ProductId == design.ProductId);
        if (product == null)
            return DesignResult.Fail("The garment this design uses no longer exists.");

        if (!product.IsActive)
            return DesignResult.Fail($"The {product.Colour} {product.Name} is no longer available.");

        if (design.FrontArtworkId == null && design.BackArtworkId == null)
            return DesignResult.Fail("That design has no artwork on it.");

        // Re-check both sides against the product as it is now. A design saved
        // last month against a 300mm print area is not automatically valid
        // against today's 250mm one.
        foreach (var side in Sides(design))
        {
            // The navigation property when the caller loaded it, a lookup
            // otherwise. Taken from the side itself rather than inferred by
            // comparing ids — the same artwork on both sides would make that
            // comparison pick the front twice.
            var artwork = side.Artwork
                ?? await _artworkRepository.GetAsync(a => a.ArtworkId == side.ArtworkId);

            if (artwork == null)
                return DesignResult.Fail($"The {side.Label} artwork is missing.");

            // The gate the whole moderation queue exists for. Pending is a
            // refusal too — "not yet reviewed" is not "approved".
            if (artwork.Status != ArtworkStatus.Approved)
            {
                return artwork.Status == ArtworkStatus.Rejected
                    ? DesignResult.Fail($"The {side.Label} artwork was rejected: {artwork.RejectionReason}")
                    : DesignResult.Fail($"The {side.Label} artwork is still waiting to be reviewed.");
            }

            var placementError = CheckPlacement(product, side.Placement, artwork, side.Label);
            if (placementError != null)
                return DesignResult.Fail(placementError);
        }

        return DesignResult.Ok(design.DesignId);
    }

    // ---- internals ----

    private async Task<(string? Error, Product? Product)> ValidateAsync(DesignDetails details)
    {
        if (string.IsNullOrWhiteSpace(details.Name))
            return ("Give the design a name.", null);

        var product = await _productRepository.GetAsync(p => p.ProductId == details.ProductId);
        if (product == null)
            return ("Pick a garment.", null);

        if (!product.IsActive)
            return ($"The {product.Colour} {product.Name} is no longer available.", null);

        // A shirt with nothing on either side is a blank, which the studio
        // sells differently and isn't what the designer is for.
        if (details.Front == null && details.Back == null)
            return ("Put artwork on at least one side.", null);

        foreach (var (placement, label) in new[] { (details.Front, "front"), (details.Back, "back") })
        {
            if (placement == null)
                continue;

            var artwork = await _artworkRepository.GetAsync(a => a.ArtworkId == placement.ArtworkId);
            if (artwork == null)
                return ($"The {label} artwork couldn't be found.", null);

            if (artwork.Status == ArtworkStatus.Rejected)
                return ($"The {label} artwork was rejected: {artwork.RejectionReason}", null);

            var placementError = CheckPlacement(product, placement, artwork, label);
            if (placementError != null)
                return (placementError, null);
        }

        return (null, product);
    }

    // Every rule about where artwork may sit and how big it may be. Shared by
    // the save path and the order path so the two can't drift.
    private static string? CheckPlacement(Product product, Placement placement, Artwork artwork, string label)
    {
        if (placement.WidthMm < MinPlacementMm || placement.HeightMm < MinPlacementMm)
            return $"The {label} print is too small — {MinPlacementMm}mm is the smallest we run.";

        if (placement.XMm < 0 || placement.YMm < 0)
            return $"The {label} artwork is off the edge of the print area.";

        if (placement.XMm + placement.WidthMm > product.PrintAreaWidthMm ||
            placement.YMm + placement.HeightMm > product.PrintAreaHeightMm)
        {
            return $"The {label} artwork doesn't fit the {product.PrintAreaWidthMm}×{product.PrintAreaHeightMm}mm " +
                   $"print area on this garment.";
        }

        // Resolution is judged at the size it's actually printed, not at upload.
        var quality = ImageLimits.CheckPrintQuality(
            artwork.WidthPx, artwork.HeightPx, placement.WidthMm, placement.HeightMm);

        return quality == null ? null : $"On the {label}: {quality}";
    }

    private static void ApplyPlacements(Design design, DesignDetails details)
    {
        design.FrontArtworkId = details.Front?.ArtworkId;
        design.FrontXMm = details.Front?.XMm;
        design.FrontYMm = details.Front?.YMm;
        design.FrontWidthMm = details.Front?.WidthMm;
        design.FrontHeightMm = details.Front?.HeightMm;

        design.BackArtworkId = details.Back?.ArtworkId;
        design.BackXMm = details.Back?.XMm;
        design.BackYMm = details.Back?.YMm;
        design.BackWidthMm = details.Back?.WidthMm;
        design.BackHeightMm = details.Back?.HeightMm;
    }

    // The sides that actually carry artwork, each with its placement and
    // whichever Artwork the caller had already loaded.
    private static IEnumerable<(int ArtworkId, Artwork? Artwork, Placement Placement, string Label)> Sides(Design design)
    {
        if (design.FrontArtworkId is int frontId)
        {
            yield return (frontId, design.FrontArtwork, new Placement(
                frontId,
                design.FrontXMm ?? 0,
                design.FrontYMm ?? 0,
                design.FrontWidthMm ?? 0,
                design.FrontHeightMm ?? 0), "front");
        }

        if (design.BackArtworkId is int backId)
        {
            yield return (backId, design.BackArtwork, new Placement(
                backId,
                design.BackXMm ?? 0,
                design.BackYMm ?? 0,
                design.BackWidthMm ?? 0,
                design.BackHeightMm ?? 0), "back");
        }
    }
}
