using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using SpreadingJoy.Models;
using SpreadingJoy.ViewModels;

namespace SpreadingJoy.Controllers;

public class HomeController : Controller
{
    private readonly IProductLogic _productLogic;

    public HomeController(IProductLogic productLogic)
    {
        _productLogic = productLogic;
    }

    // GET / — the storefront.
    public async Task<IActionResult> Index()
    {
        var products = await _productLogic.GetActiveAsync();

        return View(products.Select(p => new ProductRowViewModel
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
        }).ToList());
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel
        {
            RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
        });
    }
}
