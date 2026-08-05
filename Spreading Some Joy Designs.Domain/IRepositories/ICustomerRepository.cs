using SpreadingJoy.Domain.EntityModels;
using SpreadingJoy.Domain.IRepositories.IBase;

namespace SpreadingJoy.Domain.IRepositories;

public interface ICustomerRepository : IGenericRepository<Customer>
{
    // The customer list screen shows an order count per row. Loading them one
    // customer at a time is the classic N+1 — one query for the list, then one
    // more for every row on it.
    Task<IList<Customer>> GetAllWithOrdersAsync();

    // Orders and their lines in one round trip, for the customer detail screen.
    Task<Customer?> GetWithOrdersAsync(int customerId);
}
