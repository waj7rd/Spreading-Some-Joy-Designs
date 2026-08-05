namespace SpreadingJoy.Domain.Artworks;

// Where the bytes live once we've decided to keep them.
//
// Behind an interface because "a folder under wwwroot" is a first-deployment
// answer, not a permanent one. The day this needs to be blob storage or a CDN,
// that's a new implementation and no change to the logic layer.
public interface IImageStore
{
    // Writes the bytes under a name the store chooses, and returns it. Callers
    // never supply the filename — a name taken from a stranger's URL is a path
    // traversal waiting to happen.
    Task<string> SaveAsync(byte[] content, string extension, string sha256, CancellationToken cancellationToken = default);

    Task<byte[]?> ReadAsync(string storedFileName, CancellationToken cancellationToken = default);

    Task DeleteAsync(string storedFileName, CancellationToken cancellationToken = default);

    // A browser-reachable path for the stored file, for use in <img src>.
    string PublicPath(string storedFileName);
}
