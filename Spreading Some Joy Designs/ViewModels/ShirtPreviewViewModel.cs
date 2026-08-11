namespace SpreadingJoy.ViewModels;

// One side of a garment, rendered as a mock-up.
//
// Shared by the designer and the checkout screen so the customer sees the same
// picture in both places. Two copies of this rendering would drift, and the
// place they'd drift is the one that matters — somebody agreeing to buy
// something that looks different from what they laid out.
public class ShirtPreviewViewModel
{
    public string Side { get; set; } = "front";

    public string ColourHex { get; set; } = "#ffffff";

    public int PrintAreaWidthMm { get; set; }

    public int PrintAreaHeightMm { get; set; }

    public string? ImageUrl { get; set; }

    // Millimetres from the top-left of the print area. The view converts these
    // to percentages so the mock-up stays correct at any rendered width.
    public int XMm { get; set; }
    public int YMm { get; set; }
    public int WidthMm { get; set; }
    public int HeightMm { get; set; }

    // Drawn with a dashed outline when the artwork hasn't been reviewed yet.
    public bool IsPending { get; set; }

    public bool ShowPrintAreaSize { get; set; } = true;

    // The artwork's own pixel dimensions, needed to lock the aspect ratio while
    // resizing and to work out the effective DPI at whatever size it lands on.
    public int ImageWidthPx { get; set; }
    public int ImageHeightPx { get; set; }

    // Draggable and resizable. True in the designer, false at checkout — where
    // the customer is confirming a decision, not still making it.
    public bool Interactive { get; set; }

    // Mirrors the rules the Domain enforces, so the browser refuses the same
    // things the server would rather than letting someone lay out a design that
    // gets rejected on save.
    public int MinPlacementMm { get; set; } = 20;
    public int MinimumDpi { get; set; } = 150;

    // The widest this particular image can be printed and still clear
    // MinimumDpi — the same figure DesignLogic refuses on. Derived here rather
    // than recomputed in JavaScript so there is one definition of the rule and
    // the browser cannot drift from the server.
    //
    // Zero when the artwork's dimensions aren't known, which the designer reads
    // as "no limit" — better to allow a placement the server may reject than to
    // silently pin the artwork to nothing on missing data.
    public int MaxPrintWidthMm =>
        ImageWidthPx > 0 && MinimumDpi > 0
            ? Domain.Artworks.ImageLimits.MaxPrintableWidthMm(ImageWidthPx, MinimumDpi)
            : 0;

    public bool HasArtwork => !string.IsNullOrEmpty(ImageUrl);

    // ---- artwork position, as a percentage of the print area ----

    private static string Pct(double value) =>
        value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "%";

    private static string Pct(int value, int total) =>
        Pct(total <= 0 ? 0 : value * 100.0 / total);

    public string LeftPct => Pct(XMm, PrintAreaWidthMm);
    public string TopPct => Pct(YMm, PrintAreaHeightMm);
    public string WidthPct => Pct(WidthMm, PrintAreaWidthMm);
    public string HeightPct => Pct(HeightMm, PrintAreaHeightMm);

    // ---- where the print area sits on the garment ----
    //
    // The shirt is drawn from a 400 x 460 viewBox, so the container is taller
    // than it is wide. That ratio has to be divided back out when converting a
    // real-world print-area shape into CSS percentages, or a square print area
    // would render as a rectangle.

    private const double ShirtAspect = 460.0 / 400.0;

    // A 305mm print area — a standard adult full-front — is drawn at this share
    // of the garment's width. Everything else scales against it, so a hoodie's
    // smaller 254mm area genuinely looks smaller rather than being stretched to
    // fill the same box.
    private const double ReferencePrintWidthMm = 305.0;
    private const double ReferenceWidthPct = 40.0;

    public string PrintAreaWidthPct => Pct(AreaWidthPct);

    public string PrintAreaHeightPct => Pct(AreaHeightPct);

    public string PrintAreaLeftPct => Pct((100 - AreaWidthPct) / 2);

    // Just below the collar, where a chest print actually goes.
    public string PrintAreaTopPct => "20%";

    private double AreaWidthPct => PrintAreaWidthMm <= 0
        ? ReferenceWidthPct
        : Math.Clamp(ReferenceWidthPct * PrintAreaWidthMm / ReferencePrintWidthMm, 12, 46);

    private double AreaHeightPct => PrintAreaWidthMm <= 0 || PrintAreaHeightMm <= 0
        ? ReferenceWidthPct
        : AreaWidthPct * ((double)PrintAreaHeightMm / PrintAreaWidthMm) / ShirtAspect;
}
