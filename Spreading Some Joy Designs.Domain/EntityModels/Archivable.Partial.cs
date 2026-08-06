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

    // How a design is addressed in a URL, instead of its primary key.
    //
    // The order page is anonymous by necessity — a customer has no account —
    // so with a sequential id in the URL anyone could count upwards and page
    // through every design ever made, artwork included. For a site whose whole
    // premise is people uploading pictures, that's a real disclosure and not a
    // theoretical one.
    //
    // A GUID isn't a permission check; it's an unguessable name. Anyone holding
    // the link can see the design, which is what makes a shareable link work.
    // What it stops is enumeration.
    public Guid PublicToken { get; set; } = Guid.NewGuid();

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
