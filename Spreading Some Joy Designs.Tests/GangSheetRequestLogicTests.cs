using SpreadingJoy.Tests.Fakes;

namespace SpreadingJoy.Tests;

// Gang sheets built by members of the public.
//
// The two worth reading first are the ones that hold the architecture's rules
// down: a request with unapproved artwork can't be accepted, and a refused
// acceptance leaves no customer behind. Both are the gang sheet versions of
// rules the ordering path already has, and they exist here because this is a
// second way into the same press.
public class GangSheetRequestLogicTests
{
    private static readonly DateTime Now = new(2026, 8, 24, 9, 0, 0);

    private readonly FakeGangSheetRepository _sheets = new();
    private readonly FakeGangSheetSizeRepository _sizes = new();
    private readonly FakeGangSheetRequestRepository _requests = new();
    private readonly FakeArtworkRepository _artworks = new();
    private readonly FakeCustomerRepository _customers = new();
    private readonly FakeUnitOfWork _unitOfWork = new();

    // The studio's settings, so the shipping rules have something to read. Held
    // as a field because a test that turns postage off mid-flight is exactly the
    // interesting case.
    private readonly Studio _studio = new()
    {
        StudioId = 1,
        Name = "Spreading Some Joy Designs",
        TimeZoneId = "UTC",
        DailyPrintCapacity = 60,
        TurnaroundDays = 3,
        OffersShipping = true,
        ShippingFee = 6m
    };

    private GangSheetLogic SheetLogic() =>
        new(_sheets, _artworks, new FixedStudioClock(Now));

    private GangSheetRequestLogic Logic() =>
        new(_requests, _sizes, _artworks, _customers, SheetLogic(),
            new StudioSettingsFromContext(new FakeStudioContext(_studio)),
            _unitOfWork, new FixedStudioClock(Now));

    // A 22 x 24 in sheet — 559mm across, 610mm long.
    private GangSheetSize Size(decimal price = 20m, int lengthMm = 610)
    {
        var size = new GangSheetSize
        {
            GangSheetSizeId = 1,
            Name = "22 × 24 in",
            WidthMm = 559,
            LengthMm = lengthMm,
            Price = price,
            IsActive = true
        };

        _sizes.Seed(size);
        return size;
    }

    private Artwork Artwork(int id, string status = ArtworkStatus.Approved)
    {
        var artwork = new Artwork
        {
            ArtworkId = id,
            StoredFileName = $"{id}.png",
            ContentType = "image/png",
            Sha256 = new string('a', 64),
            WidthPx = 2000,
            HeightPx = 2000,
            Status = status
        };

        _artworks.Seed(artwork);
        return artwork;
    }

    // Collection by default: it's the choice that needs no address, so a test
    // about something else doesn't have to care about shipping.
    private static readonly ShippingAddress Somewhere =
        new("14 Elm Street", null, "Wright City", "MO", "63390");

    private static SubmitGangSheetRequest Submission(
        IReadOnlyCollection<BuilderItem>? items = null,
        bool rightsAttested = true,
        string name = "Ashley",
        string? email = "ashley@example.test",
        string method = FulfilmentMethod.Pickup,
        ShippingAddress? shipTo = null) =>
        new(
            CustomerName: name,
            Email: email,
            Phone: "555-0100",
            GangSheetSizeId: 1,
            Items: items ?? [new BuilderItem(1, "Logo", 200, 200, 1)],
            Notes: null,
            RightsAttested: rightsAttested,
            FulfilmentMethod: method,
            ShipTo: shipTo ?? ShippingAddress.None);

    private static SubmitGangSheetRequest Posted(ShippingAddress? shipTo = null) =>
        Submission(method: FulfilmentMethod.Shipping, shipTo: shipTo ?? Somewhere);

    // ---- Submitting -----------------------------------------------------

    [Fact]
    public async Task A_sheet_can_be_asked_for_without_an_account()
    {
        Size();
        Artwork(1, ArtworkStatus.Pending);

        var result = await Logic().SubmitAsync(Submission());

        Assert.True(result.Success, result.ErrorMessage);

        // The whole point of the holding table. Nothing a stranger typed has
        // become a customer, and nothing they uploaded has become a sheet.
        Assert.Empty(_customers.All);
        Assert.Empty(_sheets.All);
    }

    [Fact]
    public async Task Artwork_still_waiting_for_review_does_not_stop_somebody_asking()
    {
        // Deliberate. The approval gate belongs at the press, not at the point
        // a customer is trying to give the studio money — the order form works
        // the same way.
        Size();
        Artwork(1, ArtworkStatus.Pending);

        Assert.True((await Logic().SubmitAsync(Submission())).Success);
    }

