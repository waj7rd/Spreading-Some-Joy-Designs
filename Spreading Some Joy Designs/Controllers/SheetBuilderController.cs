using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SpreadingJoy.Domain.EntityModels;
using SpreadingJoy.Domain.Production;
using SpreadingJoy.ViewModels;

namespace SpreadingJoy.Controllers;

// The public gang sheet builder. Anonymous, like the designer and for the same
// reason: somebody should be able to land on the site, arrange their own images
// on a sheet of film and ask for it without making an account first.
//
// The sheet lives in the session while it's being built. Nothing here creates a
// Customer and nothing here creates a GangSheet — submitting writes a
// GangSheetRequest, and only staff accepting it turns that into either.
//
// Every change posts back and the server repacks. That's a page load where a
// bit of JavaScript would have felt smoother, and it's deliberate: the preview
// has to be laid out by the same packer that lays out the real sheet, and a
// second implementation in the browser would be a second answer to "where does
// this go" — one of which would be wrong at exactly the wrong moment.
public class SheetBuilderController : Controller
{
    private const string SessionSizeKey = "sheetbuilder.sizeId";
    private const string SessionItemsKey = "sheetbuilder.items";

    // One image is allowed to be as wide as the film. Below this it's a speck
    // nobody can cut out.
    private const int MinWidthMm = FilmSizes.MinTransferMm;

    private readonly IGangSheetRequestLogic _requestLogic;
    private readonly IGangSheetSizeLogic _sizeLogic;
    private readonly IArtworkLogic _artworkLogic;
    private readonly IImageStore _imageStore;

    public SheetBuilderController(
        IGangSheetRequestLogic requestLogic,
        IGangSheetSizeLogic sizeLogic,
        IArtworkLogic artworkLogic,
        IImageStore imageStore)
    {
        _requestLogic = requestLogic;
        _sizeLogic = sizeLogic;
        _artworkLogic = artworkLogic;
        _imageStore = imageStore;
    }

    // GET /SheetBuilder
    public async Task<IActionResult> Index()
    {
        var sizes = await _sizeLogic.GetActiveAsync();
        if (sizes.Count == 0)
            return View("NoSheets");

        // Default to the smallest, which is the cheapest — a visitor who hasn't
        // chosen shouldn't find themselves quoted for a whole roll.
        var sizeId = GetSessionSize() ?? sizes[0].GangSheetSizeId;

        if (sizes.All(s => s.GangSheetSizeId != sizeId))
            sizeId = sizes[0].GangSheetSizeId;

        SetSessionSize(sizeId);

        var items = GetSessionItems();

        return View(await BuildAsync(sizes, sizeId, items));
    }

    // POST /SheetBuilder/ChooseSize
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult ChooseSize(int gangSheetSizeId)
    {
        // The images stay. Moving to a bigger sheet is the obvious answer to
        // "it doesn't fit", and making somebody re-add everything to do it
        // would be the site arguing with them.
        SetSessionSize(gangSheetSizeId);
        return RedirectToAction(nameof(Index));
    }

    // POST /SheetBuilder/AddFromUrl
    //
    // Rate limited hard: every call makes our server fetch an address a stranger
    // chose. Same policy the designer uses, and the same reasoning — see the
    // architecture notes on HttpImageFetcher.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(RateLimitPolicies.ArtworkFetch)]
    public async Task<IActionResult> AddFromUrl(string? url, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            TempData["BuilderError"] = "Paste the address of an image.";
            return RedirectToAction(nameof(Index));
        }

        var result = await _artworkLogic.AddFromUrlAsync(
            url, customerId: null, approvedByUserId: null, cancellationToken);

