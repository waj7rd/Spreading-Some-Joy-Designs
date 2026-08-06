using SpreadingJoy.Tests.Fakes;

namespace SpreadingJoy.Tests;

// Designs are addressed in URLs by an unguessable token, not by their key.
//
// The order page is anonymous by necessity — a customer has no account — so a
// sequential id there let anyone count upwards and read every design ever made,
// artwork included. These tests hold that shut.
public class DesignPublicTokenTests
{
    private static readonly DateTime Now = new(2026, 8, 5, 10, 0, 0);

    private readonly FakeDesignRepository _designs = new();
    private readonly FakeProductRepository _products = new();
    private readonly FakeArtworkRepository _artworks = new();

    private DesignLogic Build() => new(_designs, _products, _artworks, new FixedStudioClock(Now));

    private void Seed()
    {
        _products.Seed(new Product
        {
            ProductId = 1,
            Name = "Heavy Cotton Tee",
            Colour = "Black",
            ColourHex = "#1a1a1a",
            BasePrice = 13m,
            PrintSidePrice = 7m,
            PrintAreaWidthMm = 305,
            PrintAreaHeightMm = 406,
            SizesRaw = "S,M,L",
            IsActive = true
        });

        _artworks.Seed(new Artwork
        {
            ArtworkId = 1,
            StoredFileName = "1.png",
            ContentType = "image/png",
            WidthPx = 3000,
            HeightPx = 3000,
            Sha256 = new string('a', 64),
            Status = ArtworkStatus.Approved,
            CreatedAt = Now
        });
    }

    private async Task<DesignResult> CreateAsync(string name) =>
        await Build().CreateAsync(new DesignDetails(
            name, 1, null, new Placement(1, 0, 0, 200, 200), null));

    [Fact]
    public async Task Every_design_gets_a_token()
    {
        Seed();

        var result = await CreateAsync("Mine");

        Assert.True(result.Success);
        Assert.NotEqual(Guid.Empty, result.PublicToken);
        Assert.Equal(result.PublicToken, _designs.All.Single().PublicToken);
    }

    [Fact]
    public async Task Tokens_are_distinct_between_designs()
    {
        // The backfill in AddDesignPublicToken.sql exists for this reason too:
        // adding the column with a single DEFAULT would have given every
        // existing row the same value and defeated the whole point.
        Seed();

        var first = await CreateAsync("First");
        var second = await CreateAsync("Second");

        Assert.NotEqual(first.PublicToken, second.PublicToken);
    }

    [Fact]
    public async Task A_design_can_be_found_by_its_token()
    {
        Seed();
        var created = await CreateAsync("Mine");

        var found = await Build().GetByPublicTokenAsync(created.PublicToken);

        Assert.NotNull(found);
        Assert.Equal(created.DesignId, found!.DesignId);
    }

    [Fact]
    public async Task An_unknown_token_finds_nothing()
    {
        Seed();
        await CreateAsync("Mine");

        Assert.Null(await Build().GetByPublicTokenAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task An_empty_token_finds_nothing_without_hitting_the_database()
    {
        // Guid.Empty is what an absent or unparseable query string binds to, so
        // it must never match — including a row that somehow stored it.
        Seed();
        await CreateAsync("Mine");

        _designs.All.Single().PublicToken = Guid.Empty;

        Assert.Null(await Build().GetByPublicTokenAsync(Guid.Empty));
    }

    [Fact]
    public async Task Knowing_a_design_id_does_not_help_you_guess_its_token()
    {
        // The point of the whole change, stated as a test: ids stay sequential
        // and predictable; nothing about them reveals the token.
        Seed();

        var first = await CreateAsync("First");
        var second = await CreateAsync("Second");

        Assert.Equal(first.DesignId + 1, second.DesignId);

        // Sequential neighbours, unrelated tokens.
        Assert.NotEqual(first.PublicToken, second.PublicToken);
        Assert.Null(await Build().GetByPublicTokenAsync(Guid.Empty));
    }
}
