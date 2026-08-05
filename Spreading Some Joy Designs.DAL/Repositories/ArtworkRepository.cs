using Microsoft.EntityFrameworkCore;
using SpreadingJoy.DAL.Context;
using SpreadingJoy.DAL.Repositories.Base;
using SpreadingJoy.Domain.EntityModels;
using SpreadingJoy.Domain.IRepositories;

namespace SpreadingJoy.DAL.Repositories;

public class ArtworkRepository : GenericRepository<SpreadingJoyContext, Artwork>, IArtworkRepository
{
    public ArtworkRepository(SpreadingJoyContext context) : base(context) { }

    public async Task<Artwork?> GetByStoredFileNameAsync(string storedFileName)
    {
        return await Context.Artworks.FirstOrDefaultAsync(a => a.StoredFileName == storedFileName);
    }

    public async Task<IList<Artwork>> GetByStatusAsync(string status)
    {
        // Oldest first — whoever has been waiting longest gets looked at first.
        // Matches the IX_Artworks_Status_CreatedAt index.
        return await Context.Artworks
            .Where(a => a.Status == status)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync();
    }
}
