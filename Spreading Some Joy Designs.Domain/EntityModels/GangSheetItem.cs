namespace SpreadingJoy.Domain.EntityModels;

// One transfer on a sheet: one image, printed once, at one size.
//
// One row per physical copy, not a row with a quantity. Twelve shirts needing
// the same front is twelve rows, because twelve of them have to be somewhere on
// the film and each one has its own position and its own cut. A quantity column
// would have meant the layout couldn't describe itself.
//
// The size is copied here rather than read back through the design, for the
// same reason OrderLines.UnitPrice is copied: somebody resizing artwork in the
// designer next week must not silently restate a sheet that has already been
// packed — or printed.
public partial class GangSheetItem
{
    public int GangSheetItemId { get; set; }

    public int GangSheetId { get; set; }

    // What gets printed. The artwork, not the design — a design is a garment
    // with pictures placed on it, and none of that reaches the film.
    public int ArtworkId { get; set; }

    // Where this came from, when it came from an order. Null for a transfer
    // added by hand: a reprint, a sample, a spare.
    public int? OrderLineId { get; set; }

    public int? DesignId { get; set; }

    // 'Front' or 'Back'. A two-sided design is two transfers, and the cut list
    // has to say which is which or they get pressed on the wrong face.
    public string Side { get; set; } = GangSheetSide.Front;

    // What to write on the cut list. Denormalised on purpose: it has to survive
    // the design being renamed and the order being completed, because it is
    // read off a piece of paper next to a heat press.
    public string Label { get; set; } = null!;

    public int WidthMm { get; set; }

    public int HeightMm { get; set; }

    // Where the packer put it, from the top-left of the film. Meaningless
    // unless IsPlaced.
    public int XMm { get; set; }

    public int YMm { get; set; }

    // Turned 90° to make it fit. Swaps the printed width and height.
    public bool Rotated { get; set; }

    // Whether the packer found room for it. An item that didn't fit stays on
    // the sheet rather than being dropped — the screen has to be able to say
    // which four things need a second sheet, and silently losing them is how a
    // customer's order goes missing.
    public bool IsPlaced { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual GangSheet GangSheet { get; set; } = null!;

    public virtual Artwork Artwork { get; set; } = null!;

    public virtual OrderLine? OrderLine { get; set; }

    public virtual Design? Design { get; set; }

    // ---- Computed in C#, not columns. ----

    // The footprint on the film, which is the stored size with the rotation
    // applied. Every layout and coverage sum reads these rather than the raw
    // pair, so a rotated transfer can't be drawn one way and cut another.
    public int PlacedWidthMm => Rotated ? HeightMm : WidthMm;

    public int PlacedHeightMm => Rotated ? WidthMm : HeightMm;
}

// Which face of the garment this transfer is destined for.
public static class GangSheetSide
{
    public const string Front = "Front";
    public const string Back = "Back";

    // No particular face. A transfer on a sheet a customer built and bought
    // isn't destined for one — they cut it out and press it wherever they like,
    // and there is no order line here that knows better.
    //
    // A third value rather than storing these as 'Front', because a cut list
    // that says "front" about a transfer nobody has decided the front of is
    // telling the person at the bench something untrue.
    public const string Any = "Any";

    public static readonly string[] All = [Front, Back, Any];

    public static bool IsKnown(string? side) => All.Contains(side);
}
