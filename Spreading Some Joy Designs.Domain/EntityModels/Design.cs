namespace SpreadingJoy.Domain.EntityModels;

// A finished shirt design: one garment, and up to two pieces of artwork placed
// on it. At least one side has to carry something — a design with neither is a
// blank shirt, which is a different product entirely.
//
// Placements are millimetres from the top-left of that side's print area, in
// the garment's own units rather than screen pixels. The designer works in
// pixels and converts on the way in; storing pixels would tie the record to
// whatever the customer's browser window happened to be that day.
public partial class Design
{
    public int DesignId { get; set; }

    // Null while a guest is still designing, set once the order is accepted.
    public int? CustomerId { get; set; }

    public int ProductId { get; set; }

    public string Name { get; set; } = null!;

    public int? FrontArtworkId { get; set; }

    public int? FrontXMm { get; set; }

    public int? FrontYMm { get; set; }

    public int? FrontWidthMm { get; set; }

    public int? FrontHeightMm { get; set; }

    public int? BackArtworkId { get; set; }

    public int? BackXMm { get; set; }

    public int? BackYMm { get; set; }

    public int? BackWidthMm { get; set; }

    public int? BackHeightMm { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Product Product { get; set; } = null!;

    public virtual Customer? Customer { get; set; }

    public virtual Artwork? FrontArtwork { get; set; }

    public virtual Artwork? BackArtwork { get; set; }

    public virtual ICollection<OrderLine> OrderLines { get; set; } = new List<OrderLine>();
}