        return await AfterArtworkAddedAsync(result, label: null);
    }

    // POST /SheetBuilder/AddFromUpload
    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(RateLimitPolicies.ArtworkFetch)]
    [RequestSizeLimit(ImageLimits.MaxBytes + 1024 * 1024)]
    public async Task<IActionResult> AddFromUpload(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0)
        {
            TempData["BuilderError"] = "Choose a file to upload.";
            return RedirectToAction(nameof(Index));
        }

        if (file.Length > ImageLimits.MaxBytes)
        {
            TempData["BuilderError"] = $"That file is over {ImageLimits.MaxBytes / (1024 * 1024)}MB.";
            return RedirectToAction(nameof(Index));
        }

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, cancellationToken);

        // file.FileName is display only. It never reaches a path — the store
        // builds the filename from the content hash.
        var result = await _artworkLogic.AddFromUploadAsync(
            buffer.ToArray(), file.FileName, customerId: null, approvedByUserId: null, cancellationToken);

        return await AfterArtworkAddedAsync(result, Path.GetFileNameWithoutExtension(file.FileName));
    }

    // POST /SheetBuilder/UpdateItem — resize, or ask for more copies.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateItem(int index, int widthMm, int quantity)
    {
        var items = GetSessionItems();

        if (index < 0 || index >= items.Count)
            return RedirectToAction(nameof(Index));

        var item = items[index];

        var width = Math.Clamp(widthMm, MinWidthMm, FilmSizes.MaxTransferMm);
        var copies = Math.Clamp(quantity, 1, 100);

        // The height follows the width. Letting somebody set both independently
        // would let them stretch their own artwork without meaning to, and they
        // would only find out when it came off the press.
        var artwork = await _artworkLogic.GetByIdAsync(item.ArtworkId);
        var height = HeightFor(artwork, width);

        items[index] = item with { WidthMm = width, HeightMm = height, Quantity = copies };
        SetSessionItems(items);

        return RedirectToAction(nameof(Index));
    }

    // POST /SheetBuilder/RemoveItem
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult RemoveItem(int index)
    {
        var items = GetSessionItems();

        if (index >= 0 && index < items.Count)
        {
            items.RemoveAt(index);
            SetSessionItems(items);
        }

        return RedirectToAction(nameof(Index));
    }

    // POST /SheetBuilder/Submit
    //
    // An anonymous POST that writes to the database, so it carries the same
    // ceiling the order form does. A real person builds a sheet, thinks about
    // it, maybe builds another.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(RateLimitPolicies.PublicOrdering)]
    //
    // The form is rendered from the builder's model, so its fields are named
    // "Submit.*". Without the prefix the binder finds nothing, every field comes
    // back empty, and the page reports a validation failure whose cause is
    // invisible — the fields it is complaining about look filled in.
    public async Task<IActionResult> Submit(
        [Bind(Prefix = nameof(SheetBuilderViewModel.Submit))] SubmitSheetViewModel model)
    {
        var sizeId = GetSessionSize();
        var items = GetSessionItems();

        if (sizeId == null || items.Count == 0)
        {
            TempData["BuilderError"] = "Put at least one image on the sheet first.";
            return RedirectToAction(nameof(Index));
        }

        if (!ModelState.IsValid)
        {
            var sizes = await _sizeLogic.GetActiveAsync();
            var invalid = await BuildAsync(sizes, sizeId.Value, items);
            invalid.Submit = model;
            invalid.ErrorMessage = "Check your details below.";
            return View(nameof(Index), invalid);
        }

        var result = await _requestLogic.SubmitAsync(new SubmitGangSheetRequest(
            CustomerName: model.CustomerName,
            Email: model.Email,
            Phone: model.Phone,
            GangSheetSizeId: sizeId.Value,
            Items: items,
            Notes: model.Notes,
            RightsAttested: model.RightsAttested));

        if (!result.Success)
        {
            var sizes = await _sizeLogic.GetActiveAsync();
            var failed = await BuildAsync(sizes, sizeId.Value, items);
            failed.Submit = model;
            failed.ErrorMessage = result.ErrorMessage;
            return View(nameof(Index), failed);
        }

        var request = await _requestLogic.GetByIdAsync(result.GangSheetRequestId);

        // Clear the session, so the next sheet starts from blank film rather
        // than inheriting the one just asked for.
        HttpContext.Session.Remove(SessionSizeKey);
        HttpContext.Session.Remove(SessionItemsKey);

        return View("Submitted", new SheetSubmittedViewModel
        {
            CustomerName = model.CustomerName.Trim(),
            SizeName = request?.GangSheetSize?.Name ?? string.Empty,
            Price = request?.PriceQuoted ?? 0m,
            TransferCount = request?.TransferCount ?? 0,
            AnyAwaitingReview = request?.Items.Any(i => i.Artwork?.Status != ArtworkStatus.Approved) ?? true
        });
    }

    // ---- internals -----------------------------------------------------

    private async Task<IActionResult> AfterArtworkAddedAsync(ArtworkResult result, string? label)
    {
        if (!result.Success)
        {
            TempData["BuilderError"] = result.ErrorMessage;
            return RedirectToAction(nameof(Index));
        }

        var artwork = await _artworkLogic.GetByIdAsync(result.ArtworkId);

        // A hash match against something a moderator already rejected. Saying so
        // now is far better than letting them arrange it, fill in the form and
        // be refused at the end.
        if (artwork?.Status == ArtworkStatus.Rejected)
        {
            TempData["BuilderError"] = $"We can't print that image: {artwork.RejectionReason}";
            return RedirectToAction(nameof(Index));
        }

        var items = GetSessionItems();

        // Starts as wide as it can go without dropping under 150 DPI, capped at
        // the film width. The first thing somebody sees should be a sensible
        // size rather than a postage stamp they have to fix.
        var sizes = await _sizeLogic.GetActiveAsync();
        var sizeId = GetSessionSize() ?? (sizes.Count > 0 ? sizes[0].GangSheetSizeId : 0);
        var size = sizes.FirstOrDefault(s => s.GangSheetSizeId == sizeId);

        var ceiling = size == null
            ? FilmSizes.MaxTransferMm
            : size.WidthMm - (2 * FilmSizes.DefaultMarginMm);

        var sharpWidth = artwork == null ? ceiling : ImageLimits.MaxPrintableWidthMm(artwork.WidthPx);
        var width = Math.Clamp(Math.Min(sharpWidth, ceiling), MinWidthMm, FilmSizes.MaxTransferMm);

        items.Add(new BuilderItem(
            ArtworkId: result.ArtworkId,
            Label: string.IsNullOrWhiteSpace(label) ? $"Image {items.Count + 1}" : label.Trim(),
            WidthMm: width,
            HeightMm: HeightFor(artwork, width),
            Quantity: 1));

        SetSessionItems(items);

        TempData["BuilderPending"] =
            "Added. Every image gets a quick look from us before it goes to the press, " +
            "so you can ask for the sheet now and we'll confirm shortly.";

        return RedirectToAction(nameof(Index));
    }

    private async Task<SheetBuilderViewModel> BuildAsync(
        IList<GangSheetSize> sizes, int sizeId, IReadOnlyList<BuilderItem> items)
    {
        var model = new SheetBuilderViewModel
        {
            GangSheetSizeId = sizeId,
            Sizes = sizes.Select(s => new SheetSizeOptionViewModel
            {
                Id = s.GangSheetSizeId,
                Name = s.Name,
                WidthMm = s.WidthMm,
                LengthMm = s.LengthMm,
                Price = s.Price
            }).ToList(),
            SuccessMessage = TempData["BuilderSuccess"] as string,
            ErrorMessage = TempData["BuilderError"] as string,
            PendingNotice = TempData["BuilderPending"] as string
        };

        var rows = new List<BuilderItemViewModel>();

        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var artwork = await _artworkLogic.GetByIdAsync(item.ArtworkId);

            rows.Add(new BuilderItemViewModel
            {
                Index = index,
                ArtworkId = item.ArtworkId,
                Label = item.Label,
                ImageUrl = artwork == null ? string.Empty : _imageStore.PublicPath(artwork.StoredFileName),
                WidthMm = item.WidthMm,
                HeightMm = item.HeightMm,
                Quantity = item.Quantity,
                ArtworkStatus = artwork?.Status ?? ArtworkStatus.Pending,
                RejectionReason = artwork?.RejectionReason,
                Dpi = artwork == null ? 0 : ImageLimits.EffectiveDpi(artwork.WidthPx, item.WidthMm),
                MaxSharpWidthMm = artwork == null ? 0 : ImageLimits.MaxPrintableWidthMm(artwork.WidthPx)
            });
        }

        model.Items = rows;

        if (items.Count > 0)
        {
            var preview = await _requestLogic.PreviewAsync(sizeId, items);

            if (preview != null)
            {
                // Images by artwork id, so the preview can draw each transfer as
                // the picture it will print without a lookup per placement.
                var imagesById = rows
                    .GroupBy(r => r.ArtworkId)
                    .ToDictionary(g => g.Key, g => g.First().ImageUrl);

                model.Preview = new SheetPreviewViewModel
                {
                    WidthMm = preview.WidthMm,
                    LengthMm = preview.LengthMm,
                    UsedLengthMm = preview.UsedLengthMm,
                    CoveragePercent = preview.CoveragePercent,
                    Price = preview.Price,
                    TooBig = preview.TooBig,
                    NoRoom = preview.NoRoom,
                    Placed = preview.Placed.Select(p => new PreviewItemViewModel
                    {
                        Label = p.Label,
                        ImageUrl = imagesById.TryGetValue(p.ArtworkId, out var url) ? url : string.Empty,
                        Rotated = p.Rotated,
                        LeftPercent = Percent(p.XMm, preview.WidthMm),
                        TopPercent = Percent(p.YMm, preview.LengthMm),
                        WidthPercent = Percent(p.WidthMm, preview.WidthMm),
                        HeightPercent = Percent(p.HeightMm, preview.LengthMm)
                    }).ToList()
                };
            }
        }

        return model;
    }

    private static double Percent(int value, int total) =>
        total <= 0 ? 0 : Math.Round(value / (double)total * 100, 3);

    // Height follows width, from the image's own proportions. A square fallback
    // when the artwork can't be read at all, so a missing row can't produce a
    // zero-height transfer the packer would happily place nowhere.
    private static int HeightFor(Artwork? artwork, int widthMm)
    {
        if (artwork == null || artwork.WidthPx <= 0 || artwork.HeightPx <= 0)
            return widthMm;

        var height = (int)Math.Round(widthMm * (artwork.HeightPx / (double)artwork.WidthPx));
        return Math.Clamp(height, MinWidthMm, FilmSizes.MaxTransferMm);
    }

    private int? GetSessionSize()
    {
        var value = HttpContext.Session.GetInt32(SessionSizeKey);
        return value is null or 0 ? null : value;
    }

    private void SetSessionSize(int sizeId) => HttpContext.Session.SetInt32(SessionSizeKey, sizeId);

    private List<BuilderItem> GetSessionItems()
    {
        var json = HttpContext.Session.GetString(SessionItemsKey);
        if (string.IsNullOrEmpty(json))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<BuilderItem>>(json) ?? [];
        }
        catch (JsonException)
        {
            // Session state written by an older build of the site. Starting the
            // visitor over is annoying; a 500 on the page they were using is
            // worse, and there is nothing here worth recovering.
            return [];
        }
    }

    private void SetSessionItems(IReadOnlyList<BuilderItem> items) =>
        HttpContext.Session.SetString(SessionItemsKey, JsonSerializer.Serialize(items));
}
