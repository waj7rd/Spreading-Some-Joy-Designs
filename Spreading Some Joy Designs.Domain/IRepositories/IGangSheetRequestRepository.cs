using SpreadingJoy.Domain.EntityModels;
using SpreadingJoy.Domain.IRepositories.IBase;

namespace SpreadingJoy.Domain.IRepositories;

public interface IGangSheetRequestRepository : IGenericRepository<GangSheetRequest>
{
    // The queue, oldest first. Items and their artwork come with it, because the
    // decision staff are making is "can this be printed" — and that is a
    // question about the artwork, which has to be on screen to answer.
    Task<IList<GangSheetRequest>> GetByStatusAsync(string status);

    Task<GangSheetRequest?> GetWithItemsAsync(int gangSheetRequestId);

    Task<int> CountPendingAsync();
}
