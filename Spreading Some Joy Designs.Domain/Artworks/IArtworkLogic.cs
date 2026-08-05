using SpreadingJoy.Domain.EntityModels;
using SpreadingJoy.Domain.Shared;

namespace SpreadingJoy.Domain.Artworks;

// Getting a picture into the system, and deciding whether it may be printed.
//
// Both entry points — a pasted URL and an uploaded file — converge on the same
// validation, the same normalisation, the same storage, and the same review
// queue. There is no path that skips any of it, which is the only reason it's
// worth having.
public interface IArtworkLogic
{
    // Fetches the URL server-side and stores our own copy of the bytes.
    //
    // approvedByUserId marks the image as reviewed on arrival, and is only ever
    // supplied when a signed-in member of staff is the one adding it — they are
    // the moderator, and the studio's own artwork doesn't need reviewing by the
    // person who just made it. It's the reason studio designs need no bypass in
    // the ordering rules: they pass the normal approval gate honestly.
    Task<ArtworkResult> AddFromUrlAsync(string url, int? customerId, int? approvedByUserId = null, CancellationToken cancellationToken = default);

    // Same pipeline, bytes supplied directly. originalFileName is kept for
    // display only and never used to build a path.
    Task<ArtworkResult> AddFromUploadAsync(byte[] content, string? originalFileName, int? customerId, int? approvedByUserId = null, CancellationToken cancellationToken = default);

    Task<Artwork?> GetByIdAsync(int artworkId);

    // Used by the endpoint that serves the bytes.
    Task<Artwork?> GetByStoredFileNameAsync(string storedFileName);

    // The moderation queue, oldest first.
    Task<IList<Artwork>> GetPendingAsync();

    Task<IList<Artwork>> GetByStatusAsync(string status);

    // A human decision, recorded against the person who made it. Nothing
    // reaches the press without one.
    Task<ArtworkResult> ApproveAsync(int artworkId, int reviewedByUserId);

    Task<ArtworkResult> RejectAsync(int artworkId, int reviewedByUserId, string reason);
}

public class ArtworkResult : IOperationResult
{
    public bool Success { get; private set; }
    public string? ErrorMessage { get; private set; }
    public int ArtworkId { get; private set; }

    // True when the bytes matched something already on file and no new row was
    // created. Worth surfacing: it's why an image can come back already
    // rejected the moment it's added.
    public bool WasDeduplicated { get; private set; }

    public static ArtworkResult Ok(int artworkId, bool wasDeduplicated = false) =>
        new() { Success = true, ArtworkId = artworkId, WasDeduplicated = wasDeduplicated };

    public static ArtworkResult Fail(string message) =>
        new() { Success = false, ErrorMessage = message };
}
