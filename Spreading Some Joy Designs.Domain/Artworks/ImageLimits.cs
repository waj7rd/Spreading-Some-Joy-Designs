namespace SpreadingJoy.Domain.Artworks;

// What counts as a usable image, and what counts as a printable one. Two
// different questions: the first is about the file, the second is about how big
// it ends up on the shirt.
public static class ImageLimits
{
    // Big enough for a full-front print at 300 DPI, small enough that a stray
    // 200MB TIFF doesn't take the server's memory with it. Enforced on the
    // declared length before reading and on the actual bytes after, because a
    // Content-Length header is a claim, not a fact.
    public const long MaxBytes = 25 * 1024 * 1024;

    // Below this it's a favicon or a tracking pixel, not artwork.
    public const int MinDimensionPx = 100;

    // A decompression bomb is a small file that becomes an enormous bitmap.
    // Capping the pixel count catches it before the decoder allocates: a
    // 50,000 x 50,000 PNG is under a megabyte on disk and 10GB in memory.
    public const long MaxPixels = 80_000_000;

    // What we'll actually decode. The list is short on purpose — every format
    // here is one whose decoder we're choosing to expose to hostile input.
    public static readonly string[] AllowedContentTypes =
        ["image/png", "image/jpeg", "image/gif", "image/webp"];

    // The resolution the designer starts warning at, in dots per inch, measured
    // at the size the artwork is actually placed at. 300 is what a print shop
    // wants; 150 is what it will grudgingly run.
    //
    // Nothing here refuses a placement for being under it. The designer shows a
    // live DPI readout and a warning below this line, and Karrie reviews the
    // artwork before it goes to press — she makes the call on whether the
    // resolution is good enough for that particular job. A soft warning she can
    // overrule beats a hard block she has to work around.
    public const int MinimumDpi = 150;

    private const double MmPerInch = 25.4;

    // Effective resolution of an image at a given printed width.
    //
    // The same 1000px image is a crisp 300 DPI at 85mm across and an unusable
    // 42 DPI at 600mm. Resolution is not a property of the file — it's a
    // property of the file and the size together, which is why this can't be
    // checked at upload time.
    public static int EffectiveDpi(int pixels, int printedMm)
    {
        if (printedMm <= 0)
            return 0;

        return (int)Math.Floor(pixels / (printedMm / MmPerInch));
    }

    // The widest this image can be printed and still hold the given DPI. Shown
    // next to the artwork so the customer is told what size would work, rather
    // than left to guess at sizes.
    public static int MaxPrintableWidthMm(int pixels, int dpi = MinimumDpi)
    {
        if (dpi <= 0)
            return 0;

        return (int)Math.Floor(pixels / (double)dpi * MmPerInch);
    }
}
