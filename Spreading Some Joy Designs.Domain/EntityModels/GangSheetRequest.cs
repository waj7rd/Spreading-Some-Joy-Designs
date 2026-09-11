namespace SpreadingJoy.Domain.EntityModels;

// A gang sheet a stranger built and asked us to print.
//
// A holding table, exactly like OrderRequests and for the same reason: nothing
// an anonymous visitor types becomes a Customer or a real GangSheet until a
// member of staff accepts it. The rule is in the architecture notes and it is
// the reason this isn't simply a GangSheet row with a flag on it.
//
// The items travel with the request rather than on a sheet, because there is no
// sheet yet. Positions aren't stored here either — where each transfer lands is
// decided by the packer when the request becomes a real sheet, and a layout
// computed for a preview is not a layout anybody printed from.
public partial class GangSheetRequest
{
    public int GangSheetRequestId { get; set; }

    public string CustomerName { get; set; } = null!;

    public string? Email { get; set; }

    public string Phone { get; set; } = null!;

    // Which sheet off the catalogue they chose.
    public int GangSheetSizeId { get; set; }

    // What that sheet cost at the moment they asked for it. Snapshotted, so a
    // price rise between submitting and being accepted can't restate what they
    // agreed to.
    public decimal PriceQuoted { get; set; }

    // 'Pickup' or 'Shipping'. The same two words orders use, checked by the same
    // Fulfilment.Check — a sheet of film is the easiest thing this studio could
    // post, so there was never a reason for it to have rules of its own.
    public string FulfilmentMethod { get; set; } = EntityModels.FulfilmentMethod.Pickup;

    // Where it goes, if it goes anywhere. A collection request stores no address
    // at all, not a partial one: Fulfilment.ToStore drops whatever was posted.
    public string? ShipToLine1 { get; set; }

    public string? ShipToLine2 { get; set; }

    public string? ShipToCity { get; set; }

    public string? ShipToState { get; set; }

    public string? ShipToPostalCode { get; set; }

    // Postage at the moment they asked, snapshotted like PriceQuoted beside it.
    // Zero on a collection request, and on every request made before the studio
    // offered postage on sheets.
    public decimal ShippingFee { get; set; }

    public string? Notes { get; set; }

    // The customer's assertion that the artwork is theirs to use. A gate, not a
    // checkbox that gets recorded — GangSheetRequestLogic refuses without it,
    // the same as OrderLogic.PlaceAsync does.
    public bool RightsAttested { get; set; }

    public string Status { get; set; } = GangSheetRequestStatus.Pending;

    public int? HandledByUserId { get; set; }

    public DateTime? HandledAt { get; set; }

    public string? DeclineReason { get; set; }

    // Set on acceptance, so the request keeps pointing at what it became.
    public int? GangSheetId { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual GangSheetSize GangSheetSize { get; set; } = null!;

    public virtual User? HandledByUser { get; set; }

    public virtual GangSheet? GangSheet { get; set; }

    public virtual ICollection<GangSheetRequestItem> Items { get; set; } = new List<GangSheetRequestItem>();

    // ---- Computed in C#, not columns. ----

    public int TransferCount => Items.Sum(i => i.Quantity);

    public bool IsShipping => EntityModels.FulfilmentMethod.IsShipping(FulfilmentMethod);

    // What they were quoted altogether. Postage is a charge on the sheet rather
    // than an item on it, for the same reason it's a charge on an order rather
    // than a line — an envelope is not a transfer and must not be packed like
    // one.
    public decimal TotalQuoted => PriceQuoted + ShippingFee;
}

// One image on a requested sheet, at a size, some number of times.
//
// Quantity is a column here, unlike GangSheetItem — a request is a statement of
// what somebody wants, not a layout. It expands into one row per copy when the
// request is accepted and the transfers actually have to go somewhere.
public partial class GangSheetRequestItem
{
    public int GangSheetRequestItemId { get; set; }

    public int GangSheetRequestId { get; set; }

    public int ArtworkId { get; set; }

    // What the customer called it, or the filename they uploaded. Ends up on
    // the cut list, so it is stored rather than derived.
    public string Label { get; set; } = null!;

    public int WidthMm { get; set; }

    public int HeightMm { get; set; }

    public int Quantity { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual GangSheetRequest GangSheetRequest { get; set; } = null!;

    public virtual Artwork Artwork { get; set; } = null!;
}

// Where a submitted sheet is. Same three states as OrderRequestStatus, on
// purpose: it is the same kind of thing waiting for the same decision.
public static class GangSheetRequestStatus
{
    public const string Pending = "Pending";
    public const string Accepted = "Accepted";
    public const string Declined = "Declined";

    public static readonly string[] All = [Pending, Accepted, Declined];
}
