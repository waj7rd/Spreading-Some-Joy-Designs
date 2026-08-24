using SpreadingJoy.Tests.Fakes;

namespace SpreadingJoy.Tests;

public class OrderLogicTests
{
    // Tuesday 4 August 2026, so a 3-day turnaround lands on Friday the 7th.
    private static readonly DateTime Now = new(2026, 8, 4, 10, 0, 0);
    private static readonly DateTime Due = new(2026, 8, 7);

    private readonly FakeOrderRepository _orders = new();
    private readonly FakeDesignRepository _designs = new();
    private readonly FakeProductRepository _products = new();
    private readonly FakeCustomerRepository _customers = new();
    private readonly FakeArtworkRepository _artworks = new();
    private readonly FixedStudioClock _clock = new(Now);

    private OrderLogic Build(
        int capacity = 60, bool offersShipping = false, decimal shippingFee = 0m)
    {
        var settings = new StudioSettings(
            capacity, 3, new[] { DayOfWeek.Saturday, DayOfWeek.Sunday }, offersShipping, shippingFee);
        var designLogic = new DesignLogic(_designs, _products, _artworks, _clock);

        return new OrderLogic(_orders, _designs, _products, _customers, designLogic, settings, _clock);
    }

    private void SeedEverything(int printedSides = 1)
    {
        var product = new Product
        {
            ProductId = 1,
            Name = "Heavy Cotton Tee",
            Colour = "Black",
            ColourHex = "#1a1a1a",
            BasePrice = 13m,
            PrintSidePrice = 7m,
            PrintAreaWidthMm = 305,
            PrintAreaHeightMm = 406,
            SizesRaw = "S,M,L,XL,2XL",
            ExtendedSizeUpcharge = 3m,
            IsActive = true
        };

        var front = new Artwork
        {
            ArtworkId = 1,
            StoredFileName = "1.png",
            ContentType = "image/png",
            WidthPx = 3000,
            HeightPx = 3000,
            Sha256 = new string('a', 64),
            Status = ArtworkStatus.Approved,
            CreatedAt = Now
        };

        var back = new Artwork
        {
            ArtworkId = 2,
            StoredFileName = "2.png",
            ContentType = "image/png",
            WidthPx = 3000,
            HeightPx = 3000,
            Sha256 = new string('b', 64),
            Status = ArtworkStatus.Approved,
            CreatedAt = Now
        };

        var design = new Design
        {
            DesignId = 1,
            ProductId = 1,
            Name = "My design",
            IsActive = true,
            CreatedAt = Now,
            Product = product,
            FrontArtworkId = 1,
            FrontXMm = 0,
            FrontYMm = 0,
            FrontWidthMm = 200,
            FrontHeightMm = 200,
            FrontArtwork = front
        };

        if (printedSides == 2)
        {
            design.BackArtworkId = 2;
            design.BackXMm = 0;
            design.BackYMm = 0;
            design.BackWidthMm = 200;
            design.BackHeightMm = 200;
            design.BackArtwork = back;
        }

        _products.Seed(product);
        _artworks.Seed(front, back);
        _designs.Seed(design);
        _customers.Seed(new Customer { CustomerId = 1, FullName = "Sam Ortiz", IsActive = true, CreatedAt = Now });
    }

    private static PlaceOrderRequest Request(
        int quantity = 1,
        string size = "M",
        bool rights = true,
        DateTime? dueOn = null,
        string method = FulfilmentMethod.Pickup,
        ShippingAddress? shipTo = null) =>
        new(CustomerId: 1,
            DueOn: dueOn ?? Due,
            Lines: [new OrderLineRequest(1, size, quantity)],
            RightsAttested: rights,
            Notes: null,
            FulfilmentMethod: method,
            ShipTo: shipTo);

    // ---- the rights gate ----

