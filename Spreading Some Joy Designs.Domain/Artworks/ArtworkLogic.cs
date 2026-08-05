using System.Security.Cryptography;
using SpreadingJoy.Domain.EntityModels;
using SpreadingJoy.Domain.IRepositories;
using SpreadingJoy.Domain.Shared;

namespace SpreadingJoy.Domain.Artworks;

public class ArtworkLogic : IArtworkLogic
{
    private readonly IArtworkRepository _artworkRepository;
    private readonly IImageFetcher _fetcher;
    private readonly IImageInspector _inspector;
    private readonly IImageStore _store;
    private readonly IStudioClock _clock;

    public ArtworkLogic(
        IArtworkRepository artworkRepository,
        IImageFetcher fetcher,
        IImageInspector inspector,
        IImageStore store,
        IStudioClock clock)
    {
        _artworkRepository = artworkRepository;
        _fetcher = fetcher;
        _inspector = inspector;
        _store = store;
        _clock = clock;
    }

    public async Task<ArtworkResult> AddFromUrlAsync(string url, int? customerId, CancellationToken cancellationToken = default)
    {
        // Shape and scheme are checked here so an obviously bad address is
        // refused without a network call at all. The fetcher re-checks the
        // resolved addresses, which is the part that actually matters.
        var urlError = ImageUrlPolicy.CheckUrl(url, out var parsed);
        if (urlError != null)
            return ArtworkResult.Fail(urlError);

        var fetched = await _fetcher.FetchAsync(parsed!.ToString(), cancellationToken);
        if (!fetched.Success)
            return ArtworkResult.Fail(fetched.ErrorMessage!);

        return await StoreAsync(
            fetched.Content!,
            sourceUrl: fetched.ResolvedUrl ?? parsed.ToString(),
            originalFileName: null,
            customerId,
            cancellationToken);
    }

    public async Task<ArtworkResult> AddFromUploadAsync(byte[] content, string? originalFileName, int? customerId, CancellationToken cancellationToken = default)
    {
        if (content == null || content.Length == 0)
            return ArtworkResult.Fail("That file was empty.");

        return await StoreAsync(content, sourceUrl: null, originalFileName, customerId, cancellationToken);
    }

    // The single path both entry points run through. Everything that decides
    // whether bytes are acceptable happens here exactly once.
    private async Task<ArtworkResult> StoreAsync(
        byte[] content,
        string? sourceUrl,
        string? originalFileName,
        int? customerId,
        CancellationToken cancellationToken)
    {
        if (content.LongLength > ImageLimits.MaxBytes)
            return ArtworkResult.Fail($"That image is over {ImageLimits.MaxBytes / (1024 * 1024)}MB. Try a smaller one.");

        // Decode before believing anything about what this is. The header the
        // server sent and the extension on the filename are both just claims.
        var info = _inspector.Inspect(content);
        if (info == null)
            return ArtworkResult.Fail("We couldn't read that as an image. PNG, JPEG, GIF and WebP all work.");

        if (info.PixelCount > ImageLimits.MaxPixels)
            return ArtworkResult.Fail("That image has too many pixels to process safely. Try one under 80 megapixels.");

        if (info.WidthPx < ImageLimits.MinDimensionPx || info.HeightPx < ImageLimits.MinDimensionPx)
            return ArtworkResult.Fail(
                $"That image is only {info.WidthPx}×{info.HeightPx}. We need at least " +
                $"{ImageLimits.MinDimensionPx}px on each side to print anything usable.");

        // Re-encode to strip metadata and anything else riding along in the
        // original container, then hash what we're actually keeping. Hashing the
        // input instead would mean the same picture with different EXIF counted
        // as two images, which defeats both the dedupe and the rejection memory.
        var normalised = _inspector.Normalise(content, info);
        var sha256 = Sha256Hex(normalised);

        var existing = await _artworkRepository.GetAsync(a => a.Sha256 == sha256);
        if (existing != null)
        {
            // Same bytes, already on file. Reuse the row rather than storing a
            // second copy — which also means an image a moderator has already
            // rejected comes straight back rejected, no matter which URL it
            // arrived through this time.
            return ArtworkResult.Ok(existing.ArtworkId, wasDeduplicated: true);
        }

        var storedFileName = await _store.SaveAsync(normalised, info.Extension, sha256, cancellationToken);

        var artwork = new Artwork
        {
            CustomerId = customerId,
            SourceUrl = sourceUrl,
            OriginalFileName = Truncate(originalFileName, 255),
            StoredFileName = storedFileName,
            ContentType = info.ContentType,
            ByteSize = normalised.LongLength,
            WidthPx = info.WidthPx,
            HeightPx = info.HeightPx,
            Sha256 = sha256,
            Status = ArtworkStatus.Pending,
            CreatedAt = _clock.UtcNow
        };

        await _artworkRepository.AddAsync(artwork);
        await _artworkRepository.SaveChangesAsync();

        return ArtworkResult.Ok(artwork.ArtworkId);
    }

    public async Task<Artwork?> GetByIdAsync(int artworkId) =>
        await _artworkRepository.GetAsync(a => a.ArtworkId == artworkId);

    public async Task<Artwork?> GetByStoredFileNameAsync(string storedFileName) =>
        await _artworkRepository.GetByStoredFileNameAsync(storedFileName);

    public async Task<IList<Artwork>> GetPendingAsync() =>
        await _artworkRepository.GetByStatusAsync(ArtworkStatus.Pending);

    public async Task<IList<Artwork>> GetByStatusAsync(string status)
    {
        if (!ArtworkStatus.All.Contains(status))
            return new List<Artwork>();

        return await _artworkRepository.GetByStatusAsync(status);
    }

    public async Task<ArtworkResult> ApproveAsync(int artworkId, int reviewedByUserId)
    {
        var artwork = await _artworkRepository.GetAsync(a => a.ArtworkId == artworkId);
        if (artwork == null)
            return ArtworkResult.Fail("Artwork not found.");

        artwork.Status = ArtworkStatus.Approved;
        artwork.RejectionReason = null;
        artwork.ReviewedByUserId = reviewedByUserId;
        artwork.ReviewedAt = _clock.UtcNow;

        await _artworkRepository.SaveChangesAsync();
        return ArtworkResult.Ok(artwork.ArtworkId);
    }

    public async Task<ArtworkResult> RejectAsync(int artworkId, int reviewedByUserId, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return ArtworkResult.Fail("Say why it's being rejected — the customer sees this.");

        var artwork = await _artworkRepository.GetAsync(a => a.ArtworkId == artworkId);
        if (artwork == null)
            return ArtworkResult.Fail("Artwork not found.");

        artwork.Status = ArtworkStatus.Rejected;
        artwork.RejectionReason = reason.Trim();
        artwork.ReviewedByUserId = reviewedByUserId;
        artwork.ReviewedAt = _clock.UtcNow;

        // The file itself is kept. A rejected image is the evidence for why it
        // was rejected, and deleting it means the next moderator to see the same
        // hash has nothing to compare against.
        await _artworkRepository.SaveChangesAsync();
        return ArtworkResult.Ok(artwork.ArtworkId);
    }

    private static string Sha256Hex(byte[] content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}
