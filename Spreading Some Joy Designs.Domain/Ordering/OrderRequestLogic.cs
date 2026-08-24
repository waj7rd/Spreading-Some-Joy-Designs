using SpreadingJoy.Domain.EntityModels;
using SpreadingJoy.Domain.IRepositories;
using SpreadingJoy.Domain.Shared;

namespace SpreadingJoy.Domain.Ordering;

public class OrderRequestLogic : IOrderRequestLogic
{
    private const int MaxQuantity = 500;

    private readonly IOrderRequestRepository _requestRepository;
    private readonly ICustomerRepository _customerRepository;
    private readonly IDesignRepository _designRepository;
    private readonly IProductRepository _productRepository;
    private readonly IDesignLogic _designLogic;
    private readonly IOrderLogic _orderLogic;
    private readonly IStudioSettings _settings;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IStudioClock _clock;

    public OrderRequestLogic(
        IOrderRequestRepository requestRepository,
        ICustomerRepository customerRepository,
        IDesignRepository designRepository,
        IProductRepository productRepository,
        IDesignLogic designLogic,
        IOrderLogic orderLogic,
        IStudioSettings settings,
        IUnitOfWork unitOfWork,
        IStudioClock clock)
    {
        _requestRepository = requestRepository;
        _customerRepository = customerRepository;
        _designRepository = designRepository;
        _productRepository = productRepository;
        _designLogic = designLogic;
        _orderLogic = orderLogic;
        _settings = settings;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<OrderRequestResult> SubmitAsync(SubmitOrderRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CustomerName))
            return OrderRequestResult.Fail("Tell us your name.");

        if (string.IsNullOrWhiteSpace(request.Phone))
            return OrderRequestResult.Fail("Leave a phone number so we can reach you.");

        if (request.Quantity < 1 || request.Quantity > MaxQuantity)
            return OrderRequestResult.Fail($"Quantity has to be between 1 and {MaxQuantity}.");

        if (!request.RightsAttested)
            return OrderRequestResult.Fail(
                "We need you to confirm you have the right to use this artwork before we can print it.");

        var design = await _designRepository.GetAsync(d => d.DesignId == request.DesignId);
        if (design == null || !design.IsActive)
            return OrderRequestResult.Fail("That design couldn't be found.");

        var product = await _productRepository.GetAsync(p => p.ProductId == design.ProductId);
        if (product == null || !product.IsActive)
            return OrderRequestResult.Fail("That garment is no longer available.");

        var sizeCode = request.SizeCode?.Trim().ToUpperInvariant() ?? string.Empty;
        if (!product.Sizes.Contains(sizeCode))
            return OrderRequestResult.Fail($"The {product.Colour} {product.Name} doesn't come in {sizeCode}.");

        // The studio switch is re-read here rather than trusted from the form. The
        // ship-or-collect choice is only rendered when shipping is on, but a form
        // is a suggestion and this is the rule.
        var method = FulfilmentMethod.Normalise(request.FulfilmentMethod);
        var requested = request.ShipTo ?? ShippingAddress.None;

        var fulfilmentError = Fulfilment.Check(method, requested, _settings.OffersShipping);
        if (fulfilmentError != null)
            return OrderRequestResult.Fail(fulfilmentError);

        var shipTo = Fulfilment.ToStore(method, requested);

        // Deliberately not checked here: the due date and the day's capacity.
        // A date the studio can't hit is something to ring the customer about,
        // not a red validation message on a public form — and holding capacity
        // for a request nobody has accepted would let anyone fill the calendar.
        var orderRequest = new OrderRequest
        {
            CustomerName = request.CustomerName.Trim(),
            Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim(),
            Phone = request.Phone.Trim(),
            DesignId = design.DesignId,
            SizeCode = sizeCode,
            Quantity = request.Quantity,
            RequestedFor = request.RequestedFor.Date,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            FulfilmentMethod = method,
            ShipToLine1 = shipTo.Line1,
            ShipToLine2 = shipTo.Line2,
            ShipToCity = shipTo.City,
            ShipToState = shipTo.State,
            ShipToPostalCode = shipTo.PostalCode,
            RightsAttested = true,
            Status = OrderRequestStatus.Pending,
            CreatedAt = _clock.UtcNow
        };

        await _requestRepository.AddAsync(orderRequest);
        await _requestRepository.SaveChangesAsync();

