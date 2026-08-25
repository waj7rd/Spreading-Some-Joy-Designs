using SpreadingJoy.Domain.EntityModels;
using SpreadingJoy.Domain.IRepositories;
using SpreadingJoy.Domain.Shared;

namespace SpreadingJoy.Domain.Production;

public class GangSheetLogic : IGangSheetLogic
{
    // One sheet holding more than this is somebody having typed a quantity
    // wrong, not a print run. The packer would cope; the screen that draws them
    // would not, and neither would whoever has to cut them out.
    private const int MaxItemsPerSheet = 400;

    // How many copies one request may add at once. A single order line asking
    // for more than this is worth a second look before it eats a roll of film.
    private const int MaxCopiesPerRequest = 100;

    private const int MaxNameLength = 100;
    private const int MaxLabelLength = 120;
    private const int MaxNotesLength = 500;

    private readonly IGangSheetRepository _gangSheetRepository;
    private readonly IArtworkRepository _artworkRepository;
    private readonly IStudioClock _clock;

    public GangSheetLogic(
        IGangSheetRepository gangSheetRepository,
        IArtworkRepository artworkRepository,
        IStudioClock clock)
    {
        _gangSheetRepository = gangSheetRepository;
        _artworkRepository = artworkRepository;
        _clock = clock;
    }

    public async Task<IList<GangSheet>> GetAllAsync()
    {
        var sheets = await _gangSheetRepository.GetAllWithItemsAsync();

        // Drafts and sheets at the press first — they're the ones somebody is
        // working on. Printed sheets are history, newest first.
        return sheets
            .OrderBy(s => s.Status == GangSheetStatus.Printed ? 1 : 0)
            .ThenByDescending(s => s.CreatedAt)
            .ToList();
    }

    public Task<GangSheet?> GetAsync(int gangSheetId) =>
        _gangSheetRepository.GetWithItemsAsync(gangSheetId);

    public async Task<GangSheetResult> CreateAsync(GangSheetDetails details, int? createdByUserId)
    {
        var validation = Validate(details);
        if (validation != null)
            return GangSheetResult.Fail(validation);

        var sheet = new GangSheet
        {
            CreatedByUserId = createdByUserId,
            CreatedAt = _clock.UtcNow,
            Status = GangSheetStatus.Draft
        };

        Apply(sheet, details);

        await _gangSheetRepository.AddAsync(sheet);
        await _gangSheetRepository.SaveChangesAsync();

        return GangSheetResult.Ok(sheet.GangSheetId);
    }

    public async Task<GangSheetResult> UpdateAsync(int gangSheetId, GangSheetDetails details)
    {
        var validation = Validate(details);
        if (validation != null)
            return GangSheetResult.Fail(validation);

        var sheet = await _gangSheetRepository.GetWithItemsAsync(gangSheetId);
        if (sheet == null)
            return GangSheetResult.Fail("Gang sheet not found.");

        if (!sheet.IsEditable)
            return GangSheetResult.Fail(LockedMessage(sheet));

        Apply(sheet, details);

        // Narrowing the film or widening the gutter moves every transfer on the
        // sheet, so the layout is rebuilt rather than left describing a sheet
        // that no longer exists.
        Repack(sheet);

        await _gangSheetRepository.SaveChangesAsync();
        return GangSheetResult.Ok(sheet.GangSheetId);
    }

    public async Task<GangSheetResult> DeleteAsync(int gangSheetId)
    {
        var sheet = await _gangSheetRepository.GetWithItemsAsync(gangSheetId);
        if (sheet == null)
            return GangSheetResult.Fail("Gang sheet not found.");

        // A printed sheet is the record of a piece of film that exists and
        // whose transfers are on somebody's shirts. Deleting it would leave the
        // order with no answer to "what did we actually print".
        if (sheet.Status == GangSheetStatus.Printed)
            return GangSheetResult.Fail("That sheet has already been printed — it stays as the record of what was on it.");

        _gangSheetRepository.Delete(sheet);
        await _gangSheetRepository.SaveChangesAsync();

        return GangSheetResult.Ok(gangSheetId);
    }

