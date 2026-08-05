namespace SpreadingJoy.Domain.EntityModels;

// Hand-written halves of the entities, kept separate so the rest can be
// regenerated without dropping these.
//
// None of the three can be deleted once used: a Product is referenced by every
// design built on it, a Design by every order line, and a Customer by their
// order history. Archiving hides them from day-to-day lists while leaving that
// history intact and resolvable.

public partial class Product
{
    public bool IsActive { get; set; } = true;
}

public partial class Design
{
    public bool IsActive { get; set; } = true;

    // The studio's own artwork, offered from the shop for anyone to order,
    // rather than something a customer brought.
    //
    // This is the lower-risk half of the business: the studio made it, so
    // there's no question about who owns it. Note it does *not* skip the
    // approval gate — artwork uploaded by staff is created already approved,
    // so a studio design passes the same check as everything else. A design
    // that somehow reached the press without an approved image would be a bug
    // whichever kind it was, and there's no branch here that could hide one.
    public bool IsStudioDesign { get; set; }
}

public partial class Customer
{
    public bool IsActive { get; set; } = true;
}
