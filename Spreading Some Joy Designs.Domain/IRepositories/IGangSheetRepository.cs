using SpreadingJoy.Domain.EntityModels;
using SpreadingJoy.Domain.IRepositories.IBase;

namespace SpreadingJoy.Domain.IRepositories;

public interface IGangSheetRepository : IGenericRepository<GangSheet>
{
    // A sheet is never useful without its items — there is no screen that shows
    // one without drawing what's on it. The artwork comes too, because every
    // item is rendered as the picture it will print.
    Task<GangSheet?> GetWithItemsAsync(int gangSheetId);

    // The list screen. Items are loaded because the summary on each row —
    // how many transfers, how much film, how much of it is covered — is counted
    // off them.
    Task<IList<GangSheet>> GetAllWithItemsAsync();

    // What's waiting to be printed: lines on open orders, with the design and
    // both pieces of artwork, because each printed side becomes its own
    // transfer.
    Task<IList<OrderLine>> GetCandidateLinesAsync();

    // How many transfers each of these order lines already has on a sheet
    // somewhere. Counted in the database rather than by loading every item:
    // this runs on every visit to the build screen.
    //
    // A line already on a sheet is shown, not hidden — a reprint is a normal
    // thing to want, and quietly dropping it from the list would look like the
    // order had gone missing.
    Task<IDictionary<int, int>> CountPlacementsByOrderLineAsync(IReadOnlyCollection<int> orderLineIds);

    // Items are added and removed through the sheet that owns them, so these
    // sit here rather than on a repository of their own. There is no screen
    // anywhere that edits a transfer without a sheet in front of it.
    Task AddItemAsync(GangSheetItem item);

    void RemoveItem(GangSheetItem item);
}