    public async Task<GangSheetResult> AddItemsAsync(int gangSheetId, IReadOnlyCollection<GangSheetItemRequest> requests)
    {
        var sheet = await _gangSheetRepository.GetWithItemsAsync(gangSheetId);
        if (sheet == null)
            return GangSheetResult.Fail("Gang sheet not found.");

        if (!sheet.IsEditable)
            return GangSheetResult.Fail(LockedMessage(sheet));

        if (requests.Count == 0)
            return GangSheetResult.Fail("Pick something to put on the sheet.");

        var copies = requests.Sum(r => r.Quantity);

        if (sheet.Items.Count + copies > MaxItemsPerSheet)
            return GangSheetResult.Fail($"That would put more than {MaxItemsPerSheet} transfers on one sheet. Start a second one.");

        // Everything is checked before anything is added, so a bad request in
        // the middle of a batch doesn't leave half of it on the sheet.
        var approved = new List<(GangSheetItemRequest Request, Artwork Artwork)>();

        foreach (var request in requests)
        {
            var problem = ValidateRequest(request);
            if (problem != null)
                return GangSheetResult.Fail(problem);

            var artwork = await _artworkRepository.GetAsync(a => a.ArtworkId == request.ArtworkId);
            if (artwork == null)
                return GangSheetResult.Fail("That artwork no longer exists.");

            // The approval gate, at the last moment it can be applied. Nothing
            // reaches film without a person having looked at it — the same rule
            // DesignLogic.ValidateForOrderAsync enforces at order time, and for
            // the same reason. Pending is not approved.
            if (artwork.Status != ArtworkStatus.Approved)
            {
                return GangSheetResult.Fail(artwork.Status == ArtworkStatus.Rejected
                    ? $"'{Describe(request)}' uses artwork that was rejected. It can't be printed."
                    : $"'{Describe(request)}' uses artwork nobody has approved yet. Approve it first.");
            }

            approved.Add((request, artwork));
        }

        foreach (var (request, artwork) in approved)
        {
            // One row per copy. Twelve shirts needing the same front is twelve
            // transfers, each of which has to land somewhere on the film.
            for (var copy = 0; copy < request.Quantity; copy++)
            {
                var item = new GangSheetItem
                {
                    GangSheetId = sheet.GangSheetId,
                    ArtworkId = artwork.ArtworkId,
                    Artwork = artwork,
                    OrderLineId = request.OrderLineId,
                    DesignId = request.DesignId,
                    Side = request.Side,
                    Label = Truncate(request.Label.Trim(), MaxLabelLength),
                    WidthMm = request.WidthMm,
                    HeightMm = request.HeightMm,
                    CreatedAt = _clock.UtcNow
                };

                sheet.Items.Add(item);
                await _gangSheetRepository.AddItemAsync(item);
            }
        }

        Repack(sheet);

        await _gangSheetRepository.SaveChangesAsync();
        return GangSheetResult.Ok(sheet.GangSheetId);
    }

    public async Task<GangSheetResult> RemoveItemAsync(int gangSheetId, int gangSheetItemId)
    {
        var sheet = await _gangSheetRepository.GetWithItemsAsync(gangSheetId);
        if (sheet == null)
            return GangSheetResult.Fail("Gang sheet not found.");

        if (!sheet.IsEditable)
            return GangSheetResult.Fail(LockedMessage(sheet));

        var item = sheet.Items.FirstOrDefault(i => i.GangSheetItemId == gangSheetItemId);
        if (item == null)
            return GangSheetResult.Fail("That transfer isn't on this sheet.");

        sheet.Items.Remove(item);
        _gangSheetRepository.RemoveItem(item);

        // Taking one out leaves a hole. Everything below it moves up, which is
        // the whole point of removing it — the sheet gets shorter and the film
        // costs less.
        Repack(sheet);

        await _gangSheetRepository.SaveChangesAsync();
        return GangSheetResult.Ok(sheet.GangSheetId);
    }

