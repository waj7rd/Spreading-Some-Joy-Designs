using Microsoft.EntityFrameworkCore;
using SpreadingJoy.DAL.Context;
using SpreadingJoy.DAL.Repositories.Base;
using SpreadingJoy.Domain.EntityModels;
using SpreadingJoy.Domain.IRepositories;

namespace SpreadingJoy.DAL.Repositories;

public class OrderRequestRepository : GenericRepository<SpreadingJoyContext, OrderRequest>, IOrderRequestRepository
{
    public OrderRequestRepository(SpreadingJoyContext context) : base(context) { }

    public async Task<IList<OrderRequest>> GetByStatusAsync(string status)
    {
        // The design and its artwork come along, because a request can't be
        // judged without seeing the picture it's asking to print.
        return await Context.OrderRequests
            .Include(r => r.Design)
                .ThenInclude(d => d.Product)
            .Include(r => r.Design)
                .ThenInclude(d => d.FrontArtwork)
            .Include(r => r.Design)
                .ThenInclude(d => d.BackArtwork)
            .Where(r => r.Status == status)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync();
    }

    public async Task<OrderRequest?> GetWithDesignAsync(int orderRequestId)
    {
        return await Context.OrderRequests
            .Include(r => r.Design)
                .ThenInclude(d => d.Product)
            .Include(r => r.Design)
                .ThenInclude(d => d.FrontArtwork)
            .Include(r => r.Design)
                .ThenInclude(d => d.BackArtwork)
            .FirstOrDefaultAsync(r => r.OrderRequestId == orderRequestId);
    }
}
