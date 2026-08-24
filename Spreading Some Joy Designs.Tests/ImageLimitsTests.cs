namespace SpreadingJoy.Tests;

// Print quality is a property of the image *and* the size it's printed at,
// never of the image alone. These lock that in.
//
// Nothing in ImageLimits refuses a placement for being under MinimumDpi — the
// warning is the designer's, and the call is Karrie's. The arithmetic those two
// lean on is what's tested here; that low resolution no longer blocks a save is
// covered in DesignLogicTests.
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
    public void Max_printable_width_of_a_zero_dpi_target_is_zero_rather_than_a_divide_by_zero()
    {
        Assert.Equal(0, ImageLimits.MaxPrintableWidthMm(1000, 0));
        Assert.Equal(0, ImageLimits.MaxPrintableWidthMm(1000, -5));
    }
}
