namespace SpreadingJoy.Tests.Fakes;

// Stand-in for the HTTP fetcher, so the artwork rules can be tested without a
// network. Records what it was asked for, answers with whatever the test set up.
public class FakeImageFetcher : IImageFetcher
{
    private readonly Func<string, ImageFetchResult> _respond;

    public FakeImageFetcher(Func<string, ImageFetchResult> respond) => _respond = respond;

    public static FakeImageFetcher Returning(byte[] content, string contentType = "image/png") =>
        new(url => ImageFetchResult.Ok(content, contentType, url));

    public static FakeImageFetcher Failing(string message) =>
        new(_ => ImageFetchResult.Fail(message));

    public List<string> RequestedUrls { get; } = new();

    public Task<ImageFetchResult> FetchAsync(string url, CancellationToken cancellationToken = default)
    {
        RequestedUrls.Add(url);
        return Task.FromResult(_respond(url));
    }
}

// Stand-in for the decoder. Returns whatever the test declares the bytes to be,
// which is the point: the real inspector's job is answering that question, and
// the logic layer's job is what it does with the answer.
public class FakeImageInspector : IImageInspector
{
    private readonly InspectedImage? _result;

    public FakeImageInspector(InspectedImage? result) => _result = result;

    public static FakeImageInspector Rejecting() => new(null);

    public static FakeImageInspector Returning(int widthPx, int heightPx, string contentType = "image/png") =>
        new(new InspectedImage(contentType, contentType.Split('/')[1], widthPx, heightPx));

    public int NormaliseCount { get; private set; }

    public InspectedImage? Inspect(byte[] content) => _result;

    public byte[] Normalise(byte[] content, InspectedImage info)
    {
        NormaliseCount++;

        // Returns something different from the input on purpose, so a test that
        // accidentally hashes the original rather than the normalised bytes
        // shows up as a different hash.
        return content.Concat(new byte[] { 0xFF }).ToArray();
    }
}

// Stand-in for the file store. Keeps bytes in a dictionary.
public class FakeImageStore : IImageStore
{
    private readonly Dictionary<string, byte[]> _files = new();

    public IReadOnlyDictionary<string, byte[]> Files => _files;

    public Task<string> SaveAsync(byte[] content, string extension, string sha256, CancellationToken cancellationToken = default)
    {
        var name = $"{sha256}.{extension.TrimStart('.')}";
        _files[name] = content;
        return Task.FromResult(name);
    }

    public Task<byte[]?> ReadAsync(string storedFileName, CancellationToken cancellationToken = default) =>
        Task.FromResult(_files.TryGetValue(storedFileName, out var bytes) ? bytes : null);

    public Task DeleteAsync(string storedFileName, CancellationToken cancellationToken = default)
    {
        _files.Remove(storedFileName);
        return Task.CompletedTask;
    }

    public string PublicPath(string storedFileName) => $"/artwork/file/{storedFileName}";
}
