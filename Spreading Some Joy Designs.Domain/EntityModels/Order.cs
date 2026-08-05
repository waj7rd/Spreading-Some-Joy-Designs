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

    // ---- Computed in C#, not a column. ----

    // Read off the snapshotted line prices, so an order total never restates
    // itself when the catalogue changes.
    public decimal Total => OrderLines.Sum(l => l.LineTotal);

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
