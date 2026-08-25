namespace SpreadingJoy.Domain.EntityModels;

// A sheet of film the studio sells, at a price.
//
// This is the catalogue for gang sheets, and it is separate from Products
// because a gang sheet isn't a garment: there is no blank to buy, no size run,
// no per-side print charge. A customer buying one is buying film with their
// pictures on it, and the only thing that varies is how much film.
//
// Sold as fixed sizes rather than by the inch, because that is how film is
// bought from a supplier — a "22 x 60" is a thing you order — and because a
// price that moves while somebody is still arranging their images is a price
// they can't decide against.
public partial class GangSheetSize
{
    public int GangSheetSizeId { get; set; }

    // What the customer sees. "22 × 60 in", "Half sheet".
    public string Name { get; set; } = null!;

    public int WidthMm { get; set; }

    public int LengthMm { get; set; }

    // What the studio charges for one. Snapshotted onto the request when it is
    // submitted, the same rule OrderLines.UnitPrice follows — putting the price
    // up next month must not restate what somebody already agreed to.
    public decimal Price { get; set; }

    // Withdrawn rather than deleted, so a sheet already sold at this size keeps
    // resolving to the row it was sold under.
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }

    // ---- Computed in C#, not a column. ----

    public string Dimensions => $"{WidthMm} × {LengthMm} mm";
}
