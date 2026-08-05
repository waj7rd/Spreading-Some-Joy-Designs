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
}

public partial class Customer
{
    public bool IsActive { get; set; } = true;
}