    public async Task<GangSheetResult> RepackAsync(int gangSheetId)
    {
        var sheet = await _gangSheetRepository.GetWithItemsAsync(gangSheetId);
        if (sheet == null)
            return GangSheetResult.Fail("Gang sheet not found.");

        if (!sheet.IsEditable)
            return GangSheetResult.Fail(LockedMessage(sheet));

        Repack(sheet);

        await _gangSheetRepository.SaveChangesAsync();
        return GangSheetResult.Ok(sheet.GangSheetId);
    }

    public async Task<GangSheetResult> MarkReadyAsync(int gangSheetId)
    {
        var sheet = await _gangSheetRepository.GetWithItemsAsync(gangSheetId);
        if (sheet == null)
            return GangSheetResult.Fail("Gang sheet not found.");

        if (sheet.Status != GangSheetStatus.Draft)
            return GangSheetResult.Fail("Only a draft can be marked ready.");

        if (sheet.Items.Count == 0)
            return GangSheetResult.Fail("There's nothing on this sheet yet.");

        // Repack before checking, so "everything fits" is judged against a
        // current layout rather than one from before the last change.
        Repack(sheet);

        // The check that matters. A draft can sit open for days, and artwork
        // can be rejected while it does — so the state of the artwork when a
        // transfer was added says nothing about the state of it now. This is
        // the last gate before the film.
        var unapproved = sheet.Items
            .Where(i => i.Artwork == null || i.Artwork.Status != ArtworkStatus.Approved)
            .Select(i => i.Label)
            .Distinct()
            .ToList();

        if (unapproved.Count > 0)
        {
            return GangSheetResult.Fail(
                $"Artwork on this sheet isn't approved: {string.Join(", ", unapproved.Take(3))}" +
                (unapproved.Count > 3 ? $" and {unapproved.Count - 3} more." : ".") +
                " Take them off, or get them approved.");
        }

        if (sheet.UnplacedCount > 0)
        {
            return GangSheetResult.Fail(
                $"{sheet.UnplacedCount} transfer(s) didn't fit on the film. Take them off, make the sheet longer, or start a second one.");
        }

        sheet.Status = GangSheetStatus.Ready;

        await _gangSheetRepository.SaveChangesAsync();
        return GangSheetResult.Ok(sheet.GangSheetId);
    }

    public async Task<GangSheetResult> MarkPrintedAsync(int gangSheetId)
    {
        var sheet = await _gangSheetRepository.GetWithItemsAsync(gangSheetId);
        if (sheet == null)
            return GangSheetResult.Fail("Gang sheet not found.");

        if (sheet.Status != GangSheetStatus.Ready)
            return GangSheetResult.Fail("Mark the sheet ready before printing it.");

        sheet.Status = GangSheetStatus.Printed;
        sheet.PrintedAt = _clock.UtcNow;

        await _gangSheetRepository.SaveChangesAsync();
        return GangSheetResult.Ok(sheet.GangSheetId);
    }

    public async Task<GangSheetResult> ReopenAsync(int gangSheetId)
    {
        var sheet = await _gangSheetRepository.GetWithItemsAsync(gangSheetId);
        if (sheet == null)
            return GangSheetResult.Fail("Gang sheet not found.");

        // Printed is one-way. That piece of film exists, and pretending it is a
        // draft again would let the record of what ran be edited afterwards.
        if (sheet.Status != GangSheetStatus.Ready)
            return GangSheetResult.Fail("Only a sheet waiting at the press can be reopened.");

        sheet.Status = GangSheetStatus.Draft;

        await _gangSheetRepository.SaveChangesAsync();
        return GangSheetResult.Ok(sheet.GangSheetId);
    }

