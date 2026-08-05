using System.ComponentModel.DataAnnotations;
using SpreadingJoy.ViewModels.Validation;

namespace SpreadingJoy.ViewModels;

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

    public decimal Total => Lines.Sum(l => l.LineTotal);
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

    [Range(1, int.MaxValue, ErrorMessage = "Pick a design.")]
    public int DesignId { get; set; }

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
    public string? FrontImageUrl { get; set; }
    public string? BackImageUrl { get; set; }
    public string SizeCode { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public DateTime RequestedFor { get; set; }
    public string? Notes { get; set; }
    public bool RightsAttested { get; set; }
    public DateTime CreatedAt { get; set; }

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
