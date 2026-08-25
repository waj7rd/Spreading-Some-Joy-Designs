using SpreadingJoy.Domain.EntityModels;
using SpreadingJoy.Domain.IRepositories;
using SpreadingJoy.Domain.Shared;

namespace SpreadingJoy.Domain.Production;

public class GangSheetRequestLogic : IGangSheetRequestLogic
{
    // A public form, so these are bounds on what a stranger may submit rather
    // than on what the studio can do. One sheet holding more than this is
    // somebody having typed a quantity wrong, not an order.
    private const int MaxImagesPerSheet = 60;
    private const int MaxCopiesPerImage = 100;
    private const int MaxTransfersPerSheet = 400;

    private const int MaxLabelLength = 120;
    private const int MaxNotesLength = 500;

    private readonly IGangSheetRequestRepository _requestRepository;
    private readonly IGangSheetSizeRepository _sizeRepository;
    private readonly IArtworkRepository _artworkRepository;
    private readonly ICustomerRepository _customerRepository;
    private readonly IGangSheetLogic _gangSheetLogic;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IStudioClock _clock;

    public GangSheetRequestLogic(
        IGangSheetRequestRepository requestRepository,
        IGangSheetSizeRepository sizeRepository,
        IArtworkRepository artworkRepository,
        ICustomerRepository customerRepository,
        IGangSheetLogic gangSheetLogic,
        IUnitOfWork unitOfWork,
        IStudioClock clock)
    {
        _requestRepository = requestRepository;
        _sizeRepository = sizeRepository;
        _artworkRepository = artworkRepository;
        _customerRepository = customerRepository;
        _gangSheetLogic = gangSheetLogic;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    // ---- Building ------------------------------------------------------

    public async Task<SheetPreview?> PreviewAsync(int gangSheetSizeId, IReadOnlyCollection<BuilderItem> items)
    {
        var size = await _sizeRepository.GetAsync(s => s.GangSheetSizeId == gangSheetSizeId);
        if (size == null)
            return null;

        return Pack(size, items);
    }

    // ---- Submitting ----------------------------------------------------

    public async Task<GangSheetRequestResult> SubmitAsync(SubmitGangSheetRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CustomerName))
            return GangSheetRequestResult.Fail("Tell us your name.");

        if (string.IsNullOrWhiteSpace(request.Phone))
            return GangSheetRequestResult.Fail("Leave a phone number so we can reach you.");

        // The gate, not a checkbox that gets recorded. Same rule as
        // OrderLogic.PlaceAsync, and load-bearing for the same reason: this site
        // prints pictures strangers supply, and the studio is the one who
        // receives the takedown notice.
        if (!request.RightsAttested)
            return GangSheetRequestResult.Fail(
                "We need you to confirm you have the right to use this artwork before we can print it.");

        if (request.Items.Count == 0)
            return GangSheetRequestResult.Fail("Put at least one image on the sheet.");

        if (request.Items.Count > MaxImagesPerSheet)
            return GangSheetRequestResult.Fail($"That's more than {MaxImagesPerSheet} different images on one sheet.");

        var size = await _sizeRepository.GetAsync(s => s.GangSheetSizeId == request.GangSheetSizeId);
        if (size == null || !size.IsActive)
            return GangSheetRequestResult.Fail("That sheet size isn't available any more — pick another one.");

        foreach (var item in request.Items)
        {
            var problem = ValidateItem(item);
            if (problem != null)
                return GangSheetRequestResult.Fail(problem);

            // The artwork has to be one we actually hold. The builder only ever
            // puts ids here that came from our own upload path, but the form is
            // a suggestion — a posted id is checked, not trusted.
            var artwork = await _artworkRepository.GetAsync(a => a.ArtworkId == item.ArtworkId);
            if (artwork == null)
                return GangSheetRequestResult.Fail("One of those images is no longer available. Take it off and add it again.");

            // Told now rather than at the end. A hash match against something a
            // moderator already rejected means this sheet can never be printed,
            // and finding that out after filling in the form is worse.
            if (artwork.Status == ArtworkStatus.Rejected)
                return GangSheetRequestResult.Fail($"We can't print \"{Describe(item)}\": {artwork.RejectionReason}");
        }

