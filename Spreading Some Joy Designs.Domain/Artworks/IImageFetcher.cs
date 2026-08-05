namespace SpreadingJoy.Domain.Artworks;

// Goes and gets the bytes behind a URL, subject to ImageUrlPolicy.
//
// An interface in the Domain with its implementation in the DAL, for the same
// reason as every other repository: the rules about what makes a usable image
// are business rules and belong here, while HttpClient, DNS resolution and
// redirect handling are infrastructure and belong out there. It also means the
// whole of ArtworkLogic is testable without a network.
public interface IImageFetcher
{
    Task<ImageFetchResult> FetchAsync(string url, CancellationToken cancellationToken = default);
}

public class ImageFetchResult
{
    public bool Success { get; private set; }

    public string? ErrorMessage { get; private set; }

    // Only set on success.
    public byte[]? Content { get; private set; }

    // As reported by the server, already checked against the allow-list. The
    // inspector still decides what the bytes actually are — a Content-Type
    // header is a claim by a stranger's server.
    public string? ContentType { get; private set; }

    // The address actually fetched from, after redirects. Worth keeping: it's
    // what the bytes really came from, which is what a takedown notice will be
    // about.
    public string? ResolvedUrl { get; private set; }

    public static ImageFetchResult Ok(byte[] content, string contentType, string resolvedUrl) =>
        new() { Success = true, Content = content, ContentType = contentType, ResolvedUrl = resolvedUrl };

    public static ImageFetchResult Fail(string message) =>
        new() { Success = false, ErrorMessage = message };
}
