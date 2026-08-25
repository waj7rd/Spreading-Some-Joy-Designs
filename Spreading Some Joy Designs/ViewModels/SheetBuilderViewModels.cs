using System.ComponentModel.DataAnnotations;
using SpreadingJoy.Domain.Production;
using SpreadingJoy.ViewModels.Validation;

namespace SpreadingJoy.ViewModels;

// The public gang sheet builder.
//
// Everything a visitor arranges here lives in the session until they submit,
// the same as the designer — somebody who wanders off shouldn't leave a
// half-made sheet in the database. The artwork is real by then, because we
// fetched and stored it, but nothing ties it to a person.
public class SheetBuilderViewModel
{
    public IReadOnlyList<SheetSizeOptionViewModel> Sizes { get; set; } = [];

    public int GangSheetSizeId { get; set; }

    public IReadOnlyList<BuilderItemViewModel> Items { get; set; } = [];

    // What the packer made of it. Null before a size is chosen.
    public SheetPreviewViewModel? Preview { get; set; }

    public SubmitSheetViewModel Submit { get; set; } = new();

    public string? SuccessMessage { get; set; }
    public string? ErrorMessage { get; set; }
    public string? PendingNotice { get; set; }

    public bool HasItems => Items.Count > 0;

    // Submitting is allowed only when everything fits. The rule is in
    // GangSheetRequestLogic; this just stops the button being offered when the
    // answer is already known.
    public bool CanSubmit => HasItems && Preview is { Fits: true };
}

public class SheetSizeOptionViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int WidthMm { get; set; }
    public int LengthMm { get; set; }
    public decimal Price { get; set; }

    public string Dimensions => $"{WidthMm} × {LengthMm} mm";
}

// One image on the sheet, as the visitor set it up.
public class BuilderItemViewModel
{
    // Position in the session list. What the remove and resize forms post back
    // — the items aren't rows yet, so there is no id to use.
    public int Index { get; set; }

    public int ArtworkId { get; set; }
    public string Label { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;

    public int WidthMm { get; set; }
    public int HeightMm { get; set; }
    public int Quantity { get; set; }

    public string ArtworkStatus { get; set; } = string.Empty;
    public string? RejectionReason { get; set; }

    // Effective resolution at the size it is being printed. Warned about, never
    // refused: the studio looks at every image before it goes to press, and a
    // soft warning somebody can overrule beats a hard block they work around.
    public int Dpi { get; set; }

    public bool IsLowResolution => Dpi > 0 && Dpi < ImageLimits.MinimumDpi;

    // The widest this image can go and still hold 150 DPI. Stated so the
    // visitor is told what size would work rather than left guessing.
    public int MaxSharpWidthMm { get; set; }

    public bool IsRejected => ArtworkStatus == "Rejected";
}

// The packed layout, ready to draw. Percentages of the film, so the preview is
// the layout rather than a picture of one.
public class SheetPreviewViewModel
{
    public int WidthMm { get; set; }
    public int LengthMm { get; set; }
    public int UsedLengthMm { get; set; }
    public double CoveragePercent { get; set; }
    public decimal Price { get; set; }

    public IReadOnlyList<PreviewItemViewModel> Placed { get; set; } = [];

    public IReadOnlyList<string> TooBig { get; set; } = [];
    public IReadOnlyList<string> NoRoom { get; set; } = [];

    public bool Fits => TooBig.Count == 0 && NoRoom.Count == 0;

    public double UsedPercent =>
        LengthMm <= 0 ? 0 : Math.Round(UsedLengthMm / (double)LengthMm * 100, 3);
}

public class PreviewItemViewModel
{
    public string Label { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public bool Rotated { get; set; }

    public double LeftPercent { get; set; }
    public double TopPercent { get; set; }
    public double WidthPercent { get; set; }
    public double HeightPercent { get; set; }
}

// The details a visitor gives when they ask for the sheet. Same fields the
// order form asks for, and the same rights attestation, because it is the same
// promise about the same kind of picture.
public class SubmitSheetViewModel
{
    [Required(ErrorMessage = "Tell us your name.")]
    [StringLength(100)]
    [Display(Name = "Your name")]
    public string CustomerName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Leave a phone number so we can reach you.")]
    [RegularExpression(ValidationPatterns.Phone, ErrorMessage = ValidationPatterns.PhoneMessage)]
    [StringLength(30)]
    [Display(Name = "Phone")]
    public string Phone { get; set; } = string.Empty;

    [EmailAddress(ErrorMessage = "That doesn't look like an email address.")]
    [StringLength(255)]
    [Display(Name = "Email")]
    public string? Email { get; set; }

    [StringLength(500)]
    [Display(Name = "Anything else we should know?")]
    public string? Notes { get; set; }

    // Not [Required] — a bool is always present. The refusal lives in
    // GangSheetRequestLogic, which is where it can't be skipped.
    [Display(Name = "I have the right to use this artwork")]
    public bool RightsAttested { get; set; }
}

// What the visitor sees after asking for a sheet.
public class SheetSubmittedViewModel
{
    public string CustomerName { get; set; } = string.Empty;
    public string SizeName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int TransferCount { get; set; }
    public bool AnyAwaitingReview { get; set; }
}
