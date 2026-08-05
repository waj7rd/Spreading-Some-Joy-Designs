using System.Net;
using System.Net.Sockets;
using SpreadingJoy.Domain.Artworks;

namespace SpreadingJoy.DAL.Imaging;

// Fetches the bytes behind a customer-supplied URL.
//
// The whole of this class is the answer to one question: our server is about to
// make an HTTP request to an address a stranger chose. Every piece of it exists
// because of something that can be done with that.
//
//   - Redirects are followed by hand, not by HttpClient, because the handler
//     checks the first address and then happily follows a 302 to anywhere. A
//     public URL that redirects to 169.254.169.254 is the entire attack.
//   - Every hop resolves DNS itself and checks every address that comes back,
//     since a hostname can resolve to several and only one needs to be internal.
//   - The response is read with a hard ceiling rather than trusting
//     Content-Length, which is a claim made by the server we're defending
//     against.
//   - Timeouts are short. A URL that hangs is a way to tie up request threads.
//
// What it deliberately does not do is guarantee the address it checked is the
// address it then connects to. Between the DNS lookup and the socket opening,
// a hostile DNS server can answer differently — the DNS-rebinding race. Closing
// that means connecting to the validated IP directly and carrying the hostname
// in the Host header, which breaks TLS certificate validation and virtual
// hosting. The mitigation here is that the response body never reaches the
// customer: they see "that didn't work", not what was at the address. Worth
// knowing about before this is exposed to real traffic.
public class HttpImageFetcher : IImageFetcher
{
    public const string HttpClientName = "artwork-fetch";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly IHttpClientFactory _httpClientFactory;

    public HttpImageFetcher(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<ImageFetchResult> FetchAsync(string url, CancellationToken cancellationToken = default)
    {
        var urlError = ImageUrlPolicy.CheckUrl(url, out var uri);
        if (urlError != null)
            return ImageFetchResult.Fail(urlError);

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(Timeout);

        var client = _httpClientFactory.CreateClient(HttpClientName);
        var current = uri!;

        try
        {
            for (var hop = 0; hop <= ImageUrlPolicy.MaxRedirects; hop++)
            {
                var addressError = await CheckAddressesAsync(current, timeoutSource.Token);
                if (addressError != null)
                    return ImageFetchResult.Fail(addressError);

                using var request = new HttpRequestMessage(HttpMethod.Get, current);
                using var response = await client.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead, timeoutSource.Token);

                if (IsRedirect(response.StatusCode))
                {
                    var location = response.Headers.Location;
                    if (location == null)
                        return ImageFetchResult.Fail("That address redirected somewhere we couldn't follow.");

                    // Relative Location headers are legal and common.
                    current = location.IsAbsoluteUri ? location : new Uri(current, location);

                    // Re-run the full URL policy on the new target: the first
                    // hop being https says nothing about the second one not
                    // being file:// or http://localhost.
                    var hopError = ImageUrlPolicy.CheckUrl(current.ToString(), out var hopUri);
                    if (hopError != null)
                        return ImageFetchResult.Fail("That address redirected somewhere we don't accept.");

                    current = hopUri!;
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                    return ImageFetchResult.Fail($"That address returned {(int)response.StatusCode}. Check the link.");

                var contentType = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant();
                if (contentType == null || !ImageLimits.AllowedContentTypes.Contains(contentType))
                {
                    return ImageFetchResult.Fail(
                        "That link doesn't point at an image file. Right-click the picture itself and copy its " +
                        "image address, rather than copying the address of the page it's on.");
                }

                // Checked before reading so an enormous file is refused rather
                // than downloaded and then refused. Not trusted — the read below
                // enforces the same ceiling on the actual bytes.
                if (response.Content.Headers.ContentLength > ImageLimits.MaxBytes)
                    return ImageFetchResult.Fail($"That image is over {ImageLimits.MaxBytes / (1024 * 1024)}MB.");

                var content = await ReadCappedAsync(response, timeoutSource.Token);
                if (content == null)
                    return ImageFetchResult.Fail($"That image is over {ImageLimits.MaxBytes / (1024 * 1024)}MB.");

                return ImageFetchResult.Ok(content, contentType, current.ToString());
            }

            return ImageFetchResult.Fail("That address redirected too many times.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ImageFetchResult.Fail("That address took too long to respond.");
        }
        catch (HttpRequestException)
        {
            // Deliberately vague. The difference between "connection refused"
            // and "timed out" is exactly the signal that makes an open fetcher
            // useful for mapping an internal network.
            return ImageFetchResult.Fail("We couldn't reach that address.");
        }
    }

    // Resolves the host and refuses if any address it answers with is one we
    // shouldn't be connecting to. Any, not all: a hostname that returns one
    // public and one private address is the standard way around a check that
    // only looks at the first.
    private static async Task<string?> CheckAddressesAsync(Uri uri, CancellationToken cancellationToken)
    {
        IPAddress[] addresses;

        // A literal IP in the URL needs no lookup — and must not get one, since
        // resolving "127.0.0.1" as a hostname would fail and skip the check.
        if (IPAddress.TryParse(uri.Host.Trim('[', ']'), out var literal))
        {
            addresses = [literal];
        }
        else
        {
            try
            {
                addresses = await Dns.GetHostAddressesAsync(uri.Host, cancellationToken);
            }
            catch (SocketException)
            {
                return "We couldn't find that address.";
            }
        }

        if (addresses.Length == 0)
            return "We couldn't find that address.";

        if (addresses.Any(a => !ImageUrlPolicy.IsPubliclyRoutable(a)))
            return "That address isn't one we can fetch from. Try uploading the image instead.";

        return null;
    }

    // Reads the body with a hard ceiling, so a server that lies about
    // Content-Length — or omits it — can't stream until we run out of memory.
    private static async Task<byte[]?> ReadCappedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();

        var chunk = new byte[81920];
        int read;

        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > ImageLimits.MaxBytes)
                return null;

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static bool IsRedirect(HttpStatusCode status) =>
        status is HttpStatusCode.MovedPermanently
            or HttpStatusCode.Found
            or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;
}
