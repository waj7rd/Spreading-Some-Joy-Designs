using System.ComponentModel.DataAnnotations;
using SpreadingJoy.Domain.Production;

namespace SpreadingJoy.ViewModels;

// The sheet list.
public class GangSheetListViewModel
{
    public IReadOnlyList<GangSheetRowViewModel> Sheets { get; set; } = [];

    // The "start a sheet" form lives on this page rather than a screen of its
    // own — a new sheet is six fields, and five of them have a sensible default.
    public EditGangSheetViewModel NewSheet { get; set; } = EditGangSheetViewModel.WithDefaults();

    public string? SuccessMessage { get; set; }
    public string? ErrorMessage { get; set; }
}

public class GangSheetRowViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;

    public int WidthMm { get; set; }
    public int MaxLengthMm { get; set; }
    public int UsedLengthMm { get; set; }

    public int ItemCount { get; set; }
    public int PlacedCount { get; set; }
    public int UnplacedCount { get; set; }
    public double CoveragePercent { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? PrintedAt { get; set; }
    public string? CreatedBy { get; set; }

    // Who it's for. A studio sheet is this week's orders packed onto one piece
    // of film; a customer sheet is a thing somebody bought, and the price is
    // what they were quoted rather than what the catalogue says today.
    public string Origin { get; set; } = GangSheetOriginNames.Studio;
    public string? CustomerName { get; set; }
    public decimal Price { get; set; }

    public bool IsCustomerSheet => Origin == GangSheetOriginNames.Customer;

    // "22 × 60 in" — how the film is described everywhere except in this
    // database. Shown so an order to the supplier can be placed without anyone
    // doing arithmetic.
    public string FilmSize => FilmSizes.Describe(WidthMm, MaxLengthMm);

    public string UsedSize => UsedLengthMm > 0
        ? $"{FilmSizes.MmToInches(UsedLengthMm):0.#} in of film"
        : "nothing packed yet";

    public string StatusCss => Status switch
    {
        GangSheetStatusNames.Ready => "text-bg-primary",
        GangSheetStatusNames.Printed => "text-bg-secondary",
        _ => "text-bg-warning"
    };
}

// The build screen: the sheet, what's on it, and what's waiting to go on it.
public class GangSheetBuildViewModel
{
    public GangSheetRowViewModel Sheet { get; set; } = new();

    public EditGangSheetViewModel Settings { get; set; } = new();

    public bool IsEditable { get; set; }

    public bool AllowRotation { get; set; }

    public IReadOnlyList<GangSheetItemViewModel> Items { get; set; } = [];

    public IReadOnlyList<TransferCandidateViewModel> Candidates { get; set; } = [];

    public string? Notes { get; set; }

    public string? SuccessMessage { get; set; }
    public string? ErrorMessage { get; set; }

    public IReadOnlyList<GangSheetItemViewModel> Placed =>
        Items.Where(i => i.IsPlaced).ToList();

    public IReadOnlyList<GangSheetItemViewModel> Unplaced =>
        Items.Where(i => !i.IsPlaced).ToList();
}

// One transfer, already on a sheet.
//
// The percentages are what the preview is drawn with. Computed here rather than
// in the view so the arithmetic that decides where a transfer appears on screen
// sits next to the millimetres it came from.
public class GangSheetItemViewModel
{
    public int Id { get; set; }
    public string Label { get; set; } = string.Empty;
    public string Side { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;

    public int WidthMm { get; set; }
    public int HeightMm { get; set; }
    public int XMm { get; set; }
    public int YMm { get; set; }
    public bool Rotated { get; set; }
    public bool IsPlaced { get; set; }

    // Effective resolution at the size this is being printed. Print quality is
    // a property of the image and the size together, never the file alone.
    public int Dpi { get; set; }

    public bool IsLowResolution => Dpi > 0 && Dpi < ImageLimits.MinimumDpi;

    // The film this sheet is drawn against, so each transfer can express its
    // position as a percentage of it.
    public int SheetWidthMm { get; set; }
    public int SheetLengthMm { get; set; }

    public double LeftPercent => Percent(XMm, SheetWidthMm);
    public double TopPercent => Percent(YMm, SheetLengthMm);
    public double WidthPercent => Percent(Rotated ? HeightMm : WidthMm, SheetWidthMm);
    public double HeightPercent => Percent(Rotated ? WidthMm : HeightMm, SheetLengthMm);

    private static double Percent(int value, int total) =>
        total <= 0 ? 0 : Math.Round(value / (double)total * 100, 3);
}

// One printable side of one open order line, offered for adding to a sheet.
public class TransferCandidateViewModel
{
    public int OrderLineId { get; set; }
    public int OrderId { get; set; }
    public int DesignId { get; set; }
    public int ArtworkId { get; set; }

