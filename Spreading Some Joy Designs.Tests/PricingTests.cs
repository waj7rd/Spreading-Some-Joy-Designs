namespace SpreadingJoy.Tests;

// Pricing is the thing a customer will argue about, so it's a pure function and
// these are one-line tests.
public class PricingTests
{
    private static readonly Product Tee = new()
    {
        ProductId = 1,
        Name = "Heavy Cotton Tee",
        Colour = "Black",
        BasePrice = 13m,
        PrintSidePrice = 7m,
        ExtendedSizeUpcharge = 3m,
        SizesRaw = "S,M,L,XL,2XL,3XL"
    };

    private static Design Design(bool front = true, bool back = false) => new()
    {
        DesignId = 1,
        ProductId = 1,
        Name = "d",
        FrontArtworkId = front ? 1 : null,
        BackArtworkId = back ? 2 : null
    };

    [Fact]
    public void One_side_costs_the_blank_plus_one_pass()
    {
        Assert.Equal(20m, Pricing.UnitPrice(Tee, Design(front: true), "M"));
    }

    [Fact]
    public void Two_sides_cost_two_passes()
    {
        Assert.Equal(27m, Pricing.UnitPrice(Tee, Design(front: true, back: true), "M"));
    }

    [Fact]
    public void A_back_only_design_costs_the_same_as_a_front_only_one()
    {
        Assert.Equal(
            Pricing.UnitPrice(Tee, Design(front: true), "M"),
            Pricing.UnitPrice(Tee, Design(front: false, back: true), "M"));
    }

    [Theory]
    [InlineData("S")]
    [InlineData("M")]
    [InlineData("L")]
    [InlineData("XL")]
    public void Standard_sizes_carry_no_upcharge(string size)
    {
        Assert.Equal(20m, Pricing.UnitPrice(Tee, Design(), size));
    }

    [Theory]
    [InlineData("2XL")]
    [InlineData("3XL")]
    [InlineData("4XL")]
    public void Extended_sizes_carry_the_upcharge(string size)
    {
        Assert.Equal(23m, Pricing.UnitPrice(Tee, Design(), size));
    }

    [Fact]
    public void Size_codes_are_matched_case_insensitively()
    {
        Assert.Equal(23m, Pricing.UnitPrice(Tee, Design(), "2xl"));
    }

    [Fact]
    public void Prices_are_rounded_to_the_cent_away_from_zero()
    {
        var oddly = new Product
        {
            Name = "x", Colour = "y",
            BasePrice = 10.005m,
            PrintSidePrice = 0m,
            ExtendedSizeUpcharge = 0m,
            SizesRaw = "M"
        };

        // Banker's rounding would give 10.00 here, which quietly loses a cent on
        // every shirt.
        Assert.Equal(10.01m, Pricing.UnitPrice(oddly, Design(), "M"));
    }

    [Fact]
    public void Counts_printed_sides()
    {
        Assert.Equal(0, Pricing.PrintedSides(Design(front: false)));
        Assert.Equal(1, Pricing.PrintedSides(Design(front: true)));
        Assert.Equal(2, Pricing.PrintedSides(Design(front: true, back: true)));
    }
}