    public async Task<GangSheetResult> MarkAsCustomerSheetAsync(
        int gangSheetId, int customerId, int gangSheetSizeId, decimal price)
    {
        var sheet = await _gangSheetRepository.GetWithItemsAsync(gangSheetId);
        if (sheet == null)
            return GangSheetResult.Fail("Gang sheet not found.");

        // Once, and only on the way out of a request being accepted. A sheet
        // that already belongs to somebody being reassigned would mean the film
        // one customer paid for could quietly become another customer's.
        if (sheet.Origin == GangSheetOrigin.Customer)
            return GangSheetResult.Fail("That sheet already belongs to a customer.");

        sheet.Origin = GangSheetOrigin.Customer;
        sheet.CustomerId = customerId;
        sheet.GangSheetSizeId = gangSheetSizeId;
        sheet.Price = price;

        await _gangSheetRepository.SaveChangesAsync();
        return GangSheetResult.Ok(sheet.GangSheetId);
    }

    public async Task<IList<TransferCandidate>> GetCandidatesAsync()
    {
        var lines = await _gangSheetRepository.GetCandidateLinesAsync();

        var placed = lines.Count == 0
            ? new Dictionary<int, int>()
            : await _gangSheetRepository.CountPlacementsByOrderLineAsync(lines.Select(l => l.OrderLineId).ToList());

        var candidates = new List<TransferCandidate>();

        foreach (var line in lines)
        {
            var design = line.Design;
            if (design == null)
                continue;

            // Each printed side is its own transfer. A front-and-back design on
            // a run of three shirts is six pieces of film, and the cut list has
            // to say which is which.
            Add(candidates, line, design, GangSheetSide.Front, design.FrontArtwork,
                design.FrontWidthMm, design.FrontHeightMm, placed);

            Add(candidates, line, design, GangSheetSide.Back, design.BackArtwork,
                design.BackWidthMm, design.BackHeightMm, placed);
        }

        return candidates
            .OrderBy(c => c.DueOn)
            .ThenBy(c => c.OrderId)
            .ThenBy(c => c.Side == GangSheetSide.Front ? 0 : 1)
            .ToList();
    }

    private static void Add(
        List<TransferCandidate> candidates,
        OrderLine line,
        Design design,
        string side,
        Artwork? artwork,
        int? widthMm,
        int? heightMm,
        IDictionary<int, int> placed)
    {
        // A side with no artwork on it prints nothing. A side with artwork but
        // no size is a design saved before it was finished, and inventing a size
        // for it would put the wrong thing on film.
        if (artwork == null || widthMm is not > 0 || heightMm is not > 0)
            return;

        placed.TryGetValue(line.OrderLineId, out var already);

        candidates.Add(new TransferCandidate
        {
            OrderLineId = line.OrderLineId,
            OrderId = line.OrderId,
            DesignId = design.DesignId,
            ArtworkId = artwork.ArtworkId,
            Side = side,
            Label = $"#{line.OrderId} {design.Name} ({side.ToLowerInvariant()}, {line.SizeCode})",
            DesignName = design.Name,
            CustomerName = line.Order?.Customer?.FullName,
            SizeCode = line.SizeCode,
            DueOn = line.Order?.DueOn ?? DateTime.MinValue,
            Quantity = line.Quantity,
            WidthMm = widthMm.Value,
            HeightMm = heightMm.Value,
            ArtworkWidthPx = artwork.WidthPx,
            ArtworkStatus = artwork.Status,
            StoredFileName = artwork.StoredFileName,
            AlreadyPlaced = already
        });
    }

    // Runs the packer and writes the answer back onto the items. The only place
    // positions are assigned — everything that changes a sheet ends up here, so
    // there is one layout rule rather than one per operation.
    private static void Repack(GangSheet sheet)
    {
        var items = sheet.Items.OrderBy(i => i.GangSheetItemId).ThenBy(i => i.Label).ToList();

        var spec = new GangSheetPacker.SheetSpec(
            sheet.WidthMm, sheet.MaxLengthMm, sheet.GutterMm, sheet.MarginMm, sheet.AllowRotation);

        var result = GangSheetPacker.Pack(
            items.Select((item, index) => new GangSheetPacker.PackItem(index, item.WidthMm, item.HeightMm)).ToList(),
            spec);

        // Everything starts unplaced, so an item the packer didn't mention
        // can't keep a position it held before the sheet changed shape.
        foreach (var item in items)
        {
            item.IsPlaced = false;
            item.XMm = 0;
            item.YMm = 0;
            item.Rotated = false;
        }

        foreach (var placement in result.Placed)
        {
            var item = items[placement.Key];
            item.IsPlaced = true;
            item.XMm = placement.XMm;
            item.YMm = placement.YMm;
            item.Rotated = placement.Rotated;
        }

        sheet.UsedLengthMm = result.UsedLengthMm;
    }

