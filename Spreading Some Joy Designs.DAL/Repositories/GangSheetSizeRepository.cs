using Microsoft.EntityFrameworkCore;
using SpreadingJoy.DAL.Context;
using SpreadingJoy.DAL.Repositories.Base;
using SpreadingJoy.Domain.EntityModels;
using SpreadingJoy.Domain.IRepositories;

namespace SpreadingJoy.DAL.Repositories;

public class GangSheetSizeRepository : GenericRepository<SpreadingJoyContext, GangSheetSize>, IGangSheetSizeRepository
{
    public GangSheetSizeRepository(SpreadingJoyContext context) : base(context) { }

    public async Task<IList<GangSheetSize>> GetActiveAsync()
    {
        // Shortest first: somebody buying their first sheet wants the small one
        // at the top of the list, not the roll.
        return await Context.GangSheetSizes
            .Where(s => s.IsActive)
            .OrderBy(s => s.LengthMm)
            .ThenBy(s => s.WidthMm)
            .ToListAsync();
    }
}
