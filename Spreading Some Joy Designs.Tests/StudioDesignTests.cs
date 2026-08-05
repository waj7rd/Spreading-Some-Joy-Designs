using SpreadingJoy.Tests.Fakes;

namespace SpreadingJoy.Tests;

// The studio's own designs.
//
// The load-bearing claim is that these need no exception in the ordering rules:
// artwork added by staff is approved on arrival, so a studio design passes the
// same gate as a customer's. These tests exist to keep that true — if somebody
// later adds a bypass, the test asserting a studio design with unapproved
// artwork is still refused will catch it.
public class StudioDesignTests
{
    private static readonly DateTime Now = new(2026, 8, 4, 10, 0, 0);
    private const int StaffUserId = 9;

    private readonly FakeArtworkRepository _artworks = new();
    private readonly FakeDesignRepository _designs = new();
    private readonly FakeProductRepository _products = new();
    private readonly FakeImageStore _store = new();
    private readonly FixedStudioClock _clock = new(Now);

    private ArtworkLogic BuildArtworkLogic() =>
        new(_artworks,
            FakeImageFetcher.Returning([1, 2, 3]),
            FakeImageInspector.Returning(3000, 3000),
            _store,
            _clock);

    private DesignLogic BuildDesignLogic() =>
        new(_designs, _products, _artworks, _clock);

    private Product SeedProduct(bool isActive = true)
    {
        var product = new Product
        {
            ProductId = 1,
            Name = "Heavy Cotton Tee",
            Colour = "Black",
            ColourHex = "#1a1a1a",
            BasePrice = 13m,
            PrintSidePrice = 7m,
            PrintAreaWidthMm = 305,
            PrintAreaHeightMm = 406,
            SizesRaw = "S,M,L,XL",
            ExtendedSizeUpcharge = 3m,
            IsActive = isActive
        };

        _products.Seed(product);
        return product;
    }

    // ---- artwork approval on arrival ----

    [Fact]
    public async Task Artwork_added_by_staff_is_approved_on_arrival()
    {
        var logic = BuildArtworkLogic();

        var result = await logic.AddFromUploadAsync(
            [1, 2, 3], "our-design.png", customerId: null, approvedByUserId: StaffUserId);

        Assert.True(result.Success);

        var artwork = Assert.Single(_artworks.All);
        Assert.Equal(ArtworkStatus.Approved, artwork.Status);
        Assert.Equal(StaffUserId, artwork.ReviewedByUserId);
        Assert.Equal(Now, artwork.ReviewedAt);
    }

    [Fact]
    public async Task Artwork_added_by_a_customer_still_starts_pending()
    {
        var logic = BuildArtworkLogic();

        var result = await logic.AddFromUrlAsync(
            "https://example.com/cat.png", customerId: null, approvedByUserId: null);

        Assert.True(result.Success);

        var artwork = Assert.Single(_artworks.All);
        Assert.Equal(ArtworkStatus.Pending, artwork.Status);
        Assert.Null(artwork.ReviewedByUserId);
    }

    [Fact]
    public async Task Staff_re_uploading_a_rejected_image_does_not_un_reject_it()
    {
        // A considered rejection shouldn't be undone by dropping the same file
        // in again. Reversing one is what the Approve button is for, where it's
        // an explicit act by a named person.
        var logic = BuildArtworkLogic();

        var first = await logic.AddFromUrlAsync("https://example.com/cat.png", customerId: null);
        await logic.RejectAsync(first.ArtworkId, StaffUserId, "Someone else's photograph.");

        var again = await logic.AddFromUploadAsync(
            [1, 2, 3], "same-picture.png", customerId: null, approvedByUserId: StaffUserId);

        Assert.True(again.Success);
        Assert.True(again.WasDeduplicated);

        var artwork = Assert.Single(_artworks.All);
        Assert.Equal(ArtworkStatus.Rejected, artwork.Status);
    }

    // ---- the flag ----

    [Fact]
    public async Task A_design_made_by_staff_is_marked_as_the_studios_own()
    {
        SeedProduct();
        var artworkLogic = BuildArtworkLogic();
        var added = await artworkLogic.AddFromUploadAsync(
            [1, 2, 3], "ours.png", customerId: null, approvedByUserId: StaffUserId);

        var result = await BuildDesignLogic().CreateAsync(new DesignDetails(
            "Flamingo", 1, null, new Placement(added.ArtworkId, 0, 0, 200, 200), null,
            IsStudioDesign: true));

        Assert.True(result.Success);
        Assert.True(_designs.All.Single().IsStudioDesign);
    }

