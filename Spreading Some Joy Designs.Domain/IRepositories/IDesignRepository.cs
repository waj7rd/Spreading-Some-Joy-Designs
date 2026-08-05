using SpreadingJoy.Domain.EntityModels;
using SpreadingJoy.Domain.IRepositories.IBase;

namespace SpreadingJoy.Domain.IRepositories;

public interface IDesignRepository : IGenericRepository<Design>
{
    // A design is never useful on its own — every screen that shows one needs
    // the garment it sits on and the artwork placed on it. Loading them
    // separately is three round trips to render one card.
    Task<Design?> GetWithArtworkAsync(int designId);

    // The shop and its management screen. Loads the garment and both artworks,
    // because every row is rendered as a shirt.
    Task<IList<Design>> GetStudioDesignsAsync(bool activeOnly);
}
