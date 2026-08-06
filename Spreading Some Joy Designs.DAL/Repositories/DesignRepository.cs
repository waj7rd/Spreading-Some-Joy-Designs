using Microsoft.EntityFrameworkCore;
using SpreadingJoy.DAL.Context;
using SpreadingJoy.DAL.Repositories.Base;
using SpreadingJoy.Domain.EntityModels;
using SpreadingJoy.Domain.IRepositories;

namespace SpreadingJoy.DAL.Repositories;

public class DesignRepository : GenericRepository<SpreadingJoyContext, Design>, IDesignRepository
{
    public DesignRepository(SpreadingJoyContext context) : base(context) { }

    public async Task<IList<Design>> GetStudioDesignsAsync(bool activeOnly)
    {
        var query = Context.Designs
            .Include(d => d.Product)
            .Include(d => d.FrontArtwork)
            .Include(d => d.BackArtwork)
            .Where(d => d.IsStudioDesign);

        if (activeOnly)
        {
            // The shop must never offer a design built on a garment that's been
            // archived — it would be orderable right up until OrderLogic
            // refused it at the very last step.
            query = query.Where(d => d.IsActive && d.Product.IsActive);
        }

        return await query
            .OrderByDescending(d => d.IsActive)
            .ThenByDescending(d => d.CreatedAt)
            .ToListAsync();
    }

    public async Task<Design?> GetByPublicTokenAsync(Guid publicToken)
    {
        return await Context.Designs
            .Include(d => d.Product)
            .Include(d => d.FrontArtwork)
            .Include(d => d.BackArtwork)
            .FirstOrDefaultAsync(d => d.PublicToken == publicToken);
    }

    public async Task<Design?> GetWithArtworkAsync(int designId)
    {
        return await Context.Designs
            .Include(d => d.Product)
            .Include(d => d.FrontArtwork)
            .Include(d => d.BackArtwork)
            .FirstOrDefaultAsync(d => d.DesignId == designId);
    }
}
