using SpreadingJoy.Domain.EntityModels;
using SpreadingJoy.Domain.Shared;

namespace SpreadingJoy.Domain.Catalog;

// The garment catalogue. Blank costs move, a supplier discontinues a colourway,
// the studio decides 4XL isn't worth stocking — none of that should require a
// developer.
public interface IProductLogic
{
    // What the storefront shows and what can be designed on.
    Task<IList<Product>> GetActiveAsync();

    // Everything, archived included, for the management screen.
    Task<IList<Product>> GetAllAsync();

    Task<Product?> GetByIdAsync(int productId);

    Task<ProductResult> CreateAsync(ProductDetails details);

    Task<ProductResult> UpdateAsync(int productId, ProductDetails details);

    // Archived products disappear from the designer but stay attached to the
    // designs and orders already built on them.
    Task<ProductResult> SetActiveAsync(int productId, bool isActive);
}

// The editable fields of a product, as one parameter object.
//
// Eleven positional arguments is a call site nobody can read and a signature
// where swapping two decimals compiles cleanly and quietly mis-prices the
// catalogue.
public record ProductDetails(
    string Name,
    string? Description,
    string Colour,
    string ColourHex,
    decimal BasePrice,
    decimal PrintSidePrice,
    int PrintAreaWidthMm,
    int PrintAreaHeightMm,
    IReadOnlyCollection<string> Sizes,
    decimal ExtendedSizeUpcharge);

public class ProductResult : IOperationResult
{
    public bool Success { get; private set; }
    public string? ErrorMessage { get; private set; }
    public int ProductId { get; private set; }

    public static ProductResult Ok(int productId) => new() { Success = true, ProductId = productId };

    public static ProductResult Fail(string message) => new() { Success = false, ErrorMessage = message };
}
