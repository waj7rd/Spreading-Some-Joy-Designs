using SpreadingJoy.Tests.Fakes;

namespace SpreadingJoy.Tests;

// Recording that something has actually gone.
//
// Two different answers to the same need, and the difference is deliberate. An
// order gets a Shipped *status*, because the production board is organised
// around the status chain and staff have to see at a glance which parcels are
// still on the bench. A gang sheet gets a dispatch *record*, because its chain
// describes the film — draft, ready, printed — and posting it is a fact about
// the envelope, not about the film.
public class DispatchTests
{
    private static readonly DateTime Now = new(2026, 8, 25, 14, 30, 0);

    private readonly FakeOrderRepository _orders = new();
    private readonly FakeDesignRepository _designs = new();
    private readonly FakeProductRepository _products = new();
    private readonly FakeCustomerRepository _customers = new();
    private readonly FakeArtworkRepository _artworks = new();
    private readonly FakeGangSheetRepository _sheets = new();

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

    private OrderLogic Logic() => new(
        _orders, _designs, _products, _customers,
        new DesignLogic(_designs, _products, _artworks, new FixedStudioClock(Now)),
        new StudioSettingsFromContext(new FakeStudioContext(_studio)),
        new FixedStudioClock(Now));

    private GangSheetLogic SheetLogic() =>
        new(_sheets, _artworks, new FixedStudioClock(Now));

    private Order Order(string method = FulfilmentMethod.Shipping, string status = OrderStatus.Printed)
    {
        var order = new Order
        {
            OrderId = 1,
            CustomerId = 1,
            Status = status,
            DueOn = Now.Date.AddDays(2),
            FulfilmentMethod = method,
            ShipToLine1 = method == FulfilmentMethod.Shipping ? "14 Elm Street" : null,
            ShipToCity = method == FulfilmentMethod.Shipping ? "Wright City" : null,
            ShipToState = method == FulfilmentMethod.Shipping ? "MO" : null,
            ShipToPostalCode = method == FulfilmentMethod.Shipping ? "63390" : null,
            ShippingFee = method == FulfilmentMethod.Shipping ? 6m : 0m,
            RightsAttested = true,
            CreatedAt = Now
        };

        _orders.Seed(order);
        return order;
    }

    // ---- Orders ---------------------------------------------------------

    [Fact]
    public async Task Posting_an_order_records_when_it_went()
    {
        var order = Order();

        var result = await Logic().MarkShippedAsync(1, "USPS", "9400111899223");

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(OrderStatus.Shipped, order.Status);
        Assert.Equal(new FixedStudioClock(Now).UtcNow, order.DispatchedAt);
        Assert.Equal("USPS", order.Carrier);
        Assert.Equal("9400111899223", order.TrackingNumber);
    }

    [Fact]
    public async Task A_dispatch_with_no_reference_number_is_still_a_dispatch()
    {
        // A parcel handed to a courier at the counter has neither. Refusing to
        // record it would mean the studio stops recording dispatches at all.
        var order = Order();

        Assert.True((await Logic().MarkShippedAsync(1, null, null)).Success);

        Assert.True(order.HasBeenDispatched);
        Assert.Null(order.Carrier);
        Assert.Null(order.TrackingNumber);
    }

    [Fact]
    public async Task Blank_carrier_and_tracking_are_stored_as_nothing_rather_than_as_spaces()
    {
        var order = Order();

        await Logic().MarkShippedAsync(1, "   ", "  ");

        Assert.Null(order.Carrier);
        Assert.Null(order.TrackingNumber);
    }

    [Fact]
    public async Task A_collection_order_cannot_be_posted()
    {
        var order = Order(method: FulfilmentMethod.Pickup);

        var result = await Logic().MarkShippedAsync(1, null, null);

        Assert.False(result.Success);
        Assert.Contains("collection order", result.ErrorMessage!);
        Assert.Equal(OrderStatus.Printed, order.Status);
    }

    [Fact]
    public async Task A_postal_order_with_no_address_is_not_dispatched_to_nowhere()
    {
        var order = Order();
        order.ShipToLine1 = null;

        var result = await Logic().MarkShippedAsync(1, null, null);

        Assert.False(result.Success);
        Assert.Contains("no address", result.ErrorMessage!);
    }

    [Fact]
    public async Task Shipped_cannot_be_set_as_a_plain_status_change()
    {
        // It carries a dispatch record, so it goes through its own method — the
        // same arrangement Cancelled uses for its reason. Reachable as a plain
        // status change, it would produce orders marked as posted with nothing
        // saying when.
        var order = Order();

        var result = await Logic().SetStatusAsync(1, OrderStatus.Shipped);

        Assert.False(result.Success);
        Assert.Contains("Mark shipped", result.ErrorMessage!);
        Assert.Null(order.DispatchedAt);
    }

    [Fact]
    public async Task A_postal_order_is_never_ready_for_pickup()
    {
        // It is not waiting on a counter and never will be. Marking it so is how
        // a customer gets told to come and collect a parcel.
        var order = Order();

        var result = await Logic().SetStatusAsync(1, OrderStatus.ReadyForPickup);

        Assert.False(result.Success);
        Assert.Contains("mark it shipped instead", result.ErrorMessage!);
        Assert.Equal(OrderStatus.Printed, order.Status);
    }

    [Fact]
    public async Task A_collection_order_is_still_ready_for_pickup_as_it_always_was()
    {
        var order = Order(method: FulfilmentMethod.Pickup);

        Assert.True((await Logic().SetStatusAsync(1, OrderStatus.ReadyForPickup)).Success);
        Assert.Equal(OrderStatus.ReadyForPickup, order.Status);
    }

