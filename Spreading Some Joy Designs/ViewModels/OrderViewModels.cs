using System.ComponentModel.DataAnnotations;
using SpreadingJoy.ViewModels.Validation;

// The view models below carry a FulfilmentMethod property, which would otherwise
// shadow the type of the same name. Aliased rather than renamed: the property
// lines up with the column and the entity, and one alias here is cheaper than a
// third name for the same idea.
using FulfilmentMethods = SpreadingJoy.Domain.EntityModels.FulfilmentMethod;

namespace SpreadingJoy.ViewModels;

// An address as it would go on a label: the parts that exist, in order, with
// city, state and ZIP gathered onto one line.
//
// Shared by the queue and the order screen so a shipped job reads the same way
// wherever staff meet it.
public static class ShippingAddressLines
{
    public static IReadOnlyList<string> For(
        string? line1, string? line2, string? city, string? state, string? postalCode)
    {
        var lines = new List<string>();

        if (!string.IsNullOrWhiteSpace(line1))
            lines.Add(line1.Trim());

        if (!string.IsNullOrWhiteSpace(line2))
            lines.Add(line2.Trim());

        var locality = string.Join(" ", new[] { city?.Trim(), state?.Trim(), postalCode?.Trim() }
            .Where(part => !string.IsNullOrWhiteSpace(part)));

        if (locality.Length > 0)
            lines.Add(locality);

        return lines;
    }
}

