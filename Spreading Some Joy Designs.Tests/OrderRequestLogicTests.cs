using SpreadingJoy.Tests.Fakes;

namespace SpreadingJoy.Tests;

public class OrderRequestLogicTests
{
    private static readonly DateTime Now = new(2026, 8, 4, 10, 0, 0);
    private static readonly DateTime Due = new(2026, 8, 7);

    private readonly FakeOrderRequestRepository _requests = new();
    private readonly FakeCustomerRepository _customers = new();
    private readonly FakeDesignRepository _designs = new();
    private readonly FakeProductRepository _products = new();
    private readonly FakeArtworkRepository _artworks = new();
    private readonly FakeOrderRepository _orders = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FixedStudioClock _clock = new(Now);

    private Artwork _artwork = null!;

    private OrderRequestLogic Build(int capacity = 60)
    {
        var settings = new StudioSettings(capacity, 3, new[] { DayOfWeek.Saturday, DayOfWeek.Sunday });
        var designLogic = new DesignLogic(_designs, _products, _artworks, _clock);
        var orderLogic = new OrderLogic(_orders, _designs, _products, _customers, designLogic, settings, _clock);

        return new OrderRequestLogic(
            _requests, _customers, _designs, _products, designLogic, orderLogic, _unitOfWork, _clock);
    }

    private void SeedEverything(string artworkStatus = ArtworkStatus.Approved)
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

        _artwork = new Artwork
        {
            ArtworkId = 1,
            StoredFileName = "1.png",
            ContentType = "image/png",
            WidthPx = 3000,
            HeightPx = 3000,
            Sha256 = new string('a', 64),
            Status = artworkStatus,
            RejectionReason = artworkStatus == ArtworkStatus.Rejected ? "Not yours." : null,
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
            FrontArtwork = _artwork
        };

