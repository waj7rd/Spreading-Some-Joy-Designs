namespace SpreadingJoy.ViewModels;

// A studio design as it appears in the shop.
public class StudioDesignViewModel
{
    public int Id { get; set; }

    // What goes in the "order this" link. Studio designs are public by nature,
    // but they use the same addressing as everything else so there's only one
    // way designs are referenced in URLs.
    public Guid PublicToken { get; set; }

    public string Name { get; set; } = string.Empty;
    public string GarmentName { get; set; } = string.Empty;
    public string ColourHex { get; set; } = "#ffffff";
    public decimal Price { get; set; }
    public int PrintedSides { get; set; }
    public IReadOnlyList<string> Sizes { get; set; } = [];
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }

    public ShirtPreviewViewModel Front { get; set; } = new() { Side = "front" };
    public ShirtPreviewViewModel Back { get; set; } = new() { Side = "back" };

    // Staff-only. A studio design whose artwork somehow isn't approved can't be
    // ordered, and the management screen has to say so rather than leaving
    // somebody wondering why the shop looks empty.
    public string? ArtworkStatusWarning { get; set; }
}

public class ShopViewModel
{
    public IList<StudioDesignViewModel> Designs { get; set; } = [];
    public string? SuccessMessage { get; set; }
    public string? ErrorMessage { get; set; }
}
