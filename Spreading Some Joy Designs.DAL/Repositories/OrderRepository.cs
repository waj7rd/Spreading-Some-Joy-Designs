using Microsoft.EntityFrameworkCore;
using SpreadingJoy.DAL.Context;
using SpreadingJoy.DAL.Repositories.Base;
using SpreadingJoy.Domain.EntityModels;
using SpreadingJoy.Domain.IRepositories;

namespace SpreadingJoy.DAL.Repositories;

public class OrderRepository : GenericRepository<SpreadingJoyContext, Order>, IOrderRepository
{
    public OrderRepository(SpreadingJoyContext context) : base(context) { }

    public async Task<Order?> GetWithLinesAsync(int orderId)
    {
        return await Context.Orders
            .Include(o => o.Customer)
            .Include(o => o.OrderLines)
                .ThenInclude(l => l.Design)
                    .ThenInclude(d => d.Product)
            .FirstOrDefaultAsync(o => o.OrderId == orderId);
    }

    public async Task<IList<Order>> GetDueOnAsync(DateTime date)
    {
        var due = date.Date;

        // The lines have to come with it: capacity is counted in garments, and
        // an order with no lines loaded counts as zero — which would let the
        // press be booked twice over without a single error.
        return await Context.Orders
            .Include(o => o.OrderLines)
            .Where(o => o.DueOn == due)
            .ToListAsync();
    }

    public async Task<IList<Order>> GetOpenAsync()
    {
        return await Context.Orders
            .Include(o => o.Customer)
            .Include(o => o.OrderLines)
            .Where(o => OrderStatus.Open.Contains(o.Status))
            .OrderBy(o => o.DueOn)
            .ThenBy(o => o.CreatedAt)
            .ToListAsync();
    }
}