public class OrderRowViewModel
{
    public int Id { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime DueOn { get; set; }
    public int GarmentCount { get; set; }
    public decimal Total { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? Notes { get; set; }

    // Whether this one goes in a box. On the board because it changes what the
    // day's work actually is — a shipped order needs packing and a trip to the
    // post office after the press is finished with it.
    public bool IsShipping { get; set; }

    // Drives the "late" styling on the board. Compared against the studio's
    // today, supplied by the controller — not DateTime.Today, which on a UTC
    // server is the wrong day for several hours each evening.
    public bool IsOverdue { get; set; }
}

public class OrderBoardViewModel
{
    public string? SuccessMessage { get; set; }
    public string? ErrorMessage { get; set; }
    public IList<OrderRowViewModel> Orders { get; set; } = [];
    public IList<string> Statuses { get; set; } = [];
}

public class OrderLineViewModel
{
    public int DesignId { get; set; }
    public string DesignName { get; set; } = string.Empty;
    public string GarmentName { get; set; } = string.Empty;
    public string SizeCode { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal => UnitPrice * Quantity;
}

public class OrderDetailsViewModel
{
    public int Id { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string? CustomerEmail { get; set; }
    public string? CustomerPhone { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime DueOn { get; set; }
    public string? Notes { get; set; }
    public bool RightsAttested { get; set; }
    public DateTime? RightsAttestedAt { get; set; }
    public string? CancellationReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public IList<OrderLineViewModel> Lines { get; set; } = [];
    public IList<string> Statuses { get; set; } = [];

    // How this order reaches the customer, read from the order itself rather
    // than inferred from whether an address happens to be filled in.
    public string FulfilmentMethod { get; set; } = FulfilmentMethods.Pickup;
    public bool IsShipping => FulfilmentMethods.IsShipping(FulfilmentMethod);

    public string? ShipToLine1 { get; set; }
    public string? ShipToLine2 { get; set; }
    public string? ShipToCity { get; set; }
    public string? ShipToState { get; set; }
    public string? ShipToPostalCode { get; set; }

    public IReadOnlyList<string> ShipToLines => ShippingAddressLines.For(
        ShipToLine1, ShipToLine2, ShipToCity, ShipToState, ShipToPostalCode);

    // The postage charged on this order, snapshotted when it was placed. Not the
    // studio's current fee — this is what the customer agreed to, and reading
    // today's number here would quietly restate a past order.
    public decimal ShippingFee { get; set; }

    // The garments alone, then what the customer actually owes. Split because a
    // shipped order has to show both: one figure leaves staff unable to answer
    // "what did the postage come to?" without doing the arithmetic themselves.
    public decimal Subtotal => Lines.Sum(l => l.LineTotal);
    public decimal Total => Subtotal + ShippingFee;

    public int GarmentCount => Lines.Sum(l => l.Quantity);

    public string? SuccessMessage { get; set; }
    public string? ErrorMessage { get; set; }
}

// The public order form: an anonymous visitor asking for a design to be printed.
public class PlaceOrderViewModel
{
    [Required(ErrorMessage = "Tell us your name.")]
    [StringLength(100)]
    [Display(Name = "Your name")]
    public string CustomerName { get; set; } = string.Empty;

    [EmailAddress(ErrorMessage = "That doesn't look like an email address.")]
    [StringLength(255)]
    [Display(Name = "Email")]
    public string? Email { get; set; }

    [Required(ErrorMessage = "Leave a phone number so we can reach you.")]
    [RegularExpression(ValidationPatterns.Phone, ErrorMessage = ValidationPatterns.PhoneMessage)]
    [StringLength(30)]
    [Display(Name = "Phone")]
    public string Phone { get; set; } = string.Empty;

    // The design is addressed by its unguessable token, never by its primary
    // key. That's true of the POST as well as the GET — otherwise the form
    // would be the way back in to pointing at somebody else's design.
    public Guid DesignToken { get; set; }

    [Required(ErrorMessage = "Pick a size.")]
    [Display(Name = "Size")]
    public string SizeCode { get; set; } = string.Empty;

    [Range(1, 500, ErrorMessage = "Quantity has to be between 1 and 500.")]
    [Display(Name = "How many")]
    public int Quantity { get; set; } = 1;

    [DataType(DataType.Date)]
    [Display(Name = "Needed by")]
    public DateTime RequestedFor { get; set; }

    [StringLength(500)]
    [Display(Name = "Anything else we should know?")]
    public string? Notes { get; set; }

    // Collection or postage. Defaulted to collection so a form posted without
    // it — a cached page, a hand-built request — asks for the thing that needs
    // no address rather than the thing that does.
    [Display(Name = "How would you like it?")]
    public string FulfilmentMethod { get; set; } = FulfilmentMethods.Pickup;

    public bool IsShipping => FulfilmentMethods.IsShipping(FulfilmentMethod);

    // Only asked for, and only required, when shipping is chosen. The
    // conditional part lives in the controller rather than in attributes:
    // "required, but only sometimes" is not something DataAnnotations says
    // well, and Fulfilment.Check in the Domain is the rule either way.
    [StringLength(200)]
    [Display(Name = "Street address")]
    public string? ShipToLine1 { get; set; }

    [StringLength(200)]
    [Display(Name = "Apartment, suite, etc.")]
    public string? ShipToLine2 { get; set; }

    [StringLength(100)]
    [Display(Name = "City")]
    public string? ShipToCity { get; set; }

    [StringLength(50)]
    [Display(Name = "State")]
    public string? ShipToState { get; set; }

    [StringLength(20)]
    [Display(Name = "ZIP")]
    public string? ShipToPostalCode { get; set; }

    // Not a formality. The whole storefront is built around people bringing
    // images they found, and this is the record that they said they had the
    // right to use this one. Validated server-side too — a checkbox is trivial
    // to strip out of a POST.
    [Display(Name = "I have the right to use this artwork")]
    public bool RightsAttested { get; set; }

    // Rendered as a summary beside the form. Display only — repopulated by the
    // controller on every render, never trusted from the post.
    public string? DesignName { get; set; }
    public string? GarmentName { get; set; }
    public IList<string> AvailableSizes { get; set; } = [];

    // Whether to offer the choice at all, and what postage would add. Both are
    // read from the studio record on every render, for the same reason the price
    // is: a figure the customer is quoted must not be one they can post back.
    public bool OffersShipping { get; set; }
    public decimal ShippingFee { get; set; }

    // The garment as it will actually be printed. Someone about to commit to
    // twelve shirts should be looking at the shirt, not at a floating rectangle
    // of artwork.
    public ShirtPreviewViewModel Front { get; set; } = new() { Side = "front" };
    public ShirtPreviewViewModel Back { get; set; } = new() { Side = "back" };

    // What one shirt costs at the currently selected size, so the total isn't a
    // surprise at the end.
    public decimal UnitPrice { get; set; }
    public decimal ExtendedSizeUpcharge { get; set; }
    public int PrintedSides { get; set; }

    public string? ErrorMessage { get; set; }
}

public class OrderRequestRowViewModel
{
    public int Id { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string Phone { get; set; } = string.Empty;
    public string DesignName { get; set; } = string.Empty;
    public string GarmentName { get; set; } = string.Empty;

    // The garment as it would be printed. Whoever is accepting this needs to see
    // the artwork *on the shirt* — a placement that's obviously wrong is plain
    // at a glance here and invisible in a floating thumbnail.
    public ShirtPreviewViewModel Front { get; set; } = new() { Side = "front" };
    public ShirtPreviewViewModel Back { get; set; } = new() { Side = "back" };

    public string SizeCode { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public DateTime RequestedFor { get; set; }
    public string? Notes { get; set; }
    public bool RightsAttested { get; set; }
    public DateTime CreatedAt { get; set; }

    // How they asked to get it. On the queue because accepting a shipped job is
    // agreeing to different work: it needs packing, postage, and an address that
    // has to be right before the order exists rather than after.
    public string FulfilmentMethod { get; set; } = FulfilmentMethods.Pickup;
    public bool IsShipping => FulfilmentMethods.IsShipping(FulfilmentMethod);

    public string? ShipToLine1 { get; set; }
    public string? ShipToLine2 { get; set; }
    public string? ShipToCity { get; set; }
    public string? ShipToState { get; set; }
    public string? ShipToPostalCode { get; set; }

    public IReadOnlyList<string> ShipToLines => ShippingAddressLines.For(
        ShipToLine1, ShipToLine2, ShipToCity, ShipToState, ShipToPostalCode);

    // What postage would be charged if this were accepted now. The studio's
    // current fee, not a snapshot — nothing is snapshotted until the order
    // exists, and presenting it as settled on a pending request would be a lie.
    public decimal ShippingFeeIfAccepted { get; set; }

    // The soonest the studio could actually promise it, prefilled on the accept
    // form so staff aren't offered a date the rules will reject.
    public DateTime SuggestedDueOn { get; set; }
}

public class OrderRequestQueueViewModel
{
    public string? SuccessMessage { get; set; }
    public string? ErrorMessage { get; set; }
    public IList<OrderRequestRowViewModel> Requests { get; set; } = [];
}
