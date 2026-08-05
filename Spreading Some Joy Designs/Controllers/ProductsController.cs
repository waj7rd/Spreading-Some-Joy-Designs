using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SpreadingJoy.Domain.EntityModels;
using SpreadingJoy.ViewModels;

namespace SpreadingJoy.Controllers;

public class ProductsController : Controller
{
    private readonly IProductLogic _productLogic;

    public ProductsController(IProductLogic productLogic)
    {
        _productLogic = productLogic;
    }

    // GET /Products — the public garment list. Archived garments aren't shown.
    public async Task<IActionResult> Index()
    {
        var products = await _productLogic.GetActiveAsync();
        return View(products.Select(ToRow).ToList());
    }

    // GET /Products/Manage — staff catalogue, archived included.
    [Authorize(Policy = Policies.ManageCatalog)]
    public async Task<IActionResult> Manage() => View(await BuildCatalogViewModelAsync());

    // GET /Products/Create
    [Authorize(Policy = Policies.ManageCatalog)]
    public IActionResult Create() => View(new EditProductViewModel());

    // POST /Products/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = Policies.ManageCatalog)]
    public async Task<IActionResult> Create(EditProductViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var result = await _productLogic.CreateAsync(ToDetails(model));

        if (!result.Success)
        {
            model.ErrorMessage = result.ErrorMessage;
            return View(model);
        }

        TempData["ProductSuccess"] = $"Added the {model.Colour.Trim()} {model.Name.Trim()}.";
        return RedirectToAction(nameof(Manage));
    }

    // GET /Products/Edit/{id}
    [Authorize(Policy = Policies.ManageCatalog)]
    public async Task<IActionResult> Edit(int id)
    {
        var product = await _productLogic.GetByIdAsync(id);
        if (product == null)
            return NotFound();

        return View(new EditProductViewModel
        {
            ProductId = product.ProductId,
            Name = product.Name,
            Description = product.Description,
            Colour = product.Colour,
            ColourHex = product.ColourHex,
            BasePrice = product.BasePrice,
            PrintSidePrice = product.PrintSidePrice,
            PrintAreaWidthMm = product.PrintAreaWidthMm,
            PrintAreaHeightMm = product.PrintAreaHeightMm,
            ExtendedSizeUpcharge = product.ExtendedSizeUpcharge,
            Sizes = product.Sizes.ToList()
        });
    }

    // POST /Products/Edit
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = Policies.ManageCatalog)]
    public async Task<IActionResult> Edit(EditProductViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var result = await _productLogic.UpdateAsync(model.ProductId, ToDetails(model));

        if (!result.Success)
        {
            model.ErrorMessage = result.ErrorMessage;
            return View(model);
        }

        TempData["ProductSuccess"] = $"Updated the {model.Colour.Trim()} {model.Name.Trim()}.";
        return RedirectToAction(nameof(Manage));
    }

    // POST /Products/SetActive
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = Policies.ManageCatalog)]
    public async Task<IActionResult> SetActive(int id, bool isActive)
    {
        var result = await _productLogic.SetActiveAsync(id, isActive);

        if (!result.Success)
        {
            var viewModel = await BuildCatalogViewModelAsync();
            viewModel.ErrorMessage = result.ErrorMessage;
            return View(nameof(Manage), viewModel);
        }

        TempData["ProductSuccess"] = isActive
            ? "Garment is available again."
            : "Garment archived — it's off the designer, and past orders still show it.";

        return RedirectToAction(nameof(Manage));
    }

    private async Task<ProductCatalogViewModel> BuildCatalogViewModelAsync()
    {
        var products = await _productLogic.GetAllAsync();

        return new ProductCatalogViewModel
        {
            SuccessMessage = TempData["ProductSuccess"] as string,
            Products = products.Select(ToRow).ToList()
        };
    }

    private static ProductDetails ToDetails(EditProductViewModel model) => new(
        Name: model.Name,
        Description: model.Description,
        Colour: model.Colour,
        ColourHex: model.ColourHex,
        BasePrice: model.BasePrice,
        PrintSidePrice: model.PrintSidePrice,
        PrintAreaWidthMm: model.PrintAreaWidthMm,
        PrintAreaHeightMm: model.PrintAreaHeightMm,
        Sizes: model.Sizes,
        ExtendedSizeUpcharge: model.ExtendedSizeUpcharge);

    private static ProductRowViewModel ToRow(Product p) => new()
    {
        Id = p.ProductId,
        Name = p.Name,
        Description = p.Description,
        Colour = p.Colour,
        ColourHex = p.ColourHex,
        BasePrice = p.BasePrice,
        PrintSidePrice = p.PrintSidePrice,
        PrintAreaWidthMm = p.PrintAreaWidthMm,
        PrintAreaHeightMm = p.PrintAreaHeightMm,
        Sizes = p.Sizes,
        ExtendedSizeUpcharge = p.ExtendedSizeUpcharge,
        IsActive = p.IsActive
    };
}