        _products.Seed(product);
        _artworks.Seed(_artwork);
        _designs.Seed(design);
    }

    private static SubmitOrderRequest Submission(
        int quantity = 1,
        string size = "M",
        bool rights = true,
        string? email = "sam@example.test") =>
        new(CustomerName: "Sam Ortiz",
            Email: email,
            Phone: "555 0100",
            DesignId: 1,
            SizeCode: size,
            Quantity: quantity,
            RequestedFor: Due,
            RightsAttested: rights,
            Notes: null);

    // ---- submitting ----

    [Fact]
    public async Task A_submission_creates_a_request_and_nothing_else()
    {
        SeedEverything();

        var result = await Build().SubmitAsync(Submission());

        Assert.True(result.Success);
        Assert.Single(_requests.All);

        // The point of the whole two-stage design: nothing a stranger typed
        // becomes a customer or an order until staff accept it.
        Assert.Empty(_customers.All);
        Assert.Empty(_orders.All);
    }

    [Fact]
    public async Task A_submission_without_the_rights_attestation_is_refused()
    {
        SeedEverything();

        var result = await Build().SubmitAsync(Submission(rights: false));

        Assert.False(result.Success);
        Assert.Empty(_requests.All);
    }

    [Fact]
    public async Task A_submission_without_a_phone_number_is_refused()
    {
        SeedEverything();

        var result = await Build().SubmitAsync(Submission() with { Phone = "  " });

        Assert.False(result.Success);
    }

    [Fact]
    public async Task A_date_the_studio_cannot_hit_is_still_accepted_as_a_request()
    {
        // Deliberate: a date the studio can't meet is a conversation, not a red
        // validation message on a public form. The rules bite on acceptance.
        SeedEverything();

        var result = await Build().SubmitAsync(Submission() with { RequestedFor = Now.Date.AddDays(1) });

        Assert.True(result.Success);
    }

    // ---- accepting ----

    [Fact]
    public async Task Accepting_creates_the_customer_and_the_order_together()
    {
        SeedEverything();
        var logic = Build();

        var submitted = await logic.SubmitAsync(Submission(quantity: 5));
        var accepted = await logic.AcceptAsync(submitted.OrderRequestId, handledByUserId: 9, Due);

        Assert.True(accepted.Success);

        var customer = Assert.Single(_customers.All);
        Assert.Equal("Sam Ortiz", customer.FullName);

        var order = Assert.Single(_orders.All);
        Assert.Equal(customer.CustomerId, order.CustomerId);
        Assert.Equal(5, order.GarmentCount);

        var request = Assert.Single(_requests.All);
        Assert.Equal(OrderRequestStatus.Accepted, request.Status);
        Assert.Equal(9, request.HandledByUserId);
        Assert.Equal(order.OrderId, request.OrderId);
    }

    [Fact]
    public async Task Accepting_attaches_the_anonymous_design_to_the_new_customer()
    {
        SeedEverything();
        var logic = Build();

        Assert.Null(_designs.All.Single().CustomerId);

        var submitted = await logic.SubmitAsync(Submission());
        await logic.AcceptAsync(submitted.OrderRequestId, 9, Due);

        var customer = Assert.Single(_customers.All);
        Assert.Equal(customer.CustomerId, _designs.All.Single().CustomerId);
    }

    [Fact]
    public async Task A_refused_acceptance_leaves_no_customer_behind()
    {
        // The reason the whole operation is wrapped in a unit of work: the
        // customer is created before the order is attempted, and a refusal
        // must not leave that customer on file.
        SeedEverything();
        var logic = Build(capacity: 10);

        var submitted = await logic.SubmitAsync(Submission(quantity: 50));
        var accepted = await logic.AcceptAsync(submitted.OrderRequestId, 9, Due);

        Assert.False(accepted.Success);
        Assert.Empty(_orders.All);

        // The fake unit of work reports what a real transaction would undo.
        Assert.True(_unitOfWork.RolledBack);

        var request = Assert.Single(_requests.All);
        Assert.Equal(OrderRequestStatus.Pending, request.Status);
    }

    [Fact]
    public async Task Artwork_rejected_while_the_request_waited_blocks_acceptance()
    {
        SeedEverything(artworkStatus: ArtworkStatus.Pending);
        var logic = Build();

        var submitted = await logic.SubmitAsync(Submission());

        _artwork.Status = ArtworkStatus.Rejected;
        _artwork.RejectionReason = "That's a copyrighted character.";

        var accepted = await logic.AcceptAsync(submitted.OrderRequestId, 9, Due);

        Assert.False(accepted.Success);
        Assert.Contains("copyrighted character", accepted.ErrorMessage);
        Assert.Empty(_orders.All);
        Assert.Empty(_customers.All);
    }

    [Fact]
    public async Task Artwork_still_pending_blocks_acceptance()
    {
        SeedEverything(artworkStatus: ArtworkStatus.Pending);
        var logic = Build();

        var submitted = await logic.SubmitAsync(Submission());
        var accepted = await logic.AcceptAsync(submitted.OrderRequestId, 9, Due);

        Assert.False(accepted.Success);
        Assert.Empty(_orders.All);
    }

    [Fact]
    public async Task A_returning_customer_is_matched_on_email_rather_than_duplicated()
    {
        SeedEverything();
        var logic = Build();

        var first = await logic.SubmitAsync(Submission());
        await logic.AcceptAsync(first.OrderRequestId, 9, Due);

        var second = await logic.SubmitAsync(Submission());
        await logic.AcceptAsync(second.OrderRequestId, 9, Due);

        Assert.Single(_customers.All);
        Assert.Equal(2, _orders.All.Count);
    }

    [Fact]
    public async Task Two_anonymous_submissions_with_no_email_stay_two_customers()
    {
        // No email means no way to tell two people apart. Merging on name alone
        // would be worse than duplicating.
        SeedEverything();
        var logic = Build();

        var first = await logic.SubmitAsync(Submission(email: null));
        await logic.AcceptAsync(first.OrderRequestId, 9, Due);

        var second = await logic.SubmitAsync(Submission(email: null));
        await logic.AcceptAsync(second.OrderRequestId, 9, Due);

        Assert.Equal(2, _customers.All.Count);
    }

    [Fact]
    public async Task A_request_cannot_be_handled_twice()
    {
        SeedEverything();
        var logic = Build();

        var submitted = await logic.SubmitAsync(Submission());
        await logic.AcceptAsync(submitted.OrderRequestId, 9, Due);

        var again = await logic.AcceptAsync(submitted.OrderRequestId, 9, Due);

        Assert.False(again.Success);
        Assert.Single(_orders.All);
    }

    [Fact]
    public async Task The_customers_own_attestation_carries_through_to_the_order()
    {
        SeedEverything();
        var logic = Build();

        var submitted = await logic.SubmitAsync(Submission());
        await logic.AcceptAsync(submitted.OrderRequestId, 9, Due);

        Assert.True(_orders.All.Single().RightsAttested);
    }

    // ---- declining ----

    [Fact]
    public async Task Declining_records_the_reason_and_who_did_it()
    {
        SeedEverything();
        var logic = Build();

        var submitted = await logic.SubmitAsync(Submission());
        var declined = await logic.DeclineAsync(submitted.OrderRequestId, 9, "We can't print that image.");

        Assert.True(declined.Success);

        var request = Assert.Single(_requests.All);
        Assert.Equal(OrderRequestStatus.Declined, request.Status);
        Assert.Equal("We can't print that image.", request.DeclineReason);
        Assert.Equal(9, request.HandledByUserId);
        Assert.Equal(Now, request.HandledAt);
    }

    [Fact]
    public async Task Declining_without_a_reason_is_refused()
    {
        SeedEverything();
        var logic = Build();

        var submitted = await logic.SubmitAsync(Submission());
        var declined = await logic.DeclineAsync(submitted.OrderRequestId, 9, "   ");

        Assert.False(declined.Success);
        Assert.Equal(OrderRequestStatus.Pending, _requests.All.Single().Status);
    }

    [Fact]
    public async Task An_already_declined_request_cannot_be_accepted()
    {
        SeedEverything();
        var logic = Build();

        var submitted = await logic.SubmitAsync(Submission());
        await logic.DeclineAsync(submitted.OrderRequestId, 9, "No.");

        var accepted = await logic.AcceptAsync(submitted.OrderRequestId, 9, Due);

        Assert.False(accepted.Success);
        Assert.Empty(_orders.All);
    }
}
