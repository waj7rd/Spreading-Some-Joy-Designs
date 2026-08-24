namespace SpreadingJoy.Domain.EntityModels;

// A placed order. Lines carry the garments; this carries who, when, and the
// paperwork that has to survive a dispute.
public partial class Order
{
    public int OrderId { get; set; }

    public int CustomerId { get; set; }

    public string Status { get; set; } = OrderStatus.Received;

    // The studio-local date the order is promised for. A date, not an instant —
    // "ready on the 12th" is what's agreed, and giving it a time would invent a
    // precision nobody committed to.
    public DateTime DueOn { get; set; }

    public string? Notes { get; set; }

    // How this order reaches the customer. Carried across from the request the
    // customer submitted, not re-decided by staff on their behalf.
    public string FulfilmentMethod { get; set; } = EntityModels.FulfilmentMethod.Pickup;

    // Only populated for a shipped order. See Fulfilment.ToStore for why a
    // collection order stores nothing here rather than whatever was typed.
    public string? ShipToLine1 { get; set; }

    public string? ShipToLine2 { get; set; }

    public string? ShipToCity { get; set; }

    public string? ShipToState { get; set; }

    public string? ShipToPostalCode { get; set; }

    // The postage charged on this order, snapshotted at the moment it was
    // placed — the same rule the line prices follow. The studio changing its
    // shipping fee next month must not restate what this customer agreed to.
    //
    // Zero for a collection order, which is what makes Total below safe to read
    // for every order ever placed, including the ones from before shipping
    // existed.
    public decimal ShippingFee { get; set; }

    // The customer's assertion that they hold the rights to the artwork they
    // supplied, captured at checkout. Stored with the order rather than the
    // account, because it's a per-order claim about per-order images — an
    // attestation made in March says nothing about a picture uploaded in June.
    public bool RightsAttested { get; set; }

    public DateTime? RightsAttestedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public string? CancellationReason { get; set; }

    public virtual Customer Customer { get; set; } = null!;

    public virtual ICollection<OrderLine> OrderLines { get; set; } = new List<OrderLine>();

    // ---- Computed in C#, not columns. ----

    // Whether this one goes in a box. Read from the stored method rather than
    // from "is there an address", so an order is never reclassified by someone
    // clearing a field.
    public bool IsShipped => EntityModels.FulfilmentMethod.IsShipping(FulfilmentMethod);

    // The garments alone, read off the snapshotted line prices so it never
    // restates itself when the catalogue changes.
    public decimal Subtotal => OrderLines.Sum(l => l.LineTotal);

    // What the customer actually owes. Postage is a charge on the order rather
    // than a line on it, because it isn't a garment: putting it in OrderLines
    // would make it a thing with a design, a size and a quantity, and would
    // land it in every count of how many shirts the press has to run.
    //
    // ShippingFee is zero on a collection order and on every order placed
    // before shipping existed, so this stays equal to Subtotal for all of them.
    public decimal Total => Subtotal + ShippingFee;

    public int GarmentCount => OrderLines.Sum(l => l.Quantity);
}

// Where an order is on the floor. String constants rather than an enum because
// these land in a NVARCHAR column and are read directly in SQL.
public static class OrderStatus
{
    public const string Received = "Received";
    public const string InProduction = "InProduction";
    public const string Printed = "Printed";
    public const string ReadyForPickup = "ReadyForPickup";
    public const string Completed = "Completed";
    public const string Cancelled = "Cancelled";

    public static readonly string[] All =
        [Received, InProduction, Printed, ReadyForPickup, Completed, Cancelled];

    // Statuses that still occupy press capacity. A cancelled or collected order
    // doesn't compete for a production day.
    public static readonly string[] Open =
        [Received, InProduction, Printed, ReadyForPickup];

    public static bool IsOpen(string status) => Open.Contains(status);
}
