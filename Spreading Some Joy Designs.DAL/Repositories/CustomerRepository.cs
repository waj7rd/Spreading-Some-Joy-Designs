using Microsoft.EntityFrameworkCore;
using SpreadingJoy.DAL.Context;
using SpreadingJoy.DAL.Repositories.Base;
using SpreadingJoy.Domain.EntityModels;
using SpreadingJoy.Domain.IRepositories;

namespace SpreadingJoy.DAL.Repositories;

public class CustomerRepository : GenericRepository<SpreadingJoyContext, Customer>, ICustomerRepository
{
    public CustomerRepository(SpreadingJoyContext context) : base(context) { }

    public async Task<IList<Customer>> GetAllWithOrdersAsync()
    {
        return await Context.Customers
            .Include(c => c.Orders)
            .ToListAsync();
    }

    public async Task<Customer?> GetWithOrdersAsync(int customerId)
    {
        return await Context.Customers
            .Include(c => c.Orders)
                .ThenInclude(o => o.OrderLines)
                    .ThenInclude(l => l.Design)
            .FirstOrDefaultAsync(c => c.CustomerId == customerId);
    }
}