    private static void Apply(GangSheet sheet, GangSheetDetails details)
    {
        sheet.Name = details.Name.Trim();
        sheet.WidthMm = details.WidthMm;
        sheet.MaxLengthMm = details.MaxLengthMm;
        sheet.GutterMm = details.GutterMm;
        sheet.MarginMm = details.MarginMm;
        sheet.AllowRotation = details.AllowRotation;
        sheet.Notes = string.IsNullOrWhiteSpace(details.Notes) ? null : Truncate(details.Notes.Trim(), MaxNotesLength);
    }

    private static string? Validate(GangSheetDetails details)
    {
        if (string.IsNullOrWhiteSpace(details.Name))
            return "Give the sheet a name.";

        if (details.Name.Trim().Length > MaxNameLength)
            return $"Keep the name under {MaxNameLength} characters.";

        if (details.WidthMm < FilmSizes.MinWidthMm || details.WidthMm > FilmSizes.MaxWidthMm)
            return $"Film width has to be between {FilmSizes.MinWidthMm}mm and {FilmSizes.MaxWidthMm}mm.";

        if (details.MaxLengthMm < FilmSizes.MinLengthMm || details.MaxLengthMm > FilmSizes.MaxLengthMm)
            return $"Sheet length has to be between {FilmSizes.MinLengthMm}mm and {FilmSizes.MaxLengthMm}mm.";

        if (details.GutterMm < 0 || details.GutterMm > FilmSizes.MaxGutterMm)
            return $"Gutter has to be between 0mm and {FilmSizes.MaxGutterMm}mm.";

        if (details.MarginMm < 0 || details.MarginMm > FilmSizes.MaxMarginMm)
            return $"Margin has to be between 0mm and {FilmSizes.MaxMarginMm}mm.";

        // Margins come off both edges, so a sheet can be configured with nothing
        // left in the middle. Caught here rather than left to the packer, which
        // would silently refuse every transfer put on it.
        if (details.MarginMm * 2 >= details.WidthMm)
            return "Those margins leave no film to print on.";

        if (details.MarginMm * 2 >= details.MaxLengthMm)
            return "Those margins are longer than the sheet.";

        return null;
    }

    private static string? ValidateRequest(GangSheetItemRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Label))
            return "Every transfer needs a label — it's what gets read off the cut list.";

        if (!GangSheetSide.IsKnown(request.Side))
            return $"'{request.Side}' isn't a side we print.";

        if (request.Quantity < 1 || request.Quantity > MaxCopiesPerRequest)
            return $"Number of copies has to be between 1 and {MaxCopiesPerRequest}.";

        if (request.WidthMm < FilmSizes.MinTransferMm || request.WidthMm > FilmSizes.MaxTransferMm)
            return $"'{Describe(request)}' is {request.WidthMm}mm wide — that has to be between {FilmSizes.MinTransferMm}mm and {FilmSizes.MaxTransferMm}mm.";

        if (request.HeightMm < FilmSizes.MinTransferMm || request.HeightMm > FilmSizes.MaxTransferMm)
            return $"'{Describe(request)}' is {request.HeightMm}mm tall — that has to be between {FilmSizes.MinTransferMm}mm and {FilmSizes.MaxTransferMm}mm.";

        return null;
    }

    private static string Describe(GangSheetItemRequest request) =>
        string.IsNullOrWhiteSpace(request.Label) ? "That transfer" : request.Label.Trim();

    private static string LockedMessage(GangSheet sheet) => sheet.Status == GangSheetStatus.Printed
        ? "That sheet has already been printed."
        : "That sheet is waiting at the press. Reopen it first if it needs changing.";

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
