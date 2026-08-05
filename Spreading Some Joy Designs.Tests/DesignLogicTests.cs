using SpreadingJoy.Tests.Fakes;

namespace SpreadingJoy.Tests;

public class DesignLogicTests
{
    private static readonly DateTime Now = new(2026, 8, 4, 10, 0, 0);

    private readonly FakeDesignRepository _designs = new();
    private readonly FakeProductRepository _products = new();
    private readonly FakeArtworkRepository _artworks = new();
    private readonly DesignLogic _logic;

    public DesignLogicTests()
    {
        _logic = new DesignLogic(_designs, _products, _artworks, new FixedStudioClock(Now));
    }

    private Product SeedProduct(int widthMm = 305, int heightMm = 406, bool isActive = true)
    {
        var product = new Product
        {
            ProductId = 1,
            Name = "Heavy Cotton Tee",
            Colour = "Black",
            ColourHex = "#1a1a1a",
            BasePrice = 13m,
            PrintSidePrice = 7m,
            PrintAreaWidthMm = widthMm,
            PrintAreaHeightMm = heightMm,
            SizesRaw = "S,M,L,XL,2XL",
            ExtendedSizeUpcharge = 3m,
            IsActive = isActive
        };

        _products.Seed(product);
        return product;
    }

    private Artwork SeedArtwork(int id = 1, string status = ArtworkStatus.Approved, int px = 3000)
    {
        var artwork = new Artwork
        {
            ArtworkId = id,
            StoredFileName = $"{id}.png",
            ContentType = "image/png",
            WidthPx = px,
            HeightPx = px,
            Sha256 = new string('a', 63) + id,
            Status = status,
            RejectionReason = status == ArtworkStatus.Rejected ? "Not yours to print." : null,
            CreatedAt = Now
        };

        _artworks.Seed(artwork);
        return artwork;
    }

    // ---- creating ----

    [Fact]
    public async Task Creates_a_front_only_design()
    {
        SeedProduct();
        SeedArtwork();

        var result = await _logic.CreateAsync(new DesignDetails(
            "My design", 1, null, new Placement(1, 50, 50, 200, 200), null));

        Assert.True(result.Success);

        var design = Assert.Single(_designs.All);
        Assert.Equal(1, design.FrontArtworkId);
        Assert.Null(design.BackArtworkId);
    }

    [Fact]
    public async Task A_design_with_nothing_on_either_side_is_refused()
    {
        SeedProduct();

        var result = await _logic.CreateAsync(new DesignDetails("Blank", 1, null, null, null));

        Assert.False(result.Success);
        Assert.Empty(_designs.All);
    }

    [Fact]
    public async Task Artwork_larger_than_the_print_area_is_refused()
    {
        SeedProduct(widthMm: 305, heightMm: 406);
        SeedArtwork();

        // 200mm wide starting 200mm from the left runs 95mm off the edge.
        var result = await _logic.CreateAsync(new DesignDetails(
            "Too wide", 1, null, new Placement(1, 200, 0, 200, 200), null));

        Assert.False(result.Success);
        Assert.Contains("doesn't fit", result.ErrorMessage);
    }

    [Fact]
    public async Task Artwork_placed_off_the_top_or_left_is_refused()
    {
        SeedProduct();
        SeedArtwork();

        var result = await _logic.CreateAsync(new DesignDetails(
            "Off the edge", 1, null, new Placement(1, -10, 0, 200, 200), null));

        Assert.False(result.Success);
    }

    [Fact]
    public async Task A_print_smaller_than_the_press_can_run_is_refused()
    {
        SeedProduct();
        SeedArtwork();

        var result = await _logic.CreateAsync(new DesignDetails(
            "Tiny", 1, null, new Placement(1, 0, 0, 5, 5), null));

        Assert.False(result.Success);
        Assert.Contains("too small", result.ErrorMessage);
    }

    [Fact]
    public async Task A_low_resolution_image_printed_large_is_refused()
    {
        SeedProduct();
        SeedArtwork(px: 300);

        var result = await _logic.CreateAsync(new DesignDetails(
            "Blurry", 1, null, new Placement(1, 0, 0, 300, 300), null));

        Assert.False(result.Success);
        Assert.Contains("DPI", result.ErrorMessage);
    }

    [Fact]
    public async Task The_same_image_printed_small_is_accepted()
    {
        SeedProduct();
        SeedArtwork(px: 300);

        var result = await _logic.CreateAsync(new DesignDetails(
            "Small but sharp", 1, null, new Placement(1, 0, 0, 40, 40), null));

        Assert.True(result.Success);
    }

    [Fact]
    public async Task Artwork_already_rejected_cannot_be_placed()
    {
        SeedProduct();
        SeedArtwork(status: ArtworkStatus.Rejected);

        var result = await _logic.CreateAsync(new DesignDetails(
            "Nope", 1, null, new Placement(1, 0, 0, 200, 200), null));

        Assert.False(result.Success);
        Assert.Contains("Not yours to print.", result.ErrorMessage);
    }

