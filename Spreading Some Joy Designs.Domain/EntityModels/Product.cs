namespace SpreadingJoy.Domain.EntityModels;

// A blank garment the studio prints on: a particular style in a particular
// colour. "Unisex Heavy Cotton Tee — Black" is one Product; the same tee in
// white is another, because they cost different amounts to buy and print.
public partial class Product
{
    public int ProductId { get; set; }

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public string Colour { get; set; } = null!;

    // Hex, used to render the mock-up in the designer without shipping a
    // photograph of every colourway.
    public string ColourHex { get; set; } = "#ffffff";

    // What one shirt costs before size upcharges and before per-side printing.
    public decimal BasePrice { get; set; }

    // Charged once per printed side. A front-and-back design costs this twice.
    public decimal PrintSidePrice { get; set; }

    // The printable rectangle, in millimetres. Artwork placement is validated
    // against this, and it's what the designer draws as the dashed outline.
    public int PrintAreaWidthMm { get; set; }

    public int PrintAreaHeightMm { get; set; }

    // Comma-separated size codes in the order they should be offered.
    public string SizesRaw { get; set; } = "S,M,L,XL,2XL,3XL";

    // Flat surcharge applied to any size flagged as extended (see Sizes.cs).
    public decimal ExtendedSizeUpcharge { get; set; }

    public virtual ICollection<Design> Designs { get; set; } = new List<Design>();

    // ---- Computed in C#, not a column. ----

    public IReadOnlyList<string> Sizes =>
        SizesRaw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.ToUpperInvariant())
            .Distinct()
            .ToArray();
}