        if (request.Items.Sum(i => i.Quantity) > MaxTransfersPerSheet)
            return GangSheetRequestResult.Fail($"That's more than {MaxTransfersPerSheet} transfers on one sheet.");

        // Everything has to actually fit. Checked here and not only in the
        // browser, because the preview is a drawing and this is the rule.
        var preview = Pack(size, request.Items);

        if (!preview.Fits)
        {
            var stranded = preview.TooBig.Concat(preview.NoRoom).Distinct().Take(3).ToList();

            return GangSheetRequestResult.Fail(
                $"Not everything fits on a {size.Name}: {string.Join(", ", stranded)}. " +
                "Make them smaller, take some off, or choose a bigger sheet.");
        }

        var gangSheetRequest = new GangSheetRequest
        {
            CustomerName = request.CustomerName.Trim(),
            Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim(),
            Phone = request.Phone.Trim(),
            GangSheetSizeId = size.GangSheetSizeId,

            // Snapshotted. A price rise between asking and being accepted must
            // not restate what this customer agreed to.
            PriceQuoted = size.Price,

            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : Truncate(request.Notes.Trim(), MaxNotesLength),
            RightsAttested = true,
            Status = GangSheetRequestStatus.Pending,
            CreatedAt = _clock.UtcNow
        };

        foreach (var item in request.Items)
        {
            gangSheetRequest.Items.Add(new GangSheetRequestItem
            {
                ArtworkId = item.ArtworkId,
                Label = Truncate(Describe(item), MaxLabelLength),
                WidthMm = item.WidthMm,
                HeightMm = item.HeightMm,
                Quantity = item.Quantity,
                CreatedAt = _clock.UtcNow
            });
        }

        await _requestRepository.AddAsync(gangSheetRequest);
        await _requestRepository.SaveChangesAsync();