    [Fact]
    public async Task Artwork_a_moderator_already_rejected_is_refused_at_the_point_of_asking()
    {
        // Told now rather than at the end: this sheet can never be printed, and
        // finding that out after filling in the form is worse.
        Size();
        Artwork(1, ArtworkStatus.Rejected).RejectionReason = "That's a film character.";

        var result = await Logic().SubmitAsync(Submission());

        Assert.False(result.Success);
        Assert.Contains("film character", result.ErrorMessage!);
    }

    [Fact]
    public async Task Nothing_is_printed_without_the_rights_attestation()
    {
        Size();
        Artwork(1);

        var result = await Logic().SubmitAsync(Submission(rightsAttested: false));

        Assert.False(result.Success);
        Assert.Contains("right to use", result.ErrorMessage!);
        Assert.Empty(_requests.All);
    }

    [Fact]
    public async Task A_name_and_a_phone_number_are_required()
    {
        Size();
        Artwork(1);

        Assert.False((await Logic().SubmitAsync(Submission(name: " "))).Success);
    }

    [Fact]
    public async Task An_empty_sheet_cannot_be_asked_for()
    {
        Size();

        var result = await Logic().SubmitAsync(Submission(items: []));

        Assert.False(result.Success);
        Assert.Contains("at least one image", result.ErrorMessage!);
    }

    [Fact]
    public async Task A_sheet_where_something_does_not_fit_is_refused()
    {
        // Twelve 200x200 transfers won't go on 559 x 610mm of film.
        Size();
        Artwork(1);

        var result = await Logic().SubmitAsync(Submission(
            items: [new BuilderItem(1, "Logo", 200, 200, 12)]));

        Assert.False(result.Success);
        Assert.Contains("fits", result.ErrorMessage!);
        Assert.Empty(_requests.All);
    }

    [Fact]
    public async Task The_price_is_snapshotted_when_it_is_asked_for()
    {
        // A price rise between asking and being accepted must not restate what
        // the customer agreed to. Same rule as OrderLines.UnitPrice.
        var size = Size(price: 20m);
        Artwork(1);

        var result = await Logic().SubmitAsync(Submission());
        Assert.True(result.Success);

        size.Price = 35m;

        var request = await Logic().GetByIdAsync(result.GangSheetRequestId);
        Assert.Equal(20m, request!.PriceQuoted);
    }

    [Fact]
    public async Task A_withdrawn_sheet_size_cannot_be_ordered()
    {
        var size = Size();
        size.IsActive = false;
        Artwork(1);

        var result = await Logic().SubmitAsync(Submission());

        Assert.False(result.Success);
        Assert.Contains("isn't available", result.ErrorMessage!);
    }

    [Fact]
    public async Task An_artwork_id_that_is_not_ours_is_refused()
    {
        // The builder only ever posts ids that came from our own upload path,
        // but the form is a suggestion — a posted id is checked, not trusted.
        Size();

        var result = await Logic().SubmitAsync(Submission(
            items: [new BuilderItem(999, "Nothing", 100, 100, 1)]));

        Assert.False(result.Success);
    }

    // ---- Previewing -----------------------------------------------------

    [Fact]
    public async Task The_preview_uses_the_same_packer_the_real_sheet_does()
    {
        Size();
        Artwork(1);

        var preview = await Logic().PreviewAsync(1, [new BuilderItem(1, "Logo", 200, 200, 2)]);

        Assert.NotNull(preview);
        Assert.True(preview!.Fits);
        Assert.Equal(2, preview.Placed.Count);

        // 6mm margin + 200 + 6mm margin. Two fit side by side across 559mm.
        Assert.Equal(212, preview.UsedLengthMm);
    }

    [Fact]
    public async Task The_preview_says_which_images_would_not_fit()
    {
        Size();
        Artwork(1);

        var preview = await Logic().PreviewAsync(1, [new BuilderItem(1, "Big one", 900, 900, 1)]);

        Assert.NotNull(preview);
        Assert.False(preview!.Fits);
        Assert.Contains("Big one", preview.TooBig);
    }

    [Fact]
    public async Task Copies_are_packed_one_by_one_because_that_is_what_the_film_has_to_hold()
    {
        Size();
        Artwork(1);

        var preview = await Logic().PreviewAsync(1, [new BuilderItem(1, "Logo", 100, 100, 5)]);

        Assert.Equal(5, preview!.Placed.Count);
    }

    // ---- Accepting ------------------------------------------------------