    [Fact]
    public async Task An_order_without_the_rights_attestation_is_refused()
    {
        SeedEverything();

        var result = await Build().PlaceAsync(Request(rights: false));

        Assert.False(result.Success);
        Assert.Empty(_orders.All);
    }

    [Fact]
    public async Task Attesting_records_when_it_happened()
    {
        SeedEverything();

        var result = await Build().PlaceAsync(Request());

        Assert.True(result.Success);

        var order = Assert.Single(_orders.All);
        Assert.True(order.RightsAttested);
        Assert.Equal(Now, order.RightsAttestedAt);
    }

    // ---- pricing ----

    [Fact]
    public async Task Unit_price_is_the_blank_plus_one_printed_side()
    {
        SeedEverything(printedSides: 1);

        await Build().PlaceAsync(Request(size: "M"));

        var line = Assert.Single(_orders.All.Single().OrderLines);
        Assert.Equal(20m, line.UnitPrice);
    }

    [Fact]
    public async Task A_second_printed_side_is_charged_again()
    {
        SeedEverything(printedSides: 2);

        await Build().PlaceAsync(Request(size: "M"));

        var line = Assert.Single(_orders.All.Single().OrderLines);
        Assert.Equal(27m, line.UnitPrice);
    }

    [Fact]
    public async Task Extended_sizes_carry_the_upcharge()
    {
        SeedEverything(printedSides: 1);

        await Build().PlaceAsync(Request(size: "2XL"));

        var line = Assert.Single(_orders.All.Single().OrderLines);
        Assert.Equal(23m, line.UnitPrice);
    }

    [Fact]
    public async Task The_price_is_snapshotted_and_survives_a_catalogue_change()
    {
        SeedEverything();

        await Build().PlaceAsync(Request(quantity: 10));

        var order = _orders.All.Single();
        var priceAtOrderTime = order.OrderLines.Single().UnitPrice;

        // The studio puts its prices up the next morning.
        _products.All.Single().BasePrice = 25m;
        _products.All.Single().PrintSidePrice = 12m;

        Assert.Equal(priceAtOrderTime, order.OrderLines.Single().UnitPrice);
        Assert.Equal(priceAtOrderTime * 10, order.Total);
    }

    // ---- capacity ----

    [Fact]
    public async Task An_order_beyond_the_days_capacity_is_refused()
    {
        SeedEverything();

        var result = await Build(capacity: 50).PlaceAsync(Request(quantity: 51));

        Assert.False(result.Success);
        Assert.Contains("50 more garments", result.ErrorMessage);
        Assert.Empty(_orders.All);
    }

    [Fact]
    public async Task Capacity_counts_garments_already_promised_that_day()
    {
        SeedEverything();
        var logic = Build(capacity: 50);

        var first = await logic.PlaceAsync(Request(quantity: 30));
        Assert.True(first.Success);

        var second = await logic.PlaceAsync(Request(quantity: 25));
        Assert.False(second.Success);

        var third = await logic.PlaceAsync(Request(quantity: 20));
        Assert.True(third.Success);
    }

    [Fact]
    public async Task A_cancelled_order_gives_its_capacity_back()
    {
        SeedEverything();
        var logic = Build(capacity: 50);

        var first = await logic.PlaceAsync(Request(quantity: 50));
        Assert.True(first.Success);

        var blocked = await logic.PlaceAsync(Request(quantity: 10));
        Assert.False(blocked.Success);

        await logic.CancelAsync(first.OrderId, "Customer changed their mind.");

        var afterCancel = await logic.PlaceAsync(Request(quantity: 10));
        Assert.True(afterCancel.Success);
    }

    [Fact]
    public async Task Capacity_is_measured_per_day_not_in_total()
    {
        SeedEverything();
        var logic = Build(capacity: 50);

        Assert.True((await logic.PlaceAsync(Request(quantity: 50, dueOn: Due))).Success);
        Assert.True((await logic.PlaceAsync(Request(quantity: 50, dueOn: Due.AddDays(3)))).Success);
    }

