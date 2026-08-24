using SpreadingJoy.Domain.EntityModels;
using SpreadingJoy.Domain.Shared;

namespace SpreadingJoy.Domain.Ordering;

// Placing and running orders.
public interface IOrderLogic
{
    Task<Order?> GetByIdAsync(int orderId);

    Task<IList<Order>> GetOpenAsync();

    Task<IList<Order>> GetForCustomerAsync(int customerId);

    // Places an order for an existing customer. Every rule is applied here
    // rather than in the controller, because the staff screen and the public
    // request path both arrive at this method and neither may skip a check.
    Task<OrderResult> PlaceAsync(PlaceOrderRequest request);

    Task<OrderResult> SetStatusAsync(int orderId, string status);

    Task<OrderResult> CancelAsync(int orderId, string reason);

    // Garments already promised for a date, and what's left. Drives the "we're
    // nearly full that day" hint on the order form.
    Task<DayCapacity> GetCapacityAsync(DateTime date);
}

public record OrderLineRequest(int DesignId, string SizeCode, int Quantity);

public record PlaceOrderRequest(
    int CustomerId,
    DateTime DueOn,
    IReadOnlyCollection<OrderLineRequest> Lines,
    bool RightsAttested,
    string? Notes,

    // Defaulted to collection, same as SubmitOrderRequest: an order placed by a
    // path that has never heard of shipping is a pickup order, not an
    // under-specified one.
    string FulfilmentMethod = EntityModels.FulfilmentMethod.Pickup,
    ShippingAddress? ShipTo = null);

public record DayCapacity(DateTime Date, int Promised, int Total)
{
    public int Remaining => Math.Max(0, Total - Promised);

    public bool IsFull => Remaining == 0;
}

public class OrderResult : IOperationResult
{
    public bool Success { get; private set; }
    public string? ErrorMessage { get; private set; }
    public int OrderId { get; private set; }

    public static OrderResult Ok(int orderId) => new() { Success = true, OrderId = orderId };

    public static OrderResult Fail(string message) => new() { Success = false, ErrorMessage = message };
}
