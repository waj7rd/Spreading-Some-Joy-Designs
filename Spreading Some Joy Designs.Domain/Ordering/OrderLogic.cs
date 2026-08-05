using SpreadingJoy.Domain.EntityModels;
using SpreadingJoy.Domain.IRepositories;
using SpreadingJoy.Domain.Shared;

namespace SpreadingJoy.Domain.Ordering;

public class OrderLogic : IOrderLogic
{
    // One shirt is an order; a thousand on one date is a conversation, not a
    // web form. The ceiling is per line so a single typed quantity can't blow
    // out the day on its own.
    private const int MaxQuantityPerLine = 500;

    private readonly IOrderRepository _orderRepository;
    private readonly IDesignRepository _designRepository;
    private readonly IProductRepository _productRepository;
    private readonly ICustomerRepository _customerRepository;
    private readonly IDesignLogic _designLogic;
    private readonly IStudioSettings _settings;
    private readonly IStudioClock _clock;

    public OrderLogic(
        IOrderRepository orderRepository,
        IDesignRepository designRepository,
        IProductRepository productRepository,
        ICustomerRepository customerRepository,
        IDesignLogic designLogic,
        IStudioSettings settings,
        IStudioClock clock)
    {
        _orderRepository = orderRepository;
        _designRepository = designRepository;
        _productRepository = productRepository;
        _customerRepository = customerRepository;
        _designLogic = designLogic;
        _settings = settings;
        _clock = clock;
    }

    public async Task<Order?> GetByIdAsync(int orderId) =>
        await _orderRepository.GetWithLinesAsync(orderId);

    public async Task<IList<Order>> GetOpenAsync() =>
        await _orderRepository.GetOpenAsync();

    public async Task<IList<Order>> GetForCustomerAsync(int customerId)
    {
        var orders = await _orderRepository.FindByAsync(o => o.CustomerId == customerId);
        return orders.OrderByDescending(o => o.CreatedAt).ToList();
    }

    public async Task<OrderResult> PlaceAsync(PlaceOrderRequest request)
    {
        if (request.Lines.Count == 0)
            return OrderResult.Fail("There's nothing on this order.");

        // The attestation is a gate, not a checkbox to record. An order without
        // it is refused, because the whole point is that it exists before the
        // studio prints somebody else's picture.
        if (!request.RightsAttested)
            return OrderResult.Fail(
                "We need you to confirm you have the right to use this artwork before we can print it.");

        var customer = await _customerRepository.GetAsync(c => c.CustomerId == request.CustomerId);
        if (customer == null)
            return OrderResult.Fail("Customer not found.");

        if (!customer.IsActive)
            return OrderResult.Fail("That customer record has been archived.");

        var dueOn = request.DueOn.Date;

        var dateError = StudioCalendar.CheckDueDate(_settings, dueOn, _clock.Today);
        if (dateError != null)
            return OrderResult.Fail(dateError);

        // Build the lines before checking capacity — the capacity question is
        // "how many garments", which isn't known until every line has been
        // validated and counted.
        var lines = new List<OrderLine>();

        foreach (var requested in request.Lines)
        {
            if (requested.Quantity < 1 || requested.Quantity > MaxQuantityPerLine)
                return OrderResult.Fail($"Quantity has to be between 1 and {MaxQuantityPerLine} per line.");

            // Everything about whether this design may be printed — archived
            // garment, unapproved artwork, a placement that no longer fits.
            var designCheck = await _designLogic.ValidateForOrderAsync(requested.DesignId);
            if (!designCheck.Success)
                return OrderResult.Fail(designCheck.ErrorMessage!);

            var design = await _designRepository.GetAsync(d => d.DesignId == requested.DesignId);
            var product = await _productRepository.GetAsync(p => p.ProductId == design!.ProductId);

            var sizeCode = requested.SizeCode?.Trim().ToUpperInvariant() ?? string.Empty;

            if (!product!.Sizes.Contains(sizeCode))
                return OrderResult.Fail($"The {product.Colour} {product.Name} doesn't come in {sizeCode}.");

            lines.Add(new OrderLine
            {
                DesignId = design!.DesignId,
                SizeCode = sizeCode,
                Quantity = requested.Quantity,

                // Snapshotted, not looked up. Re-pricing the catalogue must
                // never restate what somebody already agreed to pay.
                UnitPrice = Pricing.UnitPrice(product, design, sizeCode)
            });
        }

        var garments = lines.Sum(l => l.Quantity);

        var capacity = await GetCapacityAsync(dueOn);
        if (garments > capacity.Remaining)
        {
            return capacity.IsFull
                ? OrderResult.Fail($"We're fully booked on {dueOn:dddd d MMMM}. Try another date.")
                : OrderResult.Fail(
                    $"We've only got room for {capacity.Remaining} more garments on {dueOn:dddd d MMMM}, " +
                    $"and this order is {garments}. Try another date or split the order.");
        }

        var order = new Order
        {
            CustomerId = customer.CustomerId,
            Status = OrderStatus.Received,
            DueOn = dueOn,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            RightsAttested = true,
            RightsAttestedAt = _clock.UtcNow,
            CreatedAt = _clock.UtcNow
        };

        foreach (var line in lines)
            order.OrderLines.Add(line);

        await _orderRepository.AddAsync(order);
        await _orderRepository.SaveChangesAsync();

        return OrderResult.Ok(order.OrderId);
    }

    public async Task<OrderResult> SetStatusAsync(int orderId, string status)
    {
        if (!OrderStatus.All.Contains(status))
            return OrderResult.Fail("Unknown status.");

        // Cancelling carries a reason, so it goes through its own method rather
        // than being reachable as a plain status change with no explanation.
        if (status == OrderStatus.Cancelled)
            return OrderResult.Fail("Use Cancel so the reason gets recorded.");

        var order = await _orderRepository.GetAsync(o => o.OrderId == orderId);
        if (order == null)
            return OrderResult.Fail("Order not found.");

        if (order.Status == OrderStatus.Cancelled)
            return OrderResult.Fail("That order was cancelled.");

        order.Status = status;
        order.CompletedAt = status == OrderStatus.Completed ? _clock.UtcNow : null;

        await _orderRepository.SaveChangesAsync();
        return OrderResult.Ok(order.OrderId);
    }

    public async Task<OrderResult> CancelAsync(int orderId, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return OrderResult.Fail("Say why the order is being cancelled.");

        var order = await _orderRepository.GetAsync(o => o.OrderId == orderId);
        if (order == null)
            return OrderResult.Fail("Order not found.");

        // Cancelling a finished job doesn't un-print the shirts, and it would
        // quietly hand that day's capacity back to the scheduler.
        if (order.Status == OrderStatus.Completed)
            return OrderResult.Fail("That order is already finished.");

        order.Status = OrderStatus.Cancelled;
        order.CancellationReason = reason.Trim();

        await _orderRepository.SaveChangesAsync();
        return OrderResult.Ok(order.OrderId);
    }

    public async Task<DayCapacity> GetCapacityAsync(DateTime date)
    {
        var due = date.Date;
        var orders = await _orderRepository.GetDueOnAsync(due);

        // Only open orders compete for the press. A cancelled order gives its
        // slots back; a collected one was never going to use them again.
        var promised = orders
            .Where(o => OrderStatus.IsOpen(o.Status))
            .Sum(o => o.OrderLines.Sum(l => l.Quantity));

        return new DayCapacity(due, promised, _settings.DailyPrintCapacity);
    }
}