    [Fact]
    public async Task Moving_an_order_back_off_shipped_takes_the_tracking_number_with_it()
    {
        // A tracking number on an order nobody has posted is worse than none,
        // because somebody will read it out to a customer.
        var order = Order();
        await Logic().MarkShippedAsync(1, "USPS", "9400111899223");

        Assert.True((await Logic().SetStatusAsync(1, OrderStatus.Printed)).Success);

        Assert.False(order.HasBeenDispatched);
        Assert.Null(order.Carrier);
        Assert.Null(order.TrackingNumber);
    }

    [Fact]
    public async Task Completing_a_posted_order_keeps_the_dispatch_record()
    {
        // That parcel really did leave. The tracking number is the history of
        // the job, and the one case where moving off Shipped keeps it.
        var order = Order();
        await Logic().MarkShippedAsync(1, "USPS", "9400111899223");

        Assert.True((await Logic().SetStatusAsync(1, OrderStatus.Completed)).Success);

        Assert.True(order.HasBeenDispatched);
        Assert.Equal("9400111899223", order.TrackingNumber);
    }

    [Fact]
    public async Task A_cancelled_order_is_not_posted()
    {
        var order = Order(status: OrderStatus.Cancelled);

        Assert.False((await Logic().MarkShippedAsync(1, null, null)).Success);
        Assert.Null(order.DispatchedAt);
    }

    [Fact]
    public void A_shipped_order_still_occupies_press_capacity()
    {
        // The shirts have been through the press but the job isn't closed.
        // Dropping it out of Open would hand that day's capacity back the moment
        // a parcel was posted.
        Assert.Contains(OrderStatus.Shipped, OrderStatus.Open);
        Assert.True(OrderStatus.IsOpen(OrderStatus.Shipped));
    }

    [Fact]
    public void Each_fulfilment_method_has_exactly_one_finishing_status()
    {
        Assert.Equal(OrderStatus.Shipped, OrderStatus.FinishingStatusFor(FulfilmentMethod.Shipping));
        Assert.Equal(OrderStatus.ReadyForPickup, OrderStatus.FinishingStatusFor(FulfilmentMethod.Pickup));
    }

    // ---- Gang sheets ----------------------------------------------------

    private GangSheet CustomerSheet(
        string method = FulfilmentMethod.Shipping,
        string status = GangSheetStatus.Printed)
    {
        var sheet = new GangSheet
        {
            GangSheetId = 1,
            Name = "Dana — 22 x 24 in",
            WidthMm = 559,
            MaxLengthMm = 610,
            GutterMm = 6,
            MarginMm = 6,
            Status = status,
            Origin = GangSheetOrigin.Customer,
            CustomerId = 1,
            Price = 20m,
            FulfilmentMethod = method,
            ShipToLine1 = method == FulfilmentMethod.Shipping ? "14 Elm Street" : null,
            ShippingFee = method == FulfilmentMethod.Shipping ? 6m : 0m,
            CreatedAt = Now
        };

        _sheets.Seed(sheet);
        return sheet;
    }

    [Fact]
    public async Task Posting_a_sheet_records_it_without_touching_the_status()
    {
        // The sheet stays Printed, because that is still true of the film.
        var sheet = CustomerSheet();

        var result = await SheetLogic().MarkDispatchedAsync(1, "USPS", "9400111899223");

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(GangSheetStatus.Printed, sheet.Status);
        Assert.True(sheet.HasBeenDispatched);
        Assert.Equal("9400111899223", sheet.TrackingNumber);
    }

    [Fact]
    public async Task A_sheet_has_to_be_printed_before_it_can_be_posted()
    {
        // Dispatching a draft would record that an envelope went out containing
        // film that doesn't exist yet.
        var sheet = CustomerSheet(status: GangSheetStatus.Draft);

        var result = await SheetLogic().MarkDispatchedAsync(1, null, null);

        Assert.False(result.Success);
        Assert.Contains("Print the sheet", result.ErrorMessage!);
        Assert.False(sheet.HasBeenDispatched);
    }

    [Fact]
    public async Task A_studio_sheet_is_not_posted_to_anybody()
    {
        var sheet = CustomerSheet();
        sheet.Origin = GangSheetOrigin.Studio;

        var result = await SheetLogic().MarkDispatchedAsync(1, null, null);

        Assert.False(result.Success);
        Assert.Contains("studio sheet", result.ErrorMessage!);
    }

    [Fact]
    public async Task A_sheet_the_customer_is_collecting_is_not_posted()
    {
        CustomerSheet(method: FulfilmentMethod.Pickup);

        var result = await SheetLogic().MarkDispatchedAsync(1, null, null);

        Assert.False(result.Success);
        Assert.Contains("collected from the studio", result.ErrorMessage!);
    }

    [Fact]
    public async Task A_sheet_is_not_posted_twice()
    {
        CustomerSheet();
        Assert.True((await SheetLogic().MarkDispatchedAsync(1, "USPS", "A")).Success);

        var again = await SheetLogic().MarkDispatchedAsync(1, "UPS", "B");

        Assert.False(again.Success);
        Assert.Contains("already been posted", again.ErrorMessage!);
    }

    [Fact]
    public async Task A_printed_postal_sheet_is_what_the_studio_still_owes()
    {
        var sheet = CustomerSheet();
        Assert.True(sheet.AwaitingDispatch);

        await SheetLogic().MarkDispatchedAsync(1, null, null);

        Assert.False(sheet.AwaitingDispatch);
    }
}
