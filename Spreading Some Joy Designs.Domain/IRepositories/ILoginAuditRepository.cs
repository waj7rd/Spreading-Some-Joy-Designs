using SpreadingJoy.Domain.EntityModels;
using SpreadingJoy.Domain.IRepositories.IBase;

namespace SpreadingJoy.Domain.IRepositories;

public interface ILoginAuditRepository : IGenericRepository<LoginAudit>
{
    Task<IList<LoginAudit>> GetRecentAsync(int count);
}
