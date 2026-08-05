using SpreadingJoy.Domain.EntityModels;
using SpreadingJoy.Domain.IRepositories.IBase;

namespace SpreadingJoy.Domain.IRepositories;

public interface IArtworkRepository : IGenericRepository<Artwork>
{
    // The moderation queue, oldest first — the person waiting longest gets
    // looked at first.
    Task<IList<Artwork>> GetByStatusAsync(string status);

    // Used by the endpoint that serves the bytes. Stored names are unique
    // because they're built from the content hash.
    Task<Artwork?> GetByStoredFileNameAsync(string storedFileName);
}
