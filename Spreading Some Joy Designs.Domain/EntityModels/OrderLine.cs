namespace SpreadingJoy.Domain.EntityModels;

// One design, one size, some number of shirts.
//
// UnitPrice is a snapshot copied from the product when the order was placed,
// not a lookup. Re-pricing the catalogue must not restate what somebody already
// agreed to pay — the same reason the schema snapshots it rather than joining
// for it. The design's name is deliberately not copied: it stays resolvable
// through the FK, and a rename is usually a correction history should reflect.
public partial class OrderLine
{
    public int OrderLineId { get; set; }

    public int OrderId { get; set; }

    public int DesignId { get; set; }

    public string SizeCode { get; set; } = null!;

    public int Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public virtual Order Order { get; set; } = null!;

    public virtual Design Design { get; set; } = null!;

    // ---- Computed in C#, not a column. ----

    public decimal LineTotal => UnitPrice * Quantity;
}
