using Microsoft.EntityFrameworkCore;
using SpreadingJoy.DAL.Context;
using SpreadingJoy.DAL.Repositories.Base;
using SpreadingJoy.Domain.EntityModels;
using SpreadingJoy.Domain.IRepositories;

namespace SpreadingJoy.DAL.Repositories;

public class LoginAuditRepository : GenericRepository<SpreadingJoyContext, LoginAudit>, ILoginAuditRepository
{
    public LoginAuditRepository(SpreadingJoyContext context) : base(context) { }

    public async Task<IList<LoginAudit>> GetRecentAsync(int count)
    {
        return await Context.LoginAudits
            .Include(a => a.User)
            .OrderByDescending(a => a.OccurredAt)
            .Take(count)
            .ToListAsync();
    }
}
