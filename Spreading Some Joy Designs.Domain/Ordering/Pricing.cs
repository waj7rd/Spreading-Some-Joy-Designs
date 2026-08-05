using SpreadingJoy.Domain.EntityModels;

namespace SpreadingJoy.Domain.Ordering;

// What one garment costs.
//
// A pure function of the product, the design and the size — no repositories, no
// clock, no database. That's deliberate: pricing is the thing a customer will
// argue about, and a rule you can read in one screen and test in one line is
// worth more here than anywhere else in the codebase.
//
// The result is snapshotted onto the order line at the moment of ordering.
// Nothing re-runs this against a past order.
public static class Pricing
{
    public static decimal UnitPrice(Product product, Design design, string sizeCode)
    {
        var price = product.BasePrice;

        // Charged per printed side. A front-and-back design is two passes
        // through the press, so it costs two.
        price += product.PrintSidePrice * PrintedSides(design);

        if (Sizes.IsExtended(sizeCode))
            price += product.ExtendedSizeUpcharge;

        return decimal.Round(price, 2, MidpointRounding.AwayFromZero);
    }

    public static int PrintedSides(Design design)
    {
        var sides = 0;

        if (design.FrontArtworkId != null)
            sides++;

        if (design.BackArtworkId != null)
            sides++;

        return sides;
    }
}
