using SpreadingJoy.Tests.Fakes;

namespace SpreadingJoy.Tests;

// The rules around building a sheet. The one worth reading first is
// Artwork_rejected_after_it_was_added_stops_the_sheet_going_to_the_press — a
// gang sheet is the last thing that happens before ink meets film, which makes
// it the last place the approval gate can be applied.
public class GangSheetLogicTests
{
    private static readonly DateTime Now = new(2026, 8, 24, 9, 0, 0);

    private readonly FakeGangSheetRepository _sheets = new();
    private readonly FakeArtworkRepository _artworks = new();

    private GangSheetLogic Logic() =>
        new(_sheets, _artworks, new FixedStudioClock(Now));

    private Artwork Artwork(int id, string status = ArtworkStatus.Approved, int widthPx = 2000)
    {
        var artwork = new Artwork
        {
            ArtworkId = id,
            StoredFileName = $"{id}.png",
            ContentType = "image/png",
            Sha256 = new string('a', 64),
            WidthPx = widthPx,
            HeightPx = widthPx,
            Status = status
        };

        _artworks.Seed(artwork);
        return artwork;
    }

    private static GangSheetDetails Details(
        string name = "Week of the 3rd",
        int widthMm = 560,
        int maxLengthMm = 1520) =>
        new(name, widthMm, maxLengthMm, GutterMm: 6, MarginMm: 6, AllowRotation: true, Notes: null);

    private static GangSheetItemRequest Request(
        int artworkId = 1,
        int quantity = 1,
        int widthMm = 200,
        int heightMm = 250,
        string side = GangSheetSide.Front,
        string label = "#1 Sunshine (front, M)") =>
        new(artworkId, OrderLineId: 1, DesignId: 1, side, label, widthMm, heightMm, quantity);

    private async Task<int> NewSheetAsync(GangSheetDetails? details = null)
    {
        var result = await Logic().CreateAsync(details ?? Details(), createdByUserId: 1);
        Assert.True(result.Success, result.ErrorMessage);
        return result.GangSheetId;
    }

    // ---- Creating -------------------------------------------------------

    [Fact]
    public async Task A_new_sheet_starts_as_a_draft_with_nothing_on_it()
    {
        var id = await NewSheetAsync();
        var sheet = await Logic().GetAsync(id);

        Assert.NotNull(sheet);
        Assert.Equal(GangSheetStatus.Draft, sheet!.Status);
        Assert.Empty(sheet.Items);
        Assert.Equal(0, sheet.UsedLengthMm);
    }

