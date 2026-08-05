using System.Net;
using System.Net.Sockets;

namespace SpreadingJoy.Domain.Artworks;

// Which URLs the server is willing to go and fetch.
//
// This exists because "paste a link to your picture" means a stranger chooses
// an address that our server, inside our network, then makes a request to. Left
// open, that turns the artwork box into a proxy for reaching things only we can
// reach: a cloud instance's metadata endpoint, an admin panel on localhost, a
// database on a private subnet. The customer never sees the response, but a
// difference between "fetch failed" and "fetch succeeded" is enough to map a
// network with.
//
// Kept as pure functions here, deliberately separate from the code that does
// the fetching, because these are the rules worth having tests for. The DAL
// resolves DNS and then asks these questions about every address it got back.
public static class ImageUrlPolicy
{
    // No FTP, no file://, no gopher. http is allowed alongside https because
    // plenty of older image hosts still don't redirect properly, and the
    // content is public either way.
    private static readonly string[] AllowedSchemes = ["http", "https"];

    // Enough to follow a CDN shuffle, few enough that a redirect loop dies
    // quickly. Each hop is re-checked — a public URL that 302s to 169.254.169.254
    // is the entire trick, and checking only the first address defeats the point.
    public const int MaxRedirects = 3;

    public static string? CheckUrl(string? url, out Uri? parsed)
    {
        parsed = null;

        if (string.IsNullOrWhiteSpace(url))
            return "Paste the address of an image.";

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            return "That doesn't look like a web address.";

        if (!AllowedSchemes.Contains(uri.Scheme, StringComparer.OrdinalIgnoreCase))
            return "Only http and https addresses work here.";

        // A userinfo section ("https://user:pass@host/") is never present on a
        // legitimate image link and is a well-worn way to make a URL read as
        // one host while resolving to another.
        if (!string.IsNullOrEmpty(uri.UserInfo))
            return "That address has a username in it, which we don't accept.";

        parsed = uri;
        return null;
    }

    // Whether an address the DNS lookup returned is one we'll actually connect
    // to. Called for every address of every hop.
    //
    // Default-deny: an address family or range this doesn't recognise is
    // refused rather than allowed. The cost of being wrong in the permissive
    // direction is a server-side request forgery; in the restrictive direction
    // it's one unusual image host that has to be uploaded instead.
    public static bool IsPubliclyRoutable(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
            return false;

        if (address.AddressFamily == AddressFamily.InterNetwork)
            return IsPublicV4(address);

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
            return IsPublicV6(address);

        return false;
    }

    private static bool IsPublicV4(IPAddress address)
    {
        var b = address.GetAddressBytes();

        return (b[0], b[1]) switch
        {
            // This network / "this host on this network" — RFC 1122.
            (0, _) => false,

            // Private ranges — RFC 1918.
            (10, _) => false,
            (172, >= 16 and <= 31) => false,
            (192, 168) => false,

            // Carrier-grade NAT — RFC 6598. Shared address space, and reachable
            // inside plenty of hosting environments.
            (100, >= 64 and <= 127) => false,

            // Link-local — RFC 3927. Includes 169.254.169.254, the cloud
            // instance metadata address, which is the single most valuable
            // target this whole function exists to protect.
            (169, 254) => false,

            // Loopback, already covered by IsLoopback but stated for the range.
            (127, _) => false,

            // IETF protocol assignments and the various test/benchmark nets.
            (192, 0) => false,
            (198, >= 18 and <= 19) => false,
            (198, 51) => false,
            (203, 0) => false,

            // Multicast, reserved, and broadcast.
            (>= 224, _) => false,

            _ => true
        };
    }

    private static bool IsPublicV6(IPAddress address)
    {
        if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast)
            return false;

        // An IPv4 address wearing an IPv6 hat. ::ffff:127.0.0.1 is loopback and
        // has to be judged as such, or the v6 path becomes the way around the
        // v4 rules.
        if (address.IsIPv4MappedToIPv6)
            return IsPublicV4(address.MapToIPv4());

        var b = address.GetAddressBytes();

        // Unique local addresses, fc00::/7 — the v6 equivalent of RFC 1918.
        if ((b[0] & 0xFE) == 0xFC)
            return false;

        // Unspecified (::) and loopback (::1).
        if (b.Take(15).All(x => x == 0) && b[15] <= 1)
            return false;

        return true;
    }
}
