using SpreadingJoy.Domain.EntityModels;
using SpreadingJoy.Domain.IRepositories.IBase;

namespace SpreadingJoy.Domain.IRepositories;

public interface IOrderRepository : IGenericRepository<Order>
{
    Task<Order?> GetWithLinesAsync(int orderId);

    // Everything due on a given studio-local date, lines included. This is what
    // the capacity check counts, so it has to include the lines — capacity is
    // measured in garments, not in orders.
    Task<IList<Order>> GetDueOnAsync(DateTime date);

    // The production board: open orders, soonest due first.
    Task<IList<Order>> GetOpenAsync();
}
