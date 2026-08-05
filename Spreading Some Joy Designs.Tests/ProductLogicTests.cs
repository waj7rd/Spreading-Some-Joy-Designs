using SpreadingJoy.Tests.Fakes;

namespace SpreadingJoy.Tests;

public class ProductLogicTests
{
    private readonly FakeProductRepository _products = new();
    private readonly ProductLogic _logic;

    public ProductLogicTests()
    {
        _logic = new ProductLogic(_products);
    }

    private static ProductDetails Details(
        string name = "Heavy Cotton Tee",
        string colour = "Black",
        string hex = "#1a1a1a",
        decimal basePrice = 13m,
        decimal sidePrice = 7m,
        int widthMm = 305,
        int heightMm = 406,
        string[]? sizes = null,
        decimal upcharge = 3m) =>
        new(name, null, colour, hex, basePrice, sidePrice, widthMm, heightMm,
            sizes ?? ["S", "M", "L", "XL"], upcharge);

    [Fact]
    public async Task Creates_a_garment()
    {
        var result = await _logic.CreateAsync(Details());

        Assert.True(result.Success);
        Assert.Single(_products.All);
    }

    [Fact]
    public async Task The_same_garment_in_a_different_colour_is_a_separate_product()
    {
        await _logic.CreateAsync(Details(colour: "Black"));
        var second = await _logic.CreateAsync(Details(colour: "White", hex: "#ffffff"));

        Assert.True(second.Success);
        Assert.Equal(2, _products.All.Count);
    }

    [Fact]
    public async Task The_same_garment_in_the_same_colour_twice_is_refused()
    {
        await _logic.CreateAsync(Details());
        var duplicate = await _logic.CreateAsync(Details());

        Assert.False(duplicate.Success);
        Assert.Single(_products.All);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_garment_needs_a_name(string name)
    {
        Assert.False((await _logic.CreateAsync(Details(name: name))).Success);
    }

    [Theory]
    [InlineData("red")]
    [InlineData("#fff")]
    [InlineData("1a1a1a")]
    [InlineData("#1a1a1az")]
    public async Task The_swatch_has_to_be_a_hex_colour(string hex)
    {
        // The designer renders the garment from this value; anything else draws
        // a shirt with no colour at all.
        Assert.False((await _logic.CreateAsync(Details(hex: hex))).Success);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(700)]
    public async Task An_implausible_print_area_is_refused(int widthMm)
    {
        Assert.False((await _logic.CreateAsync(Details(widthMm: widthMm))).Success);
    }

    [Fact]
    public async Task A_negative_price_is_refused()
    {
        Assert.False((await _logic.CreateAsync(Details(basePrice: -1m))).Success);
    }

    [Fact]
    public async Task A_garment_has_to_come_in_at_least_one_size()
    {
        Assert.False((await _logic.CreateAsync(Details(sizes: []))).Success);
    }

    [Fact]
    public async Task A_size_the_studio_does_not_stock_is_refused()
    {
        Assert.False((await _logic.CreateAsync(Details(sizes: ["S", "XXXXL"]))).Success);
    }

    [Fact]
    public async Task Sizes_are_stored_in_the_order_people_expect_to_read_them()
    {
        // Alphabetical would put 2XL before L and S after M.
        await _logic.CreateAsync(Details(sizes: ["2XL", "S", "XL", "M"]));

        Assert.Equal("S,M,XL,2XL", _products.All.Single().SizesRaw);
    }

    [Fact]
    public async Task Sizes_are_normalised_to_upper_case()
    {
        await _logic.CreateAsync(Details(sizes: ["s", "m"]));

        Assert.Equal("S,M", _products.All.Single().SizesRaw);
    }

    [Fact]
    public async Task Archiving_the_last_garment_is_refused()
    {
        var created = await _logic.CreateAsync(Details());

        var result = await _logic.SetActiveAsync(created.ProductId, false);

        Assert.False(result.Success);
        Assert.True(_products.All.Single().IsActive);
    }

    [Fact]
    public async Task Archiving_is_allowed_when_another_garment_remains()
    {
        var first = await _logic.CreateAsync(Details(colour: "Black"));
        await _logic.CreateAsync(Details(colour: "White", hex: "#ffffff"));

        var result = await _logic.SetActiveAsync(first.ProductId, false);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task Archived_garments_are_off_the_active_list_but_still_in_the_catalogue()
    {
        var first = await _logic.CreateAsync(Details(colour: "Black"));
        await _logic.CreateAsync(Details(colour: "White", hex: "#ffffff"));
        await _logic.SetActiveAsync(first.ProductId, false);

        Assert.Single(await _logic.GetActiveAsync());
        Assert.Equal(2, (await _logic.GetAllAsync()).Count);
    }

    [Fact]
    public async Task Renaming_onto_another_garments_name_and_colour_is_refused()
    {
        await _logic.CreateAsync(Details(name: "Heavy Cotton Tee", colour: "Black"));
        var second = await _logic.CreateAsync(Details(name: "Soft-Wash Tee", colour: "Black"));

        var result = await _logic.UpdateAsync(second.ProductId,
            Details(name: "Heavy Cotton Tee", colour: "Black"));

        Assert.False(result.Success);
    }

    [Fact]
    public async Task A_garment_can_be_saved_over_its_own_name()
    {
        var created = await _logic.CreateAsync(Details());

        var result = await _logic.UpdateAsync(created.ProductId, Details(basePrice: 15m));

        Assert.True(result.Success);
        Assert.Equal(15m, _products.All.Single().BasePrice);
    }
}
