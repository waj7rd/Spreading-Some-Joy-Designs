using SpreadingJoy.Domain.EntityModels;
using SpreadingJoy.Domain.IRepositories.IBase;

namespace SpreadingJoy.Domain.IRepositories;

public interface IGangSheetSizeRepository : IGenericRepository<GangSheetSize>
{
    // What the public builder offers, shortest first — somebody buying their
    // first sheet wants the cheap one at the top, not the roll.
    Task<IList<GangSheetSize>> GetActiveAsync();
}