        return GangSheetRequestResult.Ok(gangSheetRequest.GangSheetRequestId);
    }

    // ---- The queue -----------------------------------------------------

    public Task<IList<GangSheetRequest>> GetByStatusAsync(string status) =>
        _requestRepository.GetByStatusAsync(GangSheetRequestStatus.All.Contains(status)
            ? status
            : GangSheetRequestStatus.Pending);

    public Task<GangSheetRequest?> GetByIdAsync(int gangSheetRequestId) =>
        _requestRepository.GetWithItemsAsync(gangSheetRequestId);

    public Task<int> CountPendingAsync() => _requestRepository.CountPendingAsync();

    public async Task<GangSheetRequestResult> AcceptAsync(int gangSheetRequestId, int handledByUserId)
    {
        // One transaction around the whole thing. Accepting creates a customer
        // and then a sheet; if putting the transfers on is refused — a piece of
        // artwork got rejected while the request sat in the queue — the customer
        // must not survive it. Same shape as accepting an order request, and it
        // rolls back on a returned failure rather than only on an exception.
        return await _unitOfWork.ExecuteAsync(async () =>
        {
            var request = await _requestRepository.GetWithItemsAsync(gangSheetRequestId);
            if (request == null)
                return GangSheetRequestResult.Fail("Request not found.");

            if (request.Status != GangSheetRequestStatus.Pending)
                return GangSheetRequestResult.Fail("That request has already been handled.");

            if (request.Items.Count == 0)
                return GangSheetRequestResult.Fail("There's nothing on that sheet.");

            // Checked here so the refusal names the problem, rather than letting
            // AddItemsAsync report it after a customer has already been created
            // and rolled back. The gate itself is still AddItemsAsync's — this
            // is the courtesy check, the same arrangement the builder uses.
            //
            // Each row is looked up rather than read off the Artwork navigation
            // property. The repository does Include it, but a check this
            // important must not depend on a caller having remembered to: an
            // unloaded navigation reads as null, null reads as "not approved",
            // and the failure would be a queue nobody can ever accept from.
            var waiting = new List<string>();

            foreach (var item in request.Items)
            {
                var artwork = await _artworkRepository.GetAsync(a => a.ArtworkId == item.ArtworkId);

                if (artwork == null || artwork.Status != ArtworkStatus.Approved)
                    waiting.Add(item.Label);
            }

            waiting = waiting.Distinct().ToList();

            if (waiting.Count > 0)
            {
                return GangSheetRequestResult.Fail(
                    $"Artwork on this sheet still needs approving: {string.Join(", ", waiting.Take(3))}" +
                    (waiting.Count > 3 ? $" and {waiting.Count - 3} more." : ".") +
                    " Approve it in the artwork queue first.");
            }

            var size = await _sizeRepository.GetAsync(s => s.GangSheetSizeId == request.GangSheetSizeId);
            if (size == null)
                return GangSheetRequestResult.Fail("The sheet size this was ordered at no longer exists.");

            var customer = await FindOrCreateCustomerAsync(request);

            var created = await _gangSheetLogic.CreateAsync(
                new GangSheetDetails(
                    Name: $"{request.CustomerName.Trim()} — {size.Name}",
                    WidthMm: size.WidthMm,
                    MaxLengthMm: size.LengthMm,
                    GutterMm: FilmSizes.DefaultGutterMm,
                    MarginMm: FilmSizes.DefaultMarginMm,
                    AllowRotation: true,
                    Notes: request.Notes),
                createdByUserId: handledByUserId);

            if (!created.Success)
                return GangSheetRequestResult.Fail(created.ErrorMessage!);

            // Through the same door studio sheets go through, which is what
            // keeps there being one approval gate rather than two.
            var added = await _gangSheetLogic.AddItemsAsync(created.GangSheetId, request.Items
                .Select(i => new GangSheetItemRequest(
                    ArtworkId: i.ArtworkId,
                    OrderLineId: null,
                    DesignId: null,

                    // No garment behind these, so no face to press them onto.
                    Side: GangSheetSide.Any,
                    Label: i.Label,
                    WidthMm: i.WidthMm,
                    HeightMm: i.HeightMm,
                    Quantity: i.Quantity))
                .ToList());

            if (!added.Success)
                return GangSheetRequestResult.Fail(added.ErrorMessage!);

            var marked = await _gangSheetLogic.MarkAsCustomerSheetAsync(
                created.GangSheetId, customer.CustomerId, size.GangSheetSizeId, request.PriceQuoted);

            if (!marked.Success)
                return GangSheetRequestResult.Fail(marked.ErrorMessage!);

            request.Status = GangSheetRequestStatus.Accepted;
            request.HandledByUserId = handledByUserId;
            request.HandledAt = _clock.UtcNow;
            request.GangSheetId = created.GangSheetId;

            await _requestRepository.SaveChangesAsync();

            return GangSheetRequestResult.Ok(request.GangSheetRequestId, created.GangSheetId);
        });
    }

    public async Task<GangSheetRequestResult> DeclineAsync(int gangSheetRequestId, int handledByUserId, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return GangSheetRequestResult.Fail("Say why it's being declined — the customer sees this.");

        var request = await _requestRepository.GetAsync(r => r.GangSheetRequestId == gangSheetRequestId);
        if (request == null)
            return GangSheetRequestResult.Fail("Request not found.");

        if (request.Status != GangSheetRequestStatus.Pending)
            return GangSheetRequestResult.Fail("That request has already been handled.");

        request.Status = GangSheetRequestStatus.Declined;
        request.DeclineReason = reason.Trim();
        request.HandledByUserId = handledByUserId;
        request.HandledAt = _clock.UtcNow;

        await _requestRepository.SaveChangesAsync();
        return GangSheetRequestResult.Ok(request.GangSheetRequestId);
    }

    // ---- internals -----------------------------------------------------

    // Runs the real packer over what the visitor has arranged. Customer sheets
    // don't get to choose the gutter, the margin or whether things may be
    // turned — those are the studio's business, and a customer picking them
    // would be a customer setting how their film gets cut.
    private static SheetPreview Pack(GangSheetSize size, IReadOnlyCollection<BuilderItem> items)
    {
        // One entry per copy, because that is what has to fit on the film.
        var copies = items
            .SelectMany(item => Enumerable.Range(0, Math.Max(0, item.Quantity)).Select(_ => item))
            .ToList();

        var spec = new GangSheetPacker.SheetSpec(
            size.WidthMm, size.LengthMm,
            FilmSizes.DefaultGutterMm, FilmSizes.DefaultMarginMm,
            AllowRotation: true);

        var result = GangSheetPacker.Pack(
            copies.Select((item, index) => new GangSheetPacker.PackItem(index, item.WidthMm, item.HeightMm)).ToList(),
            spec);

        var placed = result.Placed
            .Select(p =>
            {
                var item = copies[p.Key];

                return new PreviewPlacement(
                    item.ArtworkId,
                    Describe(item),
                    p.XMm,
                    p.YMm,
                    p.Rotated ? item.HeightMm : item.WidthMm,
                    p.Rotated ? item.WidthMm : item.HeightMm,
                    p.Rotated);
            })
            .ToList();

        var area = (double)size.WidthMm * result.UsedLengthMm;

        return new SheetPreview
        {
            GangSheetSizeId = size.GangSheetSizeId,
            SizeName = size.Name,
            WidthMm = size.WidthMm,
            LengthMm = size.LengthMm,
            Price = size.Price,
            UsedLengthMm = result.UsedLengthMm,
            CoveragePercent = area <= 0
                ? 0
                : Math.Round(placed.Sum(p => (double)p.WidthMm * p.HeightMm) / area * 100, 1),
            Placed = placed,
            TooBig = result.Unplaced
                .Where(u => u.Reason == GangSheetPacker.Rejection.TooWideForTheFilm)
                .Select(u => Describe(copies[u.Key]))
                .Distinct()
                .ToList(),
            NoRoom = result.Unplaced
                .Where(u => u.Reason == GangSheetPacker.Rejection.SheetIsFull)
                .Select(u => Describe(copies[u.Key]))
                .Distinct()
                .ToList()
        };
    }

    private static string? ValidateItem(BuilderItem item)
    {
        if (item.Quantity < 1 || item.Quantity > MaxCopiesPerImage)
            return $"Number of copies of \"{Describe(item)}\" has to be between 1 and {MaxCopiesPerImage}.";

        if (item.WidthMm < FilmSizes.MinTransferMm || item.WidthMm > FilmSizes.MaxTransferMm)
            return $"\"{Describe(item)}\" has to be between {FilmSizes.MinTransferMm}mm and {FilmSizes.MaxTransferMm}mm across.";

        if (item.HeightMm < FilmSizes.MinTransferMm || item.HeightMm > FilmSizes.MaxTransferMm)
            return $"\"{Describe(item)}\" has to be between {FilmSizes.MinTransferMm}mm and {FilmSizes.MaxTransferMm}mm tall.";

        return null;
    }

    // Matches on email when there is one, so a repeat customer buying a second
    // sheet doesn't become a second record. No email means no way to tell two
    // people apart — merging on name alone would be worse than duplicating.
    // Same rule as OrderRequestLogic, deliberately.
    private async Task<Customer> FindOrCreateCustomerAsync(GangSheetRequest request)
    {
        var email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();

        if (email != null)
        {
            var existing = await _customerRepository.GetAsync(c => c.Email == email);
            if (existing != null)
                return existing;
        }

        var customer = new Customer
        {
            FullName = request.CustomerName.Trim(),
            Email = email,
            Phone = request.Phone.Trim(),
            IsActive = true,
            CreatedAt = _clock.UtcNow
        };

        await _customerRepository.AddAsync(customer);
        await _customerRepository.SaveChangesAsync();

        return customer;
    }

    private static string Describe(BuilderItem item) =>
        string.IsNullOrWhiteSpace(item.Label) ? "that image" : item.Label.Trim();

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