    [Fact]
    public async Task Reports_remaining_capacity_for_a_day()
    {
        SeedEverything();
        var logic = Build(capacity: 60);

        await logic.PlaceAsync(Request(quantity: 20));

        var capacity = await logic.GetCapacityAsync(Due);

        Assert.Equal(20, capacity.Promised);
        Assert.Equal(40, capacity.Remaining);
        Assert.False(capacity.IsFull);
    }

    // ---- dates and sizes ----

    [Fact]
    public async Task A_due_date_inside_the_turnaround_window_is_refused()
    {
        SeedEverything();

        var result = await Build().PlaceAsync(Request(dueOn: Now.Date.AddDays(1)));

        Assert.False(result.Success);
        Assert.Empty(_orders.All);
    }

    [Fact]
    public async Task A_due_date_on_a_closed_day_is_refused()
    {
        SeedEverything();

        // Saturday 15 August.
        var result = await Build().PlaceAsync(Request(dueOn: new DateTime(2026, 8, 15)));

        Assert.False(result.Success);
    }

    [Fact]
    public async Task A_size_the_garment_does_not_come_in_is_refused()
    {
        SeedEverything();

        var result = await Build().PlaceAsync(Request(size: "4XL"));

        Assert.False(result.Success);
        Assert.Contains("doesn't come in 4XL", result.ErrorMessage);
    }

    [Fact]
    public async Task Size_codes_are_matched_case_insensitively()
    {
        SeedEverything();

        var result = await Build().PlaceAsync(Request(size: "xl"));

        Assert.True(result.Success);
        Assert.Equal("XL", _orders.All.Single().OrderLines.Single().SizeCode);
    }

    [Fact]
    public async Task A_zero_quantity_line_is_refused()
    {
        SeedEverything();

        var result = await Build().PlaceAsync(Request(quantity: 0));

        Assert.False(result.Success);
    }

    [Fact]
    public async Task An_empty_order_is_refused()
    {
        SeedEverything();

        var result = await Build().PlaceAsync(new PlaceOrderRequest(1, Due, [], true, null));

        Assert.False(result.Success);
    }

    // ---- statuses ----

    [Fact]
    public async Task Completing_an_order_stamps_the_time()
    {
        SeedEverything();
        var logic = Build();

        var placed = await logic.PlaceAsync(Request());
        await logic.SetStatusAsync(placed.OrderId, OrderStatus.Completed);

        var order = _orders.All.Single();
        Assert.Equal(OrderStatus.Completed, order.Status);
        Assert.Equal(Now, order.CompletedAt);
    }

    [Fact]
    public async Task Cancelling_through_SetStatus_is_refused_so_the_reason_is_recorded()
    {
        SeedEverything();
        var logic = Build();

        var placed = await logic.PlaceAsync(Request());
        var result = await logic.SetStatusAsync(placed.OrderId, OrderStatus.Cancelled);

        Assert.False(result.Success);
        Assert.Equal(OrderStatus.Received, _orders.All.Single().Status);
    }

    [Fact]
    public async Task Cancelling_without_a_reason_is_refused()
    {
        SeedEverything();
        var logic = Build();

        var placed = await logic.PlaceAsync(Request());
        var result = await logic.CancelAsync(placed.OrderId, "  ");

        Assert.False(result.Success);
    }

    [Fact]
    public async Task A_finished_order_cannot_be_cancelled()
    {
        SeedEverything();
        var logic = Build();

        var placed = await logic.PlaceAsync(Request());
        await logic.SetStatusAsync(placed.OrderId, OrderStatus.Completed);

        var result = await logic.CancelAsync(placed.OrderId, "Too late.");

        Assert.False(result.Success);
    }

    [Fact]
    public async Task An_unknown_status_is_refused()
    {
        // "Shipped" on purpose: the studio posts orders out, but that is a
        // fulfilment method rather than a stage on the board. Nothing moves an
        // order into a status the chain does not have.
        SeedEverything();
        var logic = Build();

        var placed = await logic.PlaceAsync(Request());
        var result = await logic.SetStatusAsync(placed.OrderId, "Shipped");

        Assert.False(result.Success);
    }

