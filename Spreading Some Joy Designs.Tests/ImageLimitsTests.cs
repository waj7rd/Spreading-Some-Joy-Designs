namespace SpreadingJoy.Tests;

// Print quality is a property of the image *and* the size it's printed at,
// never of the image alone. These lock that in.
public class ImageLimitsTests
{
    [Fact]
    public void Effective_dpi_falls_as_the_print_gets_bigger()
    {
        // 254mm is exactly 10 inches and 762mm exactly 30, so these divide
        // cleanly: the same 3000px file is a crisp 300 DPI across a chest and an
        // unusable 100 across a banner.
        Assert.Equal(300, ImageLimits.EffectiveDpi(3000, 254));
        Assert.Equal(100, ImageLimits.EffectiveDpi(3000, 762));
    }

    [Fact]
    public void Effective_dpi_of_a_zero_width_print_is_zero_rather_than_a_divide_by_zero()
    {
        Assert.Equal(0, ImageLimits.EffectiveDpi(1000, 0));
        Assert.Equal(0, ImageLimits.EffectiveDpi(1000, -5));
    }

    [Fact]
    public void Max_printable_width_is_the_inverse_of_effective_dpi()
    {
        var maxWidth = ImageLimits.MaxPrintableWidthMm(1000);

        Assert.Equal(169, maxWidth);
        Assert.True(ImageLimits.EffectiveDpi(1000, maxWidth) >= ImageLimits.MinimumDpi);
    }

    [Fact]
    public void A_large_image_printed_small_passes()
    {
        Assert.Null(ImageLimits.CheckPrintQuality(3000, 3000, 200, 200));
    }

    [Fact]
    public void A_small_image_printed_large_is_refused_with_a_usable_alternative()
    {
        var message = ImageLimits.CheckPrintQuality(400, 400, 300, 300);

        Assert.NotNull(message);

        // The refusal has to say what *would* work, or the customer is stuck
        // guessing at sizes until one is accepted.
        Assert.Contains("would work up to about", message);
    }

    [Fact]
    public void Quality_is_judged_on_the_worse_of_the_two_axes()
    {
        // Wide and short: fine horizontally, hopeless vertically. Judging on
        // width alone would pass a print that comes out visibly stretched-soft.
        var message = ImageLimits.CheckPrintQuality(4000, 200, 300, 300);

        Assert.NotNull(message);
    }

    [Fact]
    public void Exactly_at_the_minimum_dpi_is_accepted()
    {
        // 150 DPI over 100mm needs 590.5px, so 591 clears it and 590 doesn't.
        Assert.Null(ImageLimits.CheckPrintQuality(591, 591, 100, 100));
        Assert.NotNull(ImageLimits.CheckPrintQuality(590, 590, 100, 100));
    }
}
