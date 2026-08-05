using SpreadingJoy.Domain.EntityModels;
using SpreadingJoy.Domain.IRepositories.IBase;

namespace SpreadingJoy.Domain.IRepositories;

public interface IOrderRequestRepository : IGenericRepository<OrderRequest>
{
    // The staff queue, with the design and its artwork loaded — a request can't
    // be judged without seeing the picture it's asking to print.
    Task<IList<OrderRequest>> GetByStatusAsync(string status);

    Task<OrderRequest?> GetWithDesignAsync(int orderRequestId);
}
