namespace SpreadingJoy;

// Named rate-limit policies, referenced from [EnableRateLimiting].
public static class RateLimitPolicies
{
    // The anonymous order request form.
    public const string PublicOrdering = "public-ordering";

    // The sign-in form.
    public const string Login = "login";

    // Pasting an image URL. This one is the most valuable to limit of the three:
    // every request makes our server go and fetch something on the caller's
    // behalf, which is both bandwidth we pay for and the thing an attacker
    // wants to do in a loop while probing an internal network.
    public const string ArtworkFetch = "artwork-fetch";
}