    public string Side { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string DesignName { get; set; } = string.Empty;
    public string? CustomerName { get; set; }
    public string SizeCode { get; set; } = string.Empty;
    public DateTime DueOn { get; set; }

    public int Quantity { get; set; }
    public int WidthMm { get; set; }
    public int HeightMm { get; set; }
    public int AlreadyPlaced { get; set; }

    public string ImageUrl { get; set; } = string.Empty;
    public string ArtworkStatus { get; set; } = string.Empty;
    public int Dpi { get; set; }

    // Nothing reaches film without a person having looked at it. Unapproved
    // artwork is shown, so it's clear why the run is waiting — the checkbox is
    // simply disabled, and the Domain refuses it too if one is posted anyway.
    public bool IsApproved => ArtworkStatus == "Approved";

    public bool IsLowResolution => Dpi > 0 && Dpi < ImageLimits.MinimumDpi;

    public string SizeLabel => $"{WidthMm} × {HeightMm} mm";
}

// One row of the "add to sheet" form.
//
// Deliberately three fields, none of them a size or an artwork id. The
// controller looks the transfer up again by order line and side, so a posted
// form can't put a different image, or a different size, on the film than the
// screen was offering. The form is a suggestion; the server decides what it
// meant — the same shape as the ordering path taking a design token rather than
// a price.
public class SelectedTransferViewModel
{
    public bool Selected { get; set; }

    public int OrderLineId { get; set; }

    public string Side { get; set; } = string.Empty;

    public int Quantity { get; set; } = 1;
}

// The editable fields of a sheet.
//
// The bounds are repeated from GangSheetLogic, which is where the rule lives —
// these exist only so a refusal arrives in the field rather than as a banner at
// the top of the page.
public class EditGangSheetViewModel
{
    public int GangSheetId { get; set; }

    [Required(ErrorMessage = "Give the sheet a name.")]
    [StringLength(100)]
    [Display(Name = "Sheet name")]
    public string Name { get; set; } = string.Empty;

    [Range(FilmSizes.MinWidthMm, FilmSizes.MaxWidthMm, ErrorMessage = "Film width has to be between 100mm and 1000mm.")]
    [Display(Name = "Film width")]
    public int WidthMm { get; set; } = FilmSizes.InchesToMm(FilmSizes.DefaultWidthInches);

    [Range(FilmSizes.MinLengthMm, FilmSizes.MaxLengthMm, ErrorMessage = "Sheet length has to be between 100mm and 6000mm.")]
    [Display(Name = "Sheet length")]
    public int MaxLengthMm { get; set; } = FilmSizes.InchesToMm(FilmSizes.DefaultLengthInches);

    [Range(0, FilmSizes.MaxGutterMm, ErrorMessage = "Gutter has to be between 0mm and 50mm.")]
    [Display(Name = "Gutter")]
    public int GutterMm { get; set; } = FilmSizes.DefaultGutterMm;

    [Range(0, FilmSizes.MaxMarginMm, ErrorMessage = "Margin has to be between 0mm and 50mm.")]
    [Display(Name = "Edge margin")]
    public int MarginMm { get; set; } = FilmSizes.DefaultMarginMm;

    [Display(Name = "Let the packer rotate transfers")]
    public bool AllowRotation { get; set; } = true;

    [StringLength(500)]
    [Display(Name = "Notes")]
    public string? Notes { get; set; }

    public string? ErrorMessage { get; set; }

