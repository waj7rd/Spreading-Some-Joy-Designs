using Microsoft.EntityFrameworkCore;
using SpreadingJoy.DAL.Context;
using SpreadingJoy.DAL.Repositories.Base;
using SpreadingJoy.Domain.EntityModels;
using SpreadingJoy.Domain.IRepositories;

namespace SpreadingJoy.DAL.Repositories;

public class DesignRepository : GenericRepository<SpreadingJoyContext, Design>, IDesignRepository
{
    public DesignRepository(SpreadingJoyContext context) : base(context) { }

    public async Task<Design?> GetWithArtworkAsync(int designId)
    {
        return await Context.Designs
            .Include(d => d.Product)
            .Include(d => d.FrontArtwork)
            .Include(d => d.BackArtwork)
            .FirstOrDefaultAsync(d => d.DesignId == designId);
    }
}