    [Fact]
    public async Task A_sheet_needs_a_name()
    {
        var result = await Logic().CreateAsync(Details(name: "  "), createdByUserId: 1);

        Assert.False(result.Success);
        Assert.Contains("name", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Margins_that_leave_no_film_are_refused_before_they_are_saved()
    {
        var result = await Logic().CreateAsync(
            new GangSheetDetails("Silly", WidthMm: 100, MaxLengthMm: 1000, GutterMm: 0, MarginMm: 50, AllowRotation: true, Notes: null),
            createdByUserId: 1);

        Assert.False(result.Success);
        Assert.Contains("no film", result.ErrorMessage!);
    }

    // ---- The approval gate ----------------------------------------------

    [Fact]
    public async Task Artwork_nobody_has_approved_yet_cannot_be_put_on_a_sheet()
    {
        Artwork(1, ArtworkStatus.Pending);
        var id = await NewSheetAsync();

        var result = await Logic().AddItemsAsync(id, [Request()]);

        Assert.False(result.Success);
        Assert.Contains("approved", result.ErrorMessage!);
    }

    [Fact]
    public async Task Rejected_artwork_cannot_be_put_on_a_sheet()
    {
        Artwork(1, ArtworkStatus.Rejected);
        var id = await NewSheetAsync();

        var result = await Logic().AddItemsAsync(id, [Request()]);

        Assert.False(result.Success);
        Assert.Contains("rejected", result.ErrorMessage!);
    }

    [Fact]
    public async Task Artwork_rejected_after_it_was_added_stops_the_sheet_going_to_the_press()
    {
        // The gate that actually matters. A draft can sit open for days, and
        // the state of the artwork when the transfer was added says nothing
        // about the state of it now.
        var artwork = Artwork(1);
        var id = await NewSheetAsync();

        Assert.True((await Logic().AddItemsAsync(id, [Request()])).Success);

        artwork.Status = ArtworkStatus.Rejected;

        var result = await Logic().MarkReadyAsync(id);

        Assert.False(result.Success);
        Assert.Contains("isn't approved", result.ErrorMessage!);

        var sheet = await Logic().GetAsync(id);
        Assert.Equal(GangSheetStatus.Draft, sheet!.Status);
    }

    [Fact]
    public async Task Nothing_is_added_when_one_transfer_in_the_batch_is_refused()
    {
        Artwork(1);
        Artwork(2, ArtworkStatus.Pending);
        var id = await NewSheetAsync();

        var result = await Logic().AddItemsAsync(id,
        [
            Request(artworkId: 1),
            Request(artworkId: 2, label: "#2 Not approved (front, L)")
        ]);

        Assert.False(result.Success);

        var sheet = await Logic().GetAsync(id);
        Assert.Empty(sheet!.Items);
    }

    // ---- Copies ---------------------------------------------------------

    [Fact]
    public async Task Twelve_shirts_needing_the_same_front_is_twelve_transfers()
    {
        // One row per physical copy. Each of the twelve has to be somewhere on
        // the film and each one has its own cut.
        Artwork(1);
        var id = await NewSheetAsync();

        await Logic().AddItemsAsync(id, [Request(quantity: 12)]);

        var sheet = await Logic().GetAsync(id);
        Assert.Equal(12, sheet!.Items.Count);
    }

    [Fact]
    public async Task A_transfer_bigger_than_any_garment_is_refused()
    {
        Artwork(1);
        var id = await NewSheetAsync();

        var result = await Logic().AddItemsAsync(id, [Request(widthMm: 5000, heightMm: 100)]);

        Assert.False(result.Success);
        Assert.Contains("wide", result.ErrorMessage!);
    }

    // ---- Packing --------------------------------------------------------

    [Fact]
    public async Task Adding_transfers_packs_them_and_reports_the_film_used()
    {
        Artwork(1);
        var id = await NewSheetAsync();

        await Logic().AddItemsAsync(id, [Request(quantity: 2, widthMm: 200, heightMm: 250)]);

        var sheet = await Logic().GetAsync(id);

        Assert.All(sheet!.Items, i => Assert.True(i.IsPlaced));

        // Two 200mm transfers fit across 548mm of usable film, so one row:
        // 6 margin + 250 + 6 margin.
        Assert.Equal(262, sheet.UsedLengthMm);
    }

    [Fact]
    public async Task Taking_one_off_shortens_the_sheet()
    {
        Artwork(1);
        var id = await NewSheetAsync();

        // Three across 548mm of usable film is two rows.
        await Logic().AddItemsAsync(id, [Request(quantity: 3, widthMm: 200, heightMm: 250)]);

        var before = (await Logic().GetAsync(id))!.UsedLengthMm;

        var sheet = (await Logic().GetAsync(id))!;
        await Logic().RemoveItemAsync(id, sheet.Items.First().GangSheetItemId);

        var after = (await Logic().GetAsync(id))!.UsedLengthMm;

        Assert.True(after < before, $"expected the sheet to get shorter, was {before}mm and is {after}mm");
    }

    [Fact]
    public async Task A_sheet_with_something_that_did_not_fit_cannot_go_to_the_press()
    {
        Artwork(1);

        // Only room for one row on this one.
        var id = await NewSheetAsync(Details(maxLengthMm: 300));

        await Logic().AddItemsAsync(id, [Request(quantity: 3, widthMm: 200, heightMm: 250)]);

        var sheet = await Logic().GetAsync(id);
        Assert.Equal(1, sheet!.UnplacedCount);

        var result = await Logic().MarkReadyAsync(id);

        Assert.False(result.Success);
        Assert.Contains("didn't fit", result.ErrorMessage!);
    }

    [Fact]
    public async Task An_unplaced_transfer_is_kept_rather_than_dropped()
    {
        // Silently losing it is how a customer's order goes missing.
        Artwork(1);
        var id = await NewSheetAsync(Details(maxLengthMm: 300));

        await Logic().AddItemsAsync(id, [Request(quantity: 3, widthMm: 200, heightMm: 250)]);

        var sheet = await Logic().GetAsync(id);

        Assert.Equal(3, sheet!.Items.Count);
        Assert.Equal(2, sheet.PlacedCount);
    }

    // ---- The status chain -----------------------------------------------

    [Fact]
    public async Task An_empty_sheet_cannot_be_marked_ready()
    {
        var id = await NewSheetAsync();

        var result = await Logic().MarkReadyAsync(id);

        Assert.False(result.Success);
        Assert.Contains("nothing on this sheet", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_sheet_waiting_at_the_press_refuses_to_be_edited()
    {
        Artwork(1);
        var id = await NewSheetAsync();
        await Logic().AddItemsAsync(id, [Request()]);
        Assert.True((await Logic().MarkReadyAsync(id)).Success);

        var added = await Logic().AddItemsAsync(id, [Request()]);
        var repacked = await Logic().RepackAsync(id);

        Assert.False(added.Success);
        Assert.False(repacked.Success);
        Assert.Contains("waiting at the press", added.ErrorMessage!);
    }

    [Fact]
    public async Task A_sheet_at_the_press_can_be_reopened_and_a_printed_one_cannot()
    {
        Artwork(1);
        var id = await NewSheetAsync();
        await Logic().AddItemsAsync(id, [Request()]);
        await Logic().MarkReadyAsync(id);

        Assert.True((await Logic().ReopenAsync(id)).Success);
        Assert.Equal(GangSheetStatus.Draft, (await Logic().GetAsync(id))!.Status);

        await Logic().MarkReadyAsync(id);
        Assert.True((await Logic().MarkPrintedAsync(id)).Success);

        var reopened = await Logic().ReopenAsync(id);

        Assert.False(reopened.Success);
        Assert.Equal(GangSheetStatus.Printed, (await Logic().GetAsync(id))!.Status);
    }

    [Fact]
    public async Task Printing_records_when_it_happened()
    {
        Artwork(1);
        var id = await NewSheetAsync();
        await Logic().AddItemsAsync(id, [Request()]);
        await Logic().MarkReadyAsync(id);
        await Logic().MarkPrintedAsync(id);

        var sheet = await Logic().GetAsync(id);

        Assert.Equal(new FixedStudioClock(Now).UtcNow, sheet!.PrintedAt);
    }

    [Fact]
    public async Task A_sheet_cannot_be_printed_without_being_marked_ready_first()
    {
        Artwork(1);
        var id = await NewSheetAsync();
        await Logic().AddItemsAsync(id, [Request()]);

        var result = await Logic().MarkPrintedAsync(id);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task A_printed_sheet_is_the_record_of_what_ran_and_is_not_deleted()
    {
        Artwork(1);
        var id = await NewSheetAsync();
        await Logic().AddItemsAsync(id, [Request()]);
        await Logic().MarkReadyAsync(id);
        await Logic().MarkPrintedAsync(id);

        var result = await Logic().DeleteAsync(id);

        Assert.False(result.Success);
        Assert.NotNull(await Logic().GetAsync(id));
    }

    // ---- What's waiting to be printed -----------------------------------

    [Fact]
    public async Task A_two_sided_design_offers_two_transfers()
    {
        // A front-and-back design is two pieces of film, and the cut list has to
        // say which is which.
        var front = Artwork(1);
        var back = Artwork(2);

        _sheets.CandidateLines.Add(OpenLine(front, back));

        var candidates = await Logic().GetCandidatesAsync();

        Assert.Equal(2, candidates.Count);
        Assert.Contains(candidates, c => c.Side == GangSheetSide.Front);
        Assert.Contains(candidates, c => c.Side == GangSheetSide.Back);
    }

    [Fact]
    public async Task A_side_with_no_artwork_on_it_offers_nothing()
    {
        var front = Artwork(1);

        _sheets.CandidateLines.Add(OpenLine(front, back: null));

        var candidates = await Logic().GetCandidatesAsync();

        Assert.Equal(GangSheetSide.Front, Assert.Single(candidates).Side);
    }

    [Fact]
    public async Task A_side_with_artwork_but_no_size_offers_nothing()
    {
        // A design saved before it was finished. Inventing a size for it would
        // put the wrong thing on film.
        var front = Artwork(1);
        var line = OpenLine(front, back: null);
        line.Design.FrontWidthMm = null;

        _sheets.CandidateLines.Add(line);

        Assert.Empty(await Logic().GetCandidatesAsync());
    }

    [Fact]
    public async Task A_line_already_on_a_sheet_is_still_offered_and_says_so()
    {
        // A reprint is a normal thing to want, so it is shown rather than
        // filtered out — quietly dropping it would look like the order had gone
        // missing.
        var front = Artwork(1);
        _sheets.CandidateLines.Add(OpenLine(front, back: null));

        var id = await NewSheetAsync();
        await Logic().AddItemsAsync(id, [Request(quantity: 2)]);

        var candidate = Assert.Single(await Logic().GetCandidatesAsync());

        Assert.Equal(2, candidate.AlreadyPlaced);
    }

    [Fact]
    public async Task A_line_on_a_completed_order_is_not_waiting_to_be_printed()
    {
        var front = Artwork(1);
        var line = OpenLine(front, back: null);
        line.Order.Status = OrderStatus.Completed;

        _sheets.CandidateLines.Add(line);

        Assert.Empty(await Logic().GetCandidatesAsync());
    }

    private static OrderLine OpenLine(Artwork front, Artwork? back)
    {
        var design = new Design
        {
            DesignId = 1,
            ProductId = 1,
            Name = "Sunshine Circles",
            FrontArtworkId = front.ArtworkId,
            FrontArtwork = front,
            FrontWidthMm = 200,
            FrontHeightMm = 250,
            BackArtworkId = back?.ArtworkId,
            BackArtwork = back,
            BackWidthMm = back == null ? null : 180,
            BackHeightMm = back == null ? null : 180
        };

        var order = new Order
        {
            OrderId = 1,
            CustomerId = 1,
            Status = OrderStatus.Received,
            DueOn = Now.Date.AddDays(3),
            Customer = new Customer { CustomerId = 1, FullName = "Ashley" }
        };

        return new OrderLine
        {
            OrderLineId = 1,
            OrderId = 1,
            Order = order,
            DesignId = 1,
            Design = design,
            SizeCode = "M",
            Quantity = 3,
            UnitPrice = 20m
        };
    }
}
