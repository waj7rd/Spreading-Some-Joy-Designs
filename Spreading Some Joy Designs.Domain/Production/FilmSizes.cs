namespace SpreadingJoy.Domain.Production;

// The film the trade actually sells, and the conversion between what it's sold
// in and what this schema stores.
//
// DTF film comes off a roll of a fixed width and is charged by the length, and
// every supplier quotes both in inches. Everything here is stored in
// millimetres because that is what the print areas and artwork placements are
// in — a sheet whose width couldn't be compared against a print area without a
// conversion would be a unit bug waiting for a deadline.
public static class FilmSizes
{
    private const double MmPerInch = 25.4;

    // The roll widths worth offering. 22" is the common one; the narrower two
    // are what a smaller printer takes.
    public static readonly IReadOnlyList<int> WidthsInInches = [13, 16, 22, 24];

    // Sold lengths. A supplier's "22 x 60" is the second of these.
    public static readonly IReadOnlyList<int> LengthsInInches = [12, 24, 36, 48, 60, 120];

    public const int DefaultWidthInches = 22;
    public const int DefaultLengthInches = 60;

    // Enough to get scissors between two transfers without shaving either.
    public const int DefaultGutterMm = 6;

    // The unprinted border. The feed rollers touch this edge, and ink on it
    // ends up on the next sheet through.
    public const int DefaultMarginMm = 6;

    // Bounds on what the studio may type. A sheet narrower than this isn't film
    // and a sheet longer than this is a roll, not a sheet.
    public const int MinWidthMm = 100;
    public const int MaxWidthMm = 1000;
    public const int MinLengthMm = 100;
    public const int MaxLengthMm = 6000;

    // A transfer smaller than this is a speck nobody can cut out, and one
    // larger than this doesn't fit any garment the catalogue carries.
    public const int MinTransferMm = 10;
    public const int MaxTransferMm = 1000;

    public const int MaxGutterMm = 50;
    public const int MaxMarginMm = 50;

    public static int InchesToMm(double inches) => (int)Math.Round(inches * MmPerInch);

    public static double MmToInches(int mm) => Math.Round(mm / MmPerInch, 1);

    // "22 x 60 in" — how a sheet is described everywhere except in this
    // database. Shown next to the millimetres so an order to the film supplier
    // can be placed without anyone doing arithmetic.
    public static string Describe(int widthMm, int lengthMm) =>
        $"{MmToInches(widthMm):0.#} × {MmToInches(lengthMm):0.#} in";
}
