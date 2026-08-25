namespace SpreadingJoy.Domain.EntityModels;

// A sheet of transfer film with many separate transfers packed onto it.
//
// DTF film is bought by the width and charged by the length, so empty film is
// money burned. A gang sheet is the answer: everything the press owes this week
// goes onto as little film as possible, prints in one pass, and gets cut apart
// afterwards.
//
// Dimensions are millimetres, like every other measurement in this schema —
// print areas, artwork placements. The trade quotes film in inches (a "22 x 60")
// and the screens show both, but one unit is stored so nothing has to be
// converted before it can be compared.
public partial class GangSheet
{
    public int GangSheetId { get; set; }

    // What the studio calls it. "Week of the 3rd", "Reprints", "Ashley's order".
    public string Name { get; set; } = null!;

    // The film width. Fixed by what's on the roll, so it's a property of the
    // sheet rather than something the packer gets to choose.
    public int WidthMm { get; set; }

    // How long the sheet is allowed to get. A ceiling, not a target — the
    // packer reports what it actually used, and that's what gets paid for.
    public int MaxLengthMm { get; set; }

    // Space left between neighbouring transfers, so there's somewhere to put
    // the scissors without clipping the design next door.
    public int GutterMm { get; set; }

    // Unprinted border round the edge of the film. Feed rollers touch this.
    public int MarginMm { get; set; }

    // Whether the packer may turn a transfer 90° to make it fit. Off for
    // anything with a nap or a direction to it; on is the usual case.
    public bool AllowRotation { get; set; } = true;

    // Who it was built for, and therefore what it is. A studio sheet is
    // production tooling — this week's orders packed onto one piece of film. A
    // customer sheet is a thing somebody bought.
    //
    // Stored rather than inferred from "does it have a customer", because a
    // studio sheet later attributed to somebody would otherwise be
    // indistinguishable from one they ordered, and those are not the same thing.
    public string Origin { get; set; } = GangSheetOrigin.Studio;

    // Null until a customer sheet is accepted, and always null on a studio one.
    // Nothing anonymous ever sets this: a Customer row exists only once staff
    // have accepted the request.
    public int? CustomerId { get; set; }

    // Which sheet off the catalogue it was sold as, and what it cost. Null and
    // zero on a studio sheet — the studio doesn't sell film to itself.
    public int? GangSheetSizeId { get; set; }

    // Snapshotted at acceptance from the price the customer was quoted, never
    // read live off the catalogue. Same rule as OrderLines.UnitPrice.
    public decimal Price { get; set; }

    public string Status { get; set; } = GangSheetStatus.Draft;

    // What the packer actually used, in millimetres, written when the sheet was
    // last packed. Stored rather than recomputed on read: this is the number
    // the film is charged by, and the layout it came from is the one that got
    // printed.
    public int UsedLengthMm { get; set; }

    public int? CreatedByUserId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? PrintedAt { get; set; }

    public string? Notes { get; set; }

    public virtual User? CreatedByUser { get; set; }

    public virtual Customer? Customer { get; set; }

    public virtual GangSheetSize? GangSheetSize { get; set; }

    public virtual ICollection<GangSheetItem> Items { get; set; } = new List<GangSheetItem>();

    // ---- Computed in C#, not columns. Mapped as Ignore in the context. ----

    // A draft can be built on. Anything past that is a sheet somebody is
    // standing at a press with, and changing it underneath them would mean the
    // cut list and the film disagree.
    public bool IsEditable => Status == GangSheetStatus.Draft;

    public int PlacedCount => Items.Count(i => i.IsPlaced);

    public int UnplacedCount => Items.Count(i => !i.IsPlaced);

    // How much of the film the transfers actually cover. The number that says
    // whether this sheet was worth printing: 40% coverage is more than half the
    // film thrown away.
    public double CoveragePercent
    {
        get
        {
            var area = (double)WidthMm * UsedLengthMm;
            if (area <= 0)
                return 0;

            var used = Items.Where(i => i.IsPlaced).Sum(i => (double)i.PlacedWidthMm * i.PlacedHeightMm);
            return Math.Round(used / area * 100, 1);
        }
    }
}

// Where a sheet is. Deliberately short: film is either being built, waiting at
// the press, or spent.
//
// String constants rather than an enum, the same as OrderStatus and
// FulfilmentMethod — these land in an NVARCHAR column and get read in SQL.
public static class GangSheetStatus
{
    // Being packed. Items can be added and removed; every change repacks it.
    public const string Draft = "Draft";

    // Packed, checked, and locked. Waiting to go through the printer.
    public const string Ready = "Ready";

    // It ran. Kept as the record of what was on that piece of film.
    public const string Printed = "Printed";

    public static readonly string[] All = [Draft, Ready, Printed];
}

// Who a sheet was built for. Two words, because there are two situations and
// they behave differently: a studio sheet is packed from the production board
// and never has a price, a customer sheet arrived through the public builder
// and always does.
public static class GangSheetOrigin
{
    public const string Studio = "Studio";
    public const string Customer = "Customer";

    public static readonly string[] All = [Studio, Customer];
}
