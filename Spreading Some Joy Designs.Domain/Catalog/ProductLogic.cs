using System.Text.RegularExpressions;
using SpreadingJoy.Domain.EntityModels;
using SpreadingJoy.Domain.IRepositories;

namespace SpreadingJoy.Domain.Catalog;

public partial class ProductLogic : IProductLogic
{
    // A print area has to be big enough to be worth printing and small enough
    // to fit on a garment. A wrong figure here silently breaks every placement
    // check downstream, which is why it's bounded rather than trusted.
    private const int MinPrintAreaMm = 20;
    private const int MaxPrintAreaMm = 600;

    // Nobody sells a shirt for five figures, and a stray zero on a price is the
    // kind of mistake that only shows up in an angry email.
    private const decimal MaxPrice = 1000m;

    private readonly IProductRepository _productRepository;

    public ProductLogic(IProductRepository productRepository)
    {
        _productRepository = productRepository;
    }

    public async Task<IList<Product>> GetActiveAsync()
    {
        var all = await _productRepository.GetAllAsync();
        return all.Where(p => p.IsActive).OrderBy(p => p.Name).ThenBy(p => p.Colour).ToList();
    }

    public async Task<IList<Product>> GetAllAsync()
    {
        var all = await _productRepository.GetAllAsync();
        return all.OrderByDescending(p => p.IsActive).ThenBy(p => p.Name).ThenBy(p => p.Colour).ToList();
    }

    public async Task<Product?> GetByIdAsync(int productId) =>
        await _productRepository.GetAsync(p => p.ProductId == productId);

    public async Task<ProductResult> CreateAsync(ProductDetails details)
    {
        var validation = Validate(details);
        if (validation != null)
            return ProductResult.Fail(validation);

        var name = details.Name.Trim();
        var colour = details.Colour.Trim();

        // Name and colour together identify a garment: the same tee in black and
        // in white are two products, and two black ones are a duplicate.
        var clash = await _productRepository.GetAsync(p => p.Name == name && p.Colour == colour);
        if (clash != null)
            return ProductResult.Fail($"There's already a {colour} {name} in the catalogue.");

        var product = new Product { IsActive = true };
        Apply(product, details);

        await _productRepository.AddAsync(product);
        await _productRepository.SaveChangesAsync();

        return ProductResult.Ok(product.ProductId);
    }

    public async Task<ProductResult> UpdateAsync(int productId, ProductDetails details)
    {
        var validation = Validate(details);
        if (validation != null)
            return ProductResult.Fail(validation);

        var product = await _productRepository.GetAsync(p => p.ProductId == productId);
        if (product == null)
            return ProductResult.Fail("Product not found.");

        var name = details.Name.Trim();
        var colour = details.Colour.Trim();

        var clash = await _productRepository.GetAsync(p =>
            p.Name == name && p.Colour == colour && p.ProductId != productId);

        if (clash != null)
            return ProductResult.Fail($"There's already a {colour} {name} in the catalogue.");

        // Shrinking the print area can strand placements on designs that were
        // valid when they were saved. Those designs aren't rewritten here —
        // OrderLogic re-checks placement at order time, so a stranded design is
        // refused at the point it would have gone to press rather than silently
        // moved without the customer's say-so.
        Apply(product, details);

        await _productRepository.SaveChangesAsync();
        return ProductResult.Ok(product.ProductId);
    }

    public async Task<ProductResult> SetActiveAsync(int productId, bool isActive)
    {
        var product = await _productRepository.GetAsync(p => p.ProductId == productId);
        if (product == null)
            return ProductResult.Fail("Product not found.");

        // Archiving the last one would leave nothing to design on at all.
        if (!isActive)
        {
            var others = await _productRepository.FindByAsync(p => p.IsActive && p.ProductId != productId);
            if (others.Count == 0)
                return ProductResult.Fail("That's the only garment left — add another before archiving this one.");
        }

        product.IsActive = isActive;
        await _productRepository.SaveChangesAsync();

        return ProductResult.Ok(product.ProductId);
    }

    private static void Apply(Product product, ProductDetails details)
    {
        product.Name = details.Name.Trim();
        product.Description = string.IsNullOrWhiteSpace(details.Description) ? null : details.Description.Trim();
        product.Colour = details.Colour.Trim();
        product.ColourHex = details.ColourHex.Trim().ToLowerInvariant();
        product.BasePrice = details.BasePrice;
        product.PrintSidePrice = details.PrintSidePrice;
        product.PrintAreaWidthMm = details.PrintAreaWidthMm;
        product.PrintAreaHeightMm = details.PrintAreaHeightMm;
        product.ExtendedSizeUpcharge = details.ExtendedSizeUpcharge;

        // Stored in the studio's preferred display order, not the order the
        // form happened to submit them in.
        product.SizesRaw = string.Join(',', details.Sizes
            .Select(s => s.Trim().ToUpperInvariant())
            .Distinct()
            .OrderBy(Sizes.SortKey));
    }

    private static string? Validate(ProductDetails details)
    {
        if (string.IsNullOrWhiteSpace(details.Name))
            return "Give the garment a name.";

        if (string.IsNullOrWhiteSpace(details.Colour))
            return "Give the garment a colour.";

        if (!HexColour().IsMatch(details.ColourHex?.Trim() ?? string.Empty))
            return "Colour swatch has to be a hex value like #1a1a1a.";

        if (details.BasePrice < 0 || details.BasePrice > MaxPrice)
            return $"Base price has to be between 0 and {MaxPrice:C}.";

        if (details.PrintSidePrice < 0 || details.PrintSidePrice > MaxPrice)
            return $"Per-side print price has to be between 0 and {MaxPrice:C}.";

        if (details.ExtendedSizeUpcharge < 0 || details.ExtendedSizeUpcharge > MaxPrice)
            return $"Extended-size upcharge has to be between 0 and {MaxPrice:C}.";

        if (details.PrintAreaWidthMm < MinPrintAreaMm || details.PrintAreaWidthMm > MaxPrintAreaMm)
            return $"Print area width has to be between {MinPrintAreaMm}mm and {MaxPrintAreaMm}mm.";

        if (details.PrintAreaHeightMm < MinPrintAreaMm || details.PrintAreaHeightMm > MaxPrintAreaMm)
            return $"Print area height has to be between {MinPrintAreaMm}mm and {MaxPrintAreaMm}mm.";

        if (details.Sizes.Count == 0)
            return "Offer at least one size.";

        var unknown = details.Sizes.FirstOrDefault(s => !EntityModels.Sizes.IsKnown(s.Trim()));
        if (unknown != null)
            return $"'{unknown}' isn't a size we stock.";

        return null;
    }

    [GeneratedRegex(@"^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColour();
}
