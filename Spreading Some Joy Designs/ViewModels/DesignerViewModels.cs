using System.ComponentModel.DataAnnotations;

namespace SpreadingJoy.ViewModels;

// The designer screen. One garment, and up to two pieces of artwork placed on it.
public class DesignerViewModel
{
    public int? DesignId { get; set; }

    [Required(ErrorMessage = "Give the design a name.")]
    [StringLength(100)]
    [Display(Name = "Design name")]
    public string Name { get; set; } = string.Empty;

    [Range(1, int.MaxValue, ErrorMessage = "Pick a garment.")]
    [Display(Name = "Garment")]
    public int ProductId { get; set; }

    public SidePlacementViewModel Front { get; set; } = new();
    public SidePlacementViewModel Back { get; set; } = new();

    // Populated by the controller for the garment picker and the canvas.
    public IList<ProductRowViewModel> Products { get; set; } = [];

    public string? ErrorMessage { get; set; }
    public string? SuccessMessage { get; set; }

    // Set when an image was added but is still waiting on a moderator, so the
    // designer can say so plainly rather than letting someone reach checkout and
    // be refused there.
    public string? PendingNotice { get; set; }
}

// One side of the shirt. ArtworkId being null means nothing is printed on it.
public class SidePlacementViewModel
{
    public int? ArtworkId { get; set; }

    // Filled in by the controller so the canvas can render what's already there.
    public string? ImageUrl { get; set; }
    public int? ImageWidthPx { get; set; }
    public int? ImageHeightPx { get; set; }
    public string? Status { get; set; }
    public string? RejectionReason { get; set; }

    // Millimetres from the top-left of the print area. The browser works in
    // pixels and converts before posting — storing pixels would tie the record
    // to whatever window the customer happened to have open.
    public int XMm { get; set; }
    public int YMm { get; set; }
    public int WidthMm { get; set; }
    public int HeightMm { get; set; }

    public bool HasArtwork => ArtworkId.HasValue;
}

// The "add an image" panel, which is the whole point of the site.
public class AddArtworkViewModel
{
    [Display(Name = "Image address")]
    [StringLength(2048)]
    public string? Url { get; set; }

    // Which side the fetched image should land on: "front" or "back".
    public string Side { get; set; } = "front";

    public int? DesignId { get; set; }

    public int ProductId { get; set; }
}
