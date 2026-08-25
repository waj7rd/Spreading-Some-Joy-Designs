using Microsoft.EntityFrameworkCore;
using SpreadingJoy.DAL.Context;
using SpreadingJoy.DAL.Repositories.Base;
using SpreadingJoy.Domain.EntityModels;
using SpreadingJoy.Domain.IRepositories;

namespace SpreadingJoy.DAL.Repositories;

public class GangSheetRepository : GenericRepository<SpreadingJoyContext, GangSheet>, IGangSheetRepository
{
    public GangSheetRepository(SpreadingJoyContext context) : base(context) { }

    public async Task<GangSheet?> GetWithItemsAsync(int gangSheetId)
    {
        // The artwork has to come with the items. Every screen draws each
        // transfer as the picture it will print, and the approval check that
        // guards the press reads Artwork.Status off exactly these rows — loaded
        // fresh here rather than trusted from whenever the item was added.
        return await Context.GangSheets
            .Include(s => s.CreatedByUser)
            .Include(s => s.Customer)
            .Include(s => s.Items)
                .ThenInclude(i => i.Artwork)
            .Include(s => s.Items)
                .ThenInclude(i => i.Design)
            .FirstOrDefaultAsync(s => s.GangSheetId == gangSheetId);
    }

    public async Task<IList<GangSheet>> GetAllWithItemsAsync()
    {
        // The list screen counts transfers and sums coverage off the items, so
        // a sheet loaded without them would report an empty sheet rather than
        // no answer — the same trap GetDueOnAsync documents for capacity.
        return await Context.GangSheets
            .Include(s => s.CreatedByUser)
            .Include(s => s.Customer)
            .Include(s => s.Items)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync();
    }

    public async Task<IList<OrderLine>> GetCandidateLinesAsync()
    {
        return await Context.OrderLines
            .Include(l => l.Order)
                .ThenInclude(o => o.Customer)
            .Include(l => l.Design)
                .ThenInclude(d => d.FrontArtwork)
            .Include(l => l.Design)
                .ThenInclude(d => d.BackArtwork)
            .Where(l => OrderStatus.Open.Contains(l.Order.Status))
            .OrderBy(l => l.Order.DueOn)
            .ThenBy(l => l.OrderId)
            .ToListAsync();
    }

    public async Task<IDictionary<int, int>> CountPlacementsByOrderLineAsync(IReadOnlyCollection<int> orderLineIds)
    {
        if (orderLineIds.Count == 0)
            return new Dictionary<int, int>();

        // Grouped in SQL rather than by pulling every item back. This runs on
        // every visit to the build screen, against a table that grows by one row
        // per printed transfer.
        var counts = await Context.GangSheetItems
            .Where(i => i.OrderLineId != null && orderLineIds.Contains(i.OrderLineId.Value))
            .GroupBy(i => i.OrderLineId!.Value)
            .Select(g => new { OrderLineId = g.Key, Count = g.Count() })
            .ToListAsync();

        return counts.ToDictionary(c => c.OrderLineId, c => c.Count);
    }

    public async Task AddItemAsync(GangSheetItem item) =>
        await Context.GangSheetItems.AddAsync(item);

    public void RemoveItem(GangSheetItem item) =>
        Context.GangSheetItems.Remove(item);
}