    [Fact]
    public async Task Pending_artwork_can_be_placed_but_not_ordered()
    {
        // Deliberate: somebody designing shouldn't be blocked while an image
        // waits in the queue. The gate is at order time.
        SeedProduct();
        SeedArtwork(status: ArtworkStatus.Pending);

        var created = await _logic.CreateAsync(new DesignDetails(
            "Waiting", 1, null, new Placement(1, 0, 0, 200, 200), null));

        Assert.True(created.Success);

        var forOrder = await _logic.ValidateForOrderAsync(created.DesignId);

        Assert.False(forOrder.Success);
        Assert.Contains("waiting to be reviewed", forOrder.ErrorMessage);
    }

    [Fact]
    public async Task A_design_on_an_archived_garment_is_refused()
    {
        SeedProduct(isActive: false);
        SeedArtwork();

        var result = await _logic.CreateAsync(new DesignDetails(
            "Discontinued", 1, null, new Placement(1, 0, 0, 200, 200), null));

        Assert.False(result.Success);
    }

    // ---- validating at order time ----

    [Fact]
    public async Task A_valid_design_passes_the_order_check()
    {
        var product = SeedProduct();
        var artwork = SeedArtwork();

        var created = await _logic.CreateAsync(new DesignDetails(
            "Fine", 1, null, new Placement(1, 0, 0, 200, 200), null));

        // Wire the navigation property the way the real repository's Include would.
        var design = _designs.All.Single();
        design.Product = product;
        design.FrontArtwork = artwork;

        var result = await _logic.ValidateForOrderAsync(created.DesignId);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task Artwork_rejected_after_the_design_was_saved_stops_the_order()
    {
        // The whole reason the check runs again at order time: the world moves
        // between saving a design and ordering it.
        var product = SeedProduct();
        var artwork = SeedArtwork(status: ArtworkStatus.Pending);

        var created = await _logic.CreateAsync(new DesignDetails(
            "Was fine", 1, null, new Placement(1, 0, 0, 200, 200), null));

        var design = _designs.All.Single();
        design.Product = product;
        design.FrontArtwork = artwork;

        artwork.Status = ArtworkStatus.Rejected;
        artwork.RejectionReason = "Someone else's logo.";

        var result = await _logic.ValidateForOrderAsync(created.DesignId);

        Assert.False(result.Success);
        Assert.Contains("Someone else's logo.", result.ErrorMessage);
    }

    [Fact]
    public async Task Shrinking_the_print_area_strands_a_design_at_order_time()
    {
        var product = SeedProduct(widthMm: 305, heightMm: 406);
        var artwork = SeedArtwork();

        var created = await _logic.CreateAsync(new DesignDetails(
            "Full width", 1, null, new Placement(1, 0, 0, 300, 300), null));

        var design = _designs.All.Single();
        design.Product = product;
        design.FrontArtwork = artwork;

        // The catalogue changes to a smaller garment print area.
        product.PrintAreaWidthMm = 250;

        var result = await _logic.ValidateForOrderAsync(created.DesignId);

        Assert.False(result.Success);
        Assert.Contains("doesn't fit", result.ErrorMessage);
    }

    [Fact]
    public async Task An_archived_design_cannot_be_ordered()
    {
        var product = SeedProduct();
        var artwork = SeedArtwork();

        var created = await _logic.CreateAsync(new DesignDetails(
            "Retired", 1, null, new Placement(1, 0, 0, 200, 200), null));

        var design = _designs.All.Single();
        design.Product = product;
        design.FrontArtwork = artwork;

        await _logic.SetActiveAsync(created.DesignId, false);

        var result = await _logic.ValidateForOrderAsync(created.DesignId);

        Assert.False(result.Success);
        Assert.Contains("archived", result.ErrorMessage);
    }

    [Fact]
    public async Task The_back_artwork_is_checked_on_its_own_terms()
    {
        // Regression guard: an earlier version picked the front navigation
        // property for both sides, so a bad back image passed whenever the
        // front was fine.
        var product = SeedProduct();
        var good = SeedArtwork(id: 1);
        var bad = SeedArtwork(id: 2, status: ArtworkStatus.Rejected);

        var created = await _logic.CreateAsync(new DesignDetails(
            "Two sides", 1, null,
            new Placement(1, 0, 0, 200, 200),
            new Placement(2, 0, 0, 200, 200)));

        // Creation is refused outright, because rejected artwork can't be placed.
        Assert.False(created.Success);

        // Same shape, but the back only turns bad afterwards.
        bad.Status = ArtworkStatus.Approved;
        bad.RejectionReason = null;

        var retry = await _logic.CreateAsync(new DesignDetails(
            "Two sides", 1, null,
            new Placement(1, 0, 0, 200, 200),
            new Placement(2, 0, 0, 200, 200)));

        Assert.True(retry.Success);

        var design = _designs.All.Single();
        design.Product = product;
        design.FrontArtwork = good;
        design.BackArtwork = bad;

        bad.Status = ArtworkStatus.Rejected;
        bad.RejectionReason = "Back image is a trademark.";

        var result = await _logic.ValidateForOrderAsync(design.DesignId);

        Assert.False(result.Success);
        Assert.Contains("back", result.ErrorMessage);
        Assert.Contains("trademark", result.ErrorMessage);
    }
}
