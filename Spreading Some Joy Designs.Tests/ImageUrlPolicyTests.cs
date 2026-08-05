using System.Net;

namespace SpreadingJoy.Tests;

// The guard around "our server fetches an address a stranger chose".
//
// These are the highest-value tests in the suite: everything else here protects
// the studio from a bad order, and this protects the network the studio is
// hosted on. A regression is invisible from the outside — the site keeps
// working perfectly while becoming a proxy into whatever it can reach.
public class ImageUrlPolicyTests
{
    [Theory]
    [InlineData("https://example.com/cat.png")]
    [InlineData("http://example.com/cat.png")]
    [InlineData("https://cdn.example.com:8443/a/b/c.jpg?v=2")]
    public void Accepts_ordinary_image_urls(string url)
    {
        var error = ImageUrlPolicy.CheckUrl(url, out var parsed);

        Assert.Null(error);
        Assert.NotNull(parsed);
    }

    [Theory]
    [InlineData("file:///C:/Windows/win.ini")]
    [InlineData("ftp://example.com/cat.png")]
    [InlineData("gopher://example.com/1")]
    [InlineData("data:image/png;base64,iVBORw0KGgo=")]
    public void Rejects_schemes_other_than_http_and_https(string url)
    {
        Assert.NotNull(ImageUrlPolicy.CheckUrl(url, out _));
    }

    [Fact]
    public void Rejects_urls_carrying_userinfo()
    {
        // "https://trusted.com@evil.com/" reads as trusted.com and resolves to
        // evil.com. Never present on a real image link.
        var error = ImageUrlPolicy.CheckUrl("https://example.com@evil.test/cat.png", out _);

        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    [InlineData("/relative/path.png")]
    public void Rejects_junk(string url)
    {
        Assert.NotNull(ImageUrlPolicy.CheckUrl(url, out _));
    }

    // ---- the address check ----

    [Theory]
    [InlineData("127.0.0.1")]        // loopback
    [InlineData("127.5.5.5")]        // the whole 127/8 block, not just .0.1
    [InlineData("0.0.0.0")]          // "this host"
    [InlineData("10.0.0.5")]         // RFC 1918
    [InlineData("172.16.0.1")]       // RFC 1918, bottom of the range
    [InlineData("172.31.255.254")]   // RFC 1918, top of the range
    [InlineData("192.168.1.1")]      // RFC 1918
    [InlineData("100.64.0.1")]       // carrier-grade NAT
    [InlineData("169.254.1.1")]      // link-local
    [InlineData("169.254.169.254")]  // cloud instance metadata — the big one
    [InlineData("224.0.0.1")]        // multicast
    [InlineData("255.255.255.255")]  // broadcast
    public void Refuses_private_and_special_ipv4(string address)
    {
        Assert.False(ImageUrlPolicy.IsPubliclyRoutable(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("172.15.0.1")]      // just below the RFC 1918 block
    [InlineData("172.32.0.1")]      // just above it
    [InlineData("100.63.255.255")]  // just below the CGNAT block
    [InlineData("100.128.0.1")]     // just above it
    public void Allows_ordinary_public_ipv4(string address)
    {
        Assert.True(ImageUrlPolicy.IsPubliclyRoutable(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("::1")]                    // loopback
    [InlineData("::")]                     // unspecified
    [InlineData("fe80::1")]                // link-local
    [InlineData("fc00::1")]                // unique local
    [InlineData("fd12:3456:789a::1")]      // unique local
    [InlineData("ff02::1")]                // multicast
    public void Refuses_private_and_special_ipv6(string address)
    {
        Assert.False(ImageUrlPolicy.IsPubliclyRoutable(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("2606:4700:4700::1111")]
    [InlineData("2001:4860:4860::8888")]
    public void Allows_ordinary_public_ipv6(string address)
    {
        Assert.True(ImageUrlPolicy.IsPubliclyRoutable(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("::ffff:169.254.169.254")]
    [InlineData("::ffff:10.0.0.1")]
    public void Refuses_ipv4_addresses_wearing_an_ipv6_hat(string address)
    {
        // Without the mapped-address branch, the v6 path is simply the way
        // around every v4 rule above.
        Assert.False(ImageUrlPolicy.IsPubliclyRoutable(IPAddress.Parse(address)));
    }

    [Fact]
    public void Allows_a_public_ipv4_mapped_into_ipv6()
    {
        Assert.True(ImageUrlPolicy.IsPubliclyRoutable(IPAddress.Parse("::ffff:8.8.8.8")));
    }
}
