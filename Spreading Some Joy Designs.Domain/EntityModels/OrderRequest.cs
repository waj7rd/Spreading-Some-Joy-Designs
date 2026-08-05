namespace SpreadingJoy.Domain.EntityModels;

// What an anonymous visitor submits from the storefront.
//
// Deliberately not a Customer and not an Order. Anything typed by someone who
// hasn't been identified stays in this table until a member of staff has looked
// at it and accepted it — at which point the customer record and the order are
// created from it, inside one transaction. That keeps unverified strangers out
// of the customer list, and keeps a declined request from leaving a half-made
// customer behind.
public partial class OrderRequest
{
    public int OrderRequestId { get; set; }

    public string CustomerName { get; set; } = null!;

    public string? Email { get; set; }

    public string Phone { get; set; } = null!;

    public int DesignId { get; set; }

    public string SizeCode { get; set; } = null!;

    public int Quantity { get; set; }

    // What the visitor asked for. The studio's own rules are applied when the
    // request is accepted, not when it's submitted — a date the shop can't hit
    // is a conversation, not a validation error on a public form.
    public DateTime RequestedFor { get; set; }

    public string? Notes { get; set; }

    public bool RightsAttested { get; set; }

    public string Status { get; set; } = OrderRequestStatus.Pending;

    public int? HandledByUserId { get; set; }

    public DateTime? HandledAt { get; set; }

    public string? DeclineReason { get; set; }

    // Set when the request is accepted, so the request row keeps pointing at
    // what it became.
    public int? OrderId { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Design Design { get; set; } = null!;

    public virtual User? HandledByUser { get; set; }

    public virtual Order? Order { get; set; }
}

public static class OrderRequestStatus
{
    public const string Pending = "Pending";
    public const string Accepted = "Accepted";
    public const string Declined = "Declined";

    public static readonly string[] All = [Pending, Accepted, Declined];
}