    [Fact]
    public async Task Accepting_creates_the_customer_and_the_sheet()
    {
        Size();
        Artwork(1);

        var submitted = await Logic().SubmitAsync(Submission());
        var result = await Logic().AcceptAsync(submitted.GangSheetRequestId, handledByUserId: 1);

        Assert.True(result.Success, result.ErrorMessage);

        var customer = Assert.Single(_customers.All);
        Assert.Equal("Ashley", customer.FullName);

        var sheet = Assert.Single(_sheets.All);
        Assert.Equal(GangSheetOrigin.Customer, sheet.Origin);
        Assert.Equal(customer.CustomerId, sheet.CustomerId);
        Assert.Equal(20m, sheet.Price);
        Assert.Equal(GangSheetStatus.Draft, sheet.Status);
    }

    [Fact]
    public async Task A_request_with_artwork_nobody_has_approved_cannot_be_accepted()
    {
        // The gate. A request can sit in the queue for days, and it is the state
        // of the artwork now that decides whether film gets used.
        Size();
        Artwork(1, ArtworkStatus.Pending);

        var submitted = await Logic().SubmitAsync(Submission());
        var result = await Logic().AcceptAsync(submitted.GangSheetRequestId, handledByUserId: 1);

        Assert.False(result.Success);
        Assert.Contains("needs approving", result.ErrorMessage!);
        Assert.Empty(_sheets.All);
    }

    [Fact]
    public async Task A_refused_acceptance_leaves_no_customer_behind()
    {
        // The Unit of Work rolls back on a returned failure, not just an
        // exception — this codebase reports refusals as results, so a
        // transaction that only caught exceptions would commit the orphan.
        Size();
        var artwork = Artwork(1);

        var submitted = await Logic().SubmitAsync(Submission());

        // Rejected while the request sat in the queue.
        artwork.Status = ArtworkStatus.Rejected;

        var result = await Logic().AcceptAsync(submitted.GangSheetRequestId, handledByUserId: 1);

        Assert.False(result.Success);
        Assert.True(_unitOfWork.RolledBack);
    }

    [Fact]
    public async Task A_repeat_customer_does_not_become_a_second_record()
    {
        Size();
        Artwork(1);
        Artwork(2);

        var first = await Logic().SubmitAsync(Submission());
        await Logic().AcceptAsync(first.GangSheetRequestId, handledByUserId: 1);

        var second = await Logic().SubmitAsync(Submission(
            items: [new BuilderItem(2, "Another", 150, 150, 1)]));
        await Logic().AcceptAsync(second.GangSheetRequestId, handledByUserId: 1);

        Assert.Single(_customers.All);
        Assert.Equal(2, _sheets.All.Count);
    }

    [Fact]
    public async Task A_request_cannot_be_accepted_twice()
    {
        Size();
        Artwork(1);

        var submitted = await Logic().SubmitAsync(Submission());
        Assert.True((await Logic().AcceptAsync(submitted.GangSheetRequestId, 1)).Success);

        var again = await Logic().AcceptAsync(submitted.GangSheetRequestId, 1);

        Assert.False(again.Success);
        Assert.Contains("already been handled", again.ErrorMessage!);
        Assert.Single(_sheets.All);
    }

    [Fact]
    public async Task The_copies_asked_for_become_one_transfer_each_on_the_sheet()
    {
        Size();
        Artwork(1);

        var submitted = await Logic().SubmitAsync(Submission(
            items: [new BuilderItem(1, "Logo", 100, 100, 4)]));

        await Logic().AcceptAsync(submitted.GangSheetRequestId, handledByUserId: 1);

        var sheet = Assert.Single(_sheets.All);
        Assert.Equal(4, sheet.Items.Count);

        // No garment behind these, so no face to press them onto.
        Assert.All(sheet.Items, i => Assert.Equal(GangSheetSide.Any, i.Side));
    }

    // ---- Declining ------------------------------------------------------

    [Fact]
    public async Task Declining_needs_a_reason_because_the_customer_reads_it()
    {
        Size();
        Artwork(1);

        var submitted = await Logic().SubmitAsync(Submission());

        var result = await Logic().DeclineAsync(submitted.GangSheetRequestId, 1, "  ");

        Assert.False(result.Success);
        Assert.Contains("Say why", result.ErrorMessage!);
    }

    [Fact]
    public async Task A_declined_request_never_becomes_a_sheet()
    {
        Size();
        Artwork(1);

        var submitted = await Logic().SubmitAsync(Submission());
        Assert.True((await Logic().DeclineAsync(submitted.GangSheetRequestId, 1, "Artwork isn't yours.")).Success);

        Assert.Empty(_sheets.All);
        Assert.Empty(_customers.All);

        var accepted = await Logic().AcceptAsync(submitted.GangSheetRequestId, 1);
        Assert.False(accepted.Success);
    }

