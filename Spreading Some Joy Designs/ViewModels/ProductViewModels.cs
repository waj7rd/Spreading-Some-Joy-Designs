using System.ComponentModel.DataAnnotations;
using SpreadingJoy.ViewModels.Validation;

namespace SpreadingJoy.ViewModels;

public class ProductRowViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Colour { get; set; } = string.Empty;
    public string ColourHex { get; set; } = "#ffffff";
    public decimal BasePrice { get; set; }
    public decimal PrintSidePrice { get; set; }
    public int PrintAreaWidthMm { get; set; }
    public int PrintAreaHeightMm { get; set; }
    public IReadOnlyList<string> Sizes { get; set; } = [];
    public decimal ExtendedSizeUpcharge { get; set; }
    public bool IsActive { get; set; }

    // What a plain one-sided print costs in the smallest size — the number the
    // storefront leads with, so it has to be the honest floor rather than the
    // base price with the printing left off.
    public decimal FromPrice => BasePrice + PrintSidePrice;
}

public class ProductCatalogViewModel
{
    public string? SuccessMessage { get; set; }
    public string? ErrorMessage { get; set; }
    public IList<ProductRowViewModel> Products { get; set; } = [];
}

public class EditProductViewModel
{
    public int ProductId { get; set; }

    [Required(ErrorMessage = "Give the garment a name.")]
    [StringLength(100)]
    [Display(Name = "Name")]
    public string Name { get; set; } = string.Empty;

    [StringLength(500)]
    [Display(Name = "Description")]
    public string? Description { get; set; }

    [Required(ErrorMessage = "Give the garment a colour.")]
    [StringLength(50)]
    [Display(Name = "Colour")]
    public string Colour { get; set; } = string.Empty;

    [Required]
    [RegularExpression(ValidationPatterns.HexColour, ErrorMessage = ValidationPatterns.HexColourMessage)]
    [Display(Name = "Swatch")]
    public string ColourHex { get; set; } = "#ffffff";

    [Range(0, 1000, ErrorMessage = "Base price has to be between 0 and 1000.")]
    [DataType(DataType.Currency)]
    [Display(Name = "Blank cost")]
    public decimal BasePrice { get; set; }

    [Range(0, 1000, ErrorMessage = "Per-side print price has to be between 0 and 1000.")]
    [DataType(DataType.Currency)]
    [Display(Name = "Per printed side")]
    public decimal PrintSidePrice { get; set; }

    [Range(20, 600, ErrorMessage = "Print area width has to be between 20mm and 600mm.")]
    [Display(Name = "Print area width (mm)")]
    public int PrintAreaWidthMm { get; set; } = 305;

    [Range(20, 600, ErrorMessage = "Print area height has to be between 20mm and 600mm.")]
    [Display(Name = "Print area height (mm)")]
    public int PrintAreaHeightMm { get; set; } = 406;

    [Range(0, 1000, ErrorMessage = "Upcharge has to be between 0 and 1000.")]
    [DataType(DataType.Currency)]
    [Display(Name = "Extended size upcharge")]
    public decimal ExtendedSizeUpcharge { get; set; }

    [MinLength(1, ErrorMessage = "Offer at least one size.")]
    [Display(Name = "Sizes")]
    public List<string> Sizes { get; set; } = ["S", "M", "L", "XL"];

    public string? ErrorMessage { get; set; }
}
