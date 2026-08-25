using Microsoft.EntityFrameworkCore;
using SpreadingJoy.DAL.Context;
using SpreadingJoy.DAL.Repositories.Base;
using SpreadingJoy.Domain.EntityModels;
using SpreadingJoy.Domain.IRepositories;

namespace SpreadingJoy.DAL.Repositories;

public class GangSheetRequestRepository : GenericRepository<SpreadingJoyContext, GangSheetRequest>, IGangSheetRequestRepository
{
    public GangSheetRequestRepository(SpreadingJoyContext context) : base(context) { }

    public async Task<IList<GangSheetRequest>> GetByStatusAsync(string status)
    {
        // The artwork comes with the items because the decision staff are making
        // is "can this be printed", and that is a question about the pictures.
        // A queue that couldn't show them would be a queue nobody could work.
        return await Context.GangSheetRequests
            .Include(r => r.GangSheetSize)
            .Include(r => r.HandledByUser)
            .Include(r => r.Items)
                .ThenInclude(i => i.Artwork)
            .Where(r => r.Status == status)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync();
    }

    public async Task<GangSheetRequest?> GetWithItemsAsync(int gangSheetRequestId)
    {
        // Artwork loaded fresh here rather than trusted from whenever the
        // request was submitted: acceptance reads Status off these rows, and a
        // stale one would be the gate answering a question about last week.
        return await Context.GangSheetRequests
            .Include(r => r.GangSheetSize)
            .Include(r => r.HandledByUser)
            .Include(r => r.Items)
                .ThenInclude(i => i.Artwork)
            .FirstOrDefaultAsync(r => r.GangSheetRequestId == gangSheetRequestId);
    }

    public async Task<int> CountPendingAsync() =>
        await Context.GangSheetRequests.CountAsync(r => r.Status == GangSheetRequestStatus.Pending);
}