    public static EditGangSheetViewModel WithDefaults() => new();
}

// The status strings, reachable from a view model without dragging the entity
// namespace into it.
public static class GangSheetStatusNames
{
    public const string Draft = "Draft";
    public const string Ready = "Ready";
    public const string Printed = "Printed";
}

// ---------------------------------------------------------------------------
// Sheets people asked us to print.
// ---------------------------------------------------------------------------

public class GangSheetRequestListViewModel
{
    public string Status { get; set; } = GangSheetRequestStatusNames.Pending;

    // Shown on the tab, so the queue announces itself rather than waiting to be
    // checked.
    public int PendingCount { get; set; }

    public IReadOnlyList<GangSheetRequestRowViewModel> Requests { get; set; } = [];

    public string? SuccessMessage { get; set; }
    public string? ErrorMessage { get; set; }
}

public class GangSheetRequestRowViewModel
{
    public int Id { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string Phone { get; set; } = string.Empty;

    public string SizeName { get; set; } = string.Empty;
    public decimal PriceQuoted { get; set; }
    public int TransferCount { get; set; }
    public string? Notes { get; set; }

    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? HandledAt { get; set; }
    public string? HandledBy { get; set; }
    public string? DeclineReason { get; set; }
    public int? GangSheetId { get; set; }

    public IReadOnlyList<GangSheetRequestItemViewModel> Items { get; set; } = [];

    // Accepting is refused while anything on it is still waiting for review, so
    // the button is not offered either. The rule is in the logic layer; this
    // only stops staff clicking at a refusal they can already see.
    public bool ReadyToAccept => Items.Count > 0 && Items.All(i => i.IsApproved);

    public int AwaitingReviewCount => Items.Count(i => !i.IsApproved);
}

public class GangSheetRequestItemViewModel
{
    public string Label { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public int WidthMm { get; set; }
    public int HeightMm { get; set; }
    public int Quantity { get; set; }
    public string ArtworkStatus { get; set; } = string.Empty;
    public int Dpi { get; set; }

    public bool IsApproved => ArtworkStatus == "Approved";

    public bool IsLowResolution => Dpi > 0 && Dpi < ImageLimits.MinimumDpi;

    public string SizeLabel => $"{WidthMm} × {HeightMm} mm";
}

// ---------------------------------------------------------------------------
// What the studio sells.
// ---------------------------------------------------------------------------

public class GangSheetSizeListViewModel
{
    public IReadOnlyList<EditGangSheetSizeViewModel> Sizes { get; set; } = [];

    public EditGangSheetSizeViewModel NewSize { get; set; } = new();

    public string? SuccessMessage { get; set; }
    public string? ErrorMessage { get; set; }
}

public class EditGangSheetSizeViewModel
{
    public int GangSheetSizeId { get; set; }

    [Required(ErrorMessage = "Give the sheet a name.")]
    [StringLength(60)]
    [Display(Name = "Name")]
    public string Name { get; set; } = string.Empty;

    [Range(FilmSizes.MinWidthMm, FilmSizes.MaxWidthMm, ErrorMessage = "Film width has to be between 100mm and 1000mm.")]
    [Display(Name = "Width")]
    public int WidthMm { get; set; } = FilmSizes.InchesToMm(FilmSizes.DefaultWidthInches);

    [Range(FilmSizes.MinLengthMm, FilmSizes.MaxLengthMm, ErrorMessage = "Sheet length has to be between 100mm and 6000mm.")]
    [Display(Name = "Length")]
    public int LengthMm { get; set; } = FilmSizes.InchesToMm(24);

    // Zero is allowed. A studio running an offer, or throwing one in with a bulk
    // order, shouldn't have to invent a price to do it.
    [Range(0, 1000, ErrorMessage = "Price has to be between 0 and 1000 dollars.")]
    [Display(Name = "Price")]
    public decimal Price { get; set; }

    public bool IsActive { get; set; } = true;

    public string Dimensions => $"{WidthMm} × {LengthMm} mm";
}

// The request statuses, reachable from a view model without dragging the entity
// namespace into it.
public static class GangSheetRequestStatusNames
{
    public const string Pending = "Pending";
    public const string Accepted = "Accepted";
    public const string Declined = "Declined";
}

// Who a sheet was built for, reachable from a view model without dragging the
// entity namespace into it.
public static class GangSheetOriginNames
{
    public const string Studio = "Studio";
    public const string Customer = "Customer";
}
