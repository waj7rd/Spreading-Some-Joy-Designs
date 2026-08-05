using SpreadingJoy.Domain.EntityModels;
using SpreadingJoy.Domain.Shared;

namespace SpreadingJoy.Domain.Ordering;

// The anonymous storefront path.
//
// Nothing a stranger types becomes a customer record until a member of staff
// has accepted it. That's the rule this interface exists to make structural
// rather than remembered.
public interface IOrderRequestLogic
{
    Task<OrderRequestResult> SubmitAsync(SubmitOrderRequest request);

    Task<IList<OrderRequest>> GetPendingAsync();

    Task<OrderRequest?> GetByIdAsync(int orderRequestId);

    // Creates the customer (or reuses one matching on email), attaches the
    // design to them, and places the order — all inside one transaction, so a
    // refusal leaves no half-made customer behind.
    Task<OrderRequestResult> AcceptAsync(int orderRequestId, int handledByUserId, DateTime dueOn);

    Task<OrderRequestResult> DeclineAsync(int orderRequestId, int handledByUserId, string reason);
}

public record SubmitOrderRequest(
    string CustomerName,
    string? Email,
    string Phone,
    int DesignId,
    string SizeCode,
    int Quantity,
    DateTime RequestedFor,
    bool RightsAttested,
    string? Notes);

public class OrderRequestResult : IOperationResult
{
    public bool Success { get; private set; }
    public string? ErrorMessage { get; private set; }
    public int OrderRequestId { get; private set; }

    // Set only when an acceptance produced an order.
    public int? OrderId { get; private set; }

    public static OrderRequestResult Ok(int orderRequestId, int? orderId = null) =>
        new() { Success = true, OrderRequestId = orderRequestId, OrderId = orderId };

    public static OrderRequestResult Fail(string message) =>
        new() { Success = false, ErrorMessage = message };
}