        return OrderRequestResult.Ok(orderRequest.OrderRequestId);
    }

    public async Task<IList<OrderRequest>> GetPendingAsync() =>
        await _requestRepository.GetByStatusAsync(OrderRequestStatus.Pending);

    public async Task<OrderRequest?> GetByIdAsync(int orderRequestId) =>
        await _requestRepository.GetWithDesignAsync(orderRequestId);

    public async Task<OrderRequestResult> AcceptAsync(int orderRequestId, int handledByUserId, DateTime dueOn)
    {
        // One transaction around the whole thing. Accepting creates a customer,
        // re-parents the design onto them, and places the order; if the order is
        // refused — the day filled up while the request sat in the queue, the
        // artwork got rejected — none of the earlier writes may survive.
        return await _unitOfWork.ExecuteAsync(async () =>
        {
            var request = await _requestRepository.GetAsync(r => r.OrderRequestId == orderRequestId);
            if (request == null)
                return OrderRequestResult.Fail("Request not found.");

            if (request.Status != OrderRequestStatus.Pending)
                return OrderRequestResult.Fail("That request has already been handled.");

            var designCheck = await _designLogic.ValidateForOrderAsync(request.DesignId);
            if (!designCheck.Success)
                return OrderRequestResult.Fail(designCheck.ErrorMessage!);

            var customer = await FindOrCreateCustomerAsync(request);

            // The design was made by an anonymous visitor and has no owner until
            // now. Attaching it here — rather than at submission — is what keeps
            // unaccepted requests from littering the customer's design list.
            var design = await _designRepository.GetAsync(d => d.DesignId == request.DesignId);
            if (design != null && design.CustomerId == null)
            {
                design.CustomerId = customer.CustomerId;
                await _designRepository.SaveChangesAsync();
            }

            var placed = await _orderLogic.PlaceAsync(new PlaceOrderRequest(
                CustomerId: customer.CustomerId,
                DueOn: dueOn.Date,
                Lines: [new OrderLineRequest(request.DesignId, request.SizeCode, request.Quantity)],

                // Carried across from what the customer actually agreed to on
                // the public form. Not re-asserted by staff on their behalf.
                RightsAttested: request.RightsAttested,
                Notes: request.Notes,

                // Carried across exactly as stored. Accepting a request is agreeing to
                // what the customer asked for, including how they asked to get it.
                FulfilmentMethod: request.FulfilmentMethod,
                ShipTo: new ShippingAddress(
                    request.ShipToLine1,
                    request.ShipToLine2,
                    request.ShipToCity,
                    request.ShipToState,
                    request.ShipToPostalCode)));

            if (!placed.Success)
                return OrderRequestResult.Fail(placed.ErrorMessage!);

            request.Status = OrderRequestStatus.Accepted;
            request.HandledByUserId = handledByUserId;
            request.HandledAt = _clock.UtcNow;
            request.OrderId = placed.OrderId;

            await _requestRepository.SaveChangesAsync();

            return OrderRequestResult.Ok(request.OrderRequestId, placed.OrderId);
        });
    }

    public async Task<OrderRequestResult> DeclineAsync(int orderRequestId, int handledByUserId, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return OrderRequestResult.Fail("Say why it's being declined — the customer sees this.");

        var request = await _requestRepository.GetAsync(r => r.OrderRequestId == orderRequestId);
        if (request == null)
            return OrderRequestResult.Fail("Request not found.");

        if (request.Status != OrderRequestStatus.Pending)
            return OrderRequestResult.Fail("That request has already been handled.");

        request.Status = OrderRequestStatus.Declined;
        request.DeclineReason = reason.Trim();
        request.HandledByUserId = handledByUserId;
        request.HandledAt = _clock.UtcNow;

        await _requestRepository.SaveChangesAsync();
        return OrderRequestResult.Ok(request.OrderRequestId);
    }

    // Matches on email when there is one, because a repeat customer ordering a
    // second batch shouldn't become a second record. No email means no way to
    // tell two people apart — two Sarah Joneses with no address are two
    // customers, and merging them on name alone would be worse than duplicating.
    private async Task<Customer> FindOrCreateCustomerAsync(OrderRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            var existing = await _customerRepository.GetAsync(c => c.Email == request.Email);
            if (existing != null)
                return existing;
        }

        var customer = new Customer
        {
            FullName = request.CustomerName,
            Email = request.Email,
            Phone = request.Phone,
            IsActive = true,
            CreatedAt = _clock.UtcNow
        };

        await _customerRepository.AddAsync(customer);
        await _customerRepository.SaveChangesAsync();

        return customer;
    }
}
