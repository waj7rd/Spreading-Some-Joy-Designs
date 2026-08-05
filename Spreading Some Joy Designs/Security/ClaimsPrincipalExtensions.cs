using System.Security.Claims;

namespace SpreadingJoy.Security;

public static class ClaimsPrincipalExtensions
{
    // The signed-in user's id, or null when nobody is signed in.
    //
    // Every action that records who did something needs this, and doing it by
    // hand in each controller invites one of them to parse the wrong claim and
    // attribute an artwork approval to user 0.
    public static int? UserId(this ClaimsPrincipal principal)
    {
        var raw = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(raw, out var id) ? id : null;
    }
}