    [Fact]
    public async Task A_customers_design_is_not_marked_as_the_studios()
    {
        SeedProduct();
        var artworkLogic = BuildArtworkLogic();
        var added = await artworkLogic.AddFromUrlAsync("https://example.com/cat.png", customerId: null);

        // The public designer path passes the default.
        var result = await BuildDesignLogic().CreateAsync(new DesignDetails(
            "Mine", 1, null, new Placement(added.ArtworkId, 0, 0, 200, 200), null));

        Assert.True(result.Success);
        Assert.False(_designs.All.Single().IsStudioDesign);
    }

    // ---- the gate is not bypassed ----

    [Fact]
    public async Task A_studio_design_passes_the_normal_approval_gate()
    {
        var product = SeedProduct();
        var artworkLogic = BuildArtworkLogic();
        var added = await artworkLogic.AddFromUploadAsync(
            [1, 2, 3], "ours.png", customerId: null, approvedByUserId: StaffUserId);

        var designLogic = BuildDesignLogic();
        var created = await designLogic.CreateAsync(new DesignDetails(
            "Flamingo", 1, null, new Placement(added.ArtworkId, 0, 0, 200, 200), null,
            IsStudioDesign: true));

        var design = _designs.All.Single();
        design.Product = product;
        design.FrontArtwork = _artworks.All.Single();

        // Passes because the artwork is genuinely Approved, not because it's a
        // studio design.
        Assert.True((await designLogic.ValidateForOrderAsync(created.DesignId)).Success);
    }

    [Fact]
    public async Task A_studio_design_with_unapproved_artwork_is_still_refused()
    {
        // The regression guard. If somebody adds a "studio designs skip the
        // check" branch, this fails.
        var product = SeedProduct();
        var artworkLogic = BuildArtworkLogic();
        var added = await artworkLogic.AddFromUploadAsync(
            [1, 2, 3], "ours.png", customerId: null, approvedByUserId: StaffUserId);

        var designLogic = BuildDesignLogic();
        var created = await designLogic.CreateAsync(new DesignDetails(
            "Flamingo", 1, null, new Placement(added.ArtworkId, 0, 0, 200, 200), null,
            IsStudioDesign: true));

        var design = _designs.All.Single();
        design.Product = product;
        design.FrontArtwork = _artworks.All.Single();

        // Something later rejects it — a second look, a complaint.
        design.FrontArtwork.Status = ArtworkStatus.Rejected;
        design.FrontArtwork.RejectionReason = "Turned out to be licensed clip art.";

        var result = await designLogic.ValidateForOrderAsync(created.DesignId);

        Assert.False(result.Success);
        Assert.Contains("licensed clip art", result.ErrorMessage);
    }

    // ---- what the shop lists ----

    [Fact]
    public async Task The_shop_lists_only_the_studios_own_active_designs()
    {
        var product = SeedProduct();
        _designs.Seed(
            new Design { DesignId = 1, ProductId = 1, Name = "Ours", IsStudioDesign = true, IsActive = true, Product = product, CreatedAt = Now },
            new Design { DesignId = 2, ProductId = 1, Name = "Archived", IsStudioDesign = true, IsActive = false, Product = product, CreatedAt = Now },
            new Design { DesignId = 3, ProductId = 1, Name = "A customer's", IsStudioDesign = false, IsActive = true, Product = product, CreatedAt = Now });

        var shop = await BuildDesignLogic().GetStudioDesignsAsync();

        Assert.Equal("Ours", Assert.Single(shop).Name);
    }

    [Fact]
    public async Task The_shop_hides_designs_whose_garment_has_been_archived()
    {
        // Otherwise it stays orderable right up until OrderLogic refuses it at
        // the very last step, which reads as the site being broken.
        var product = SeedProduct(isActive: false);
        _designs.Seed(new Design
        {
            DesignId = 1, ProductId = 1, Name = "On a discontinued tee",
            IsStudioDesign = true, IsActive = true, Product = product, CreatedAt = Now
        });

        Assert.Empty(await BuildDesignLogic().GetStudioDesignsAsync());
    }

    [Fact]
    public async Task The_management_screen_shows_archived_studio_designs_too()
    {
        var product = SeedProduct();
        _designs.Seed(
            new Design { DesignId = 1, ProductId = 1, Name = "Ours", IsStudioDesign = true, IsActive = true, Product = product, CreatedAt = Now },
            new Design { DesignId = 2, ProductId = 1, Name = "Archived", IsStudioDesign = true, IsActive = false, Product = product, CreatedAt = Now },
            new Design { DesignId = 3, ProductId = 1, Name = "A customer's", IsStudioDesign = false, IsActive = true, Product = product, CreatedAt = Now });

        var all = await BuildDesignLogic().GetAllStudioDesignsAsync();

        Assert.Equal(2, all.Count);
        Assert.DoesNotContain(all, d => !d.IsStudioDesign);

        // Active first, so the working set is at the top.
        Assert.True(all[0].IsActive);
    }
}