    [Fact]
    public async Task An_order_for_an_archived_customer_is_refused()
    {
        SeedEverything();
        _customers.All.Single().IsActive = false;

        var result = await Build().PlaceAsync(Request());

        Assert.False(result.Success);
    }

    // ---- postage ----

    private static readonly ShippingAddress Address =
        new("14 Sycamore Street", null, "Sedalia", "MO", "65301");

    [Fact]
    public async Task Postage_is_added_to_the_total_without_touching_the_garments()
    {
        SeedEverything();

        await Build(offersShipping: true, shippingFee: 8m)
            .PlaceAsync(Request(quantity: 2, method: FulfilmentMethod.Shipping, shipTo: Address));

        var order = _orders.All.Single();

        // Subtotal is still purely the snapshotted lines. Postage sits beside it
        // rather than inside it, which is what keeps the garment count and the
        // press capacity untouched by a delivery charge.
        Assert.Equal(order.OrderLines.Sum(l => l.LineTotal), order.Subtotal);
        Assert.Equal(8m, order.ShippingFee);
        Assert.Equal(order.Subtotal + 8m, order.Total);
        Assert.Equal(2, order.GarmentCount);
    }

    [Fact]
    public async Task A_collection_order_pays_no_postage_even_when_the_studio_charges_for_it()
    {
        SeedEverything();

        await Build(offersShipping: true, shippingFee: 8m).PlaceAsync(Request());

        var order = _orders.All.Single();

        Assert.Equal(0m, order.ShippingFee);
        Assert.Equal(order.Subtotal, order.Total);
    }

    [Fact]
    public async Task The_postage_charged_is_snapshotted_rather_than_looked_up_later()
    {
        // The same rule the line prices follow. A studio putting its postage up
        // must not change what a customer already agreed to pay — and reading
        // the fee live would do exactly that, silently, on every past order.
        SeedEverything();

        await Build(offersShipping: true, shippingFee: 8m)
            .PlaceAsync(Request(method: FulfilmentMethod.Shipping, shipTo: Address));

        var order = _orders.All.Single();
        var totalWhenPlaced = order.Total;

        // The studio doubles its postage the following week.
        _ = Build(offersShipping: true, shippingFee: 16m);

        Assert.Equal(8m, order.ShippingFee);
        Assert.Equal(totalWhenPlaced, order.Total);
    }

    [Fact]
    public async Task An_order_asking_to_be_posted_by_a_studio_that_does_not_ship_is_refused()
    {
        SeedEverything();

        var result = await Build(offersShipping: false)
            .PlaceAsync(Request(method: FulfilmentMethod.Shipping, shipTo: Address));

        Assert.False(result.Success);
        Assert.Empty(_orders.All);
    }

    [Fact]
    public async Task An_order_asking_to_be_posted_with_no_address_is_refused()
    {
        SeedEverything();

        var result = await Build(offersShipping: true, shippingFee: 8m)
            .PlaceAsync(Request(method: FulfilmentMethod.Shipping, shipTo: null));

        Assert.False(result.Success);
        Assert.Empty(_orders.All);
    }

    [Fact]
    public async Task Postage_does_not_compete_for_press_capacity()
    {
        // Capacity counts garments. If postage had been modelled as an order
        // line it would arrive here as a quantity and quietly eat a slot.
        SeedEverything();

        await Build(capacity: 10, offersShipping: true, shippingFee: 8m)
            .PlaceAsync(Request(quantity: 10, method: FulfilmentMethod.Shipping, shipTo: Address));

        var capacity = await Build(capacity: 10, offersShipping: true).GetCapacityAsync(Due);

        Assert.Equal(10, capacity.Promised);
        Assert.True(capacity.IsFull);
    }
}