    // ---- Posting it ------------------------------------------------------

    [Fact]
    public async Task A_sheet_can_be_asked_for_by_post()
    {
        Size();
        Artwork(1);

        var result = await Logic().SubmitAsync(Posted());
        Assert.True(result.Success, result.ErrorMessage);

        var request = await Logic().GetByIdAsync(result.GangSheetRequestId);

        Assert.True(request!.IsShipping);
        Assert.Equal("14 Elm Street", request.ShipToLine1);
        Assert.Equal(6m, request.ShippingFee);

        // Sheet plus postage, which is what they were quoted.
        Assert.Equal(26m, request.TotalQuoted);
    }

    [Fact]
    public async Task Posting_is_refused_without_an_address()
    {
        // The same Fulfilment.Check garment orders go through. A sheet asked for
        // by post with nothing to write on the envelope is not an order.
        Size();
        Artwork(1);

        var result = await Logic().SubmitAsync(Posted(new ShippingAddress(null, null, null, null, null)));

        Assert.False(result.Success);
        Assert.Contains("street address", result.ErrorMessage!);
        Assert.Empty(_requests.All);
    }

    [Fact]
    public async Task Posting_is_refused_when_the_studio_is_not_shipping()
    {
        // The switch is on the studio record, not on the sheet catalogue. A shop
        // that isn't posting shirts isn't posting film either.
        _studio.OffersShipping = false;
        Size();
        Artwork(1);

        var result = await Logic().SubmitAsync(Posted());

        Assert.False(result.Success);
        Assert.Contains("not shipping at the moment", result.ErrorMessage!);
    }

    [Fact]
    public async Task A_collection_sheet_keeps_no_address_even_if_one_was_posted()
    {
        // Fulfilment.ToStore drops it. A half-filled address sitting on a
        // collection order is the kind of thing that later gets read as a label.
        Size();
        Artwork(1);

        var result = await Logic().SubmitAsync(
            Submission(method: FulfilmentMethod.Pickup, shipTo: Somewhere));

        var request = await Logic().GetByIdAsync(result.GangSheetRequestId);

        Assert.Null(request!.ShipToLine1);
        Assert.Equal(0m, request.ShippingFee);
    }

    [Fact]
    public async Task Postage_is_snapshotted_when_the_sheet_is_asked_for()
    {
        Size();
        Artwork(1);

        var result = await Logic().SubmitAsync(Posted());
        Assert.True(result.Success);

        _studio.ShippingFee = 25m;

        var request = await Logic().GetByIdAsync(result.GangSheetRequestId);
        Assert.Equal(6m, request!.ShippingFee);
    }

    [Fact]
    public async Task Turning_shipping_off_after_a_sheet_was_asked_for_blocks_the_acceptance()
    {
        // The rules run again at acceptance, not only at submission — the same
        // shape as OrderRequestLogic, and for the same reason: the world moves
        // while a request sits in a queue.
        Size();
        Artwork(1);

        var submitted = await Logic().SubmitAsync(Posted());
        Assert.True(submitted.Success);

        _studio.OffersShipping = false;

        var accepted = await Logic().AcceptAsync(submitted.GangSheetRequestId, handledByUserId: 1);

        Assert.False(accepted.Success);
        Assert.Contains("not shipping at the moment", accepted.ErrorMessage!);
        Assert.Empty(_sheets.All);
    }

    [Fact]
    public async Task A_collection_sheet_is_never_refused_for_any_of_that()
    {
        // Switching postage off must not stop the studio taking sheet orders.
        _studio.OffersShipping = false;
        Size();
        Artwork(1);

        var submitted = await Logic().SubmitAsync(Submission());
        Assert.True(submitted.Success, submitted.ErrorMessage);

        Assert.True((await Logic().AcceptAsync(submitted.GangSheetRequestId, 1)).Success);
    }

    [Fact]
    public async Task Accepting_carries_the_address_and_the_postage_onto_the_sheet()
    {
        Size();
        Artwork(1);

        var submitted = await Logic().SubmitAsync(Posted());
        Assert.True((await Logic().AcceptAsync(submitted.GangSheetRequestId, 1)).Success);

        var sheet = Assert.Single(_sheets.All);

        Assert.True(sheet.IsShipping);
        Assert.Equal("14 Elm Street", sheet.ShipToLine1);
        Assert.Equal("Wright City", sheet.ShipToCity);
        Assert.Equal(6m, sheet.ShippingFee);
        Assert.Equal(26m, sheet.Total);
    }
}
