using System.ComponentModel.DataAnnotations;
using SpreadingJoy.ViewModels.Validation;

namespace SpreadingJoy.ViewModels;

// How the studio runs.
//
// Note what isn't here: the tier. A studio changing its own tier is a studio
// giving itself features it hasn't paid for. Leaving the property off the view
// model means the model binder has nothing to bind even if somebody posts one —
// which is stronger than hiding the field, and matches IStudioLogic having no
// operation for it either.
public class StudioSettingsViewModel
{
    [Required(ErrorMessage = "Give the studio a name.")]
    [StringLength(100)]
    [Display(Name = "Studio name")]
    public string Name { get; set; } = string.Empty;

    [RegularExpression(ValidationPatterns.Phone, ErrorMessage = ValidationPatterns.PhoneMessage)]
    [StringLength(30)]
    [Display(Name = "Phone")]
    public string? Phone { get; set; }

    [EmailAddress(ErrorMessage = "That doesn't look like an email address.")]
    [StringLength(255)]
    [Display(Name = "Email")]
    public string? Email { get; set; }

    [StringLength(200)]
    [Display(Name = "Address")]
    public string? AddressLine { get; set; }

    [StringLength(100)]
    [Display(Name = "City")]
    public string? City { get; set; }

    [StringLength(50)]
    [Display(Name = "State")]
    public string? State { get; set; }

    [StringLength(20)]
    [Display(Name = "ZIP")]
    public string? PostalCode { get; set; }

    [Required]
    [StringLength(100)]
    [Display(Name = "Timezone")]
    public string TimeZoneId { get; set; } = "America/Chicago";

    [Range(1, 5000, ErrorMessage = "Daily capacity has to be between 1 and 5000 garments.")]
    [Display(Name = "Garments per day")]
    public int DailyPrintCapacity { get; set; }

    [Range(1, 90, ErrorMessage = "Turnaround has to be between 1 and 90 working days.")]
    [Display(Name = "Turnaround (working days)")]
    public int TurnaroundDays { get; set; }

    [Display(Name = "Closed on")]
    public List<DayOfWeek> ClosedDays { get; set; } = [];

    // Whether the storefront offers postage at all. Off means the ship-or-collect
    // choice is never rendered, and the Domain refuses it even if one is posted.
    [Display(Name = "Offer shipping")]
    public bool OffersShipping { get; set; }

    // Flat, per order. The bound is repeated in StudioLogic, which is the rule --
    // this one only exists so the refusal arrives in the field rather than as a
    // banner at the top of the page.
    [Range(0, 500, ErrorMessage = "The shipping fee has to be between 0 and 500 dollars.")]
    [Display(Name = "Shipping fee")]
    public decimal ShippingFee { get; set; }

    // Read-only on this screen, shown so staff can see what the studio is on
    // without being able to change it.
    public string CurrentTier { get; set; } = string.Empty;

    public string? SuccessMessage { get; set; }
    public string? ErrorMessage { get; set; }
}
