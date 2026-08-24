using System.Security.Cryptography;

namespace SpreadingJoy.Tests;

// The hashing itself is standard library work and not worth testing. What is
// worth testing is NeedsRehash, because it is the piece that decides whether
// raising the iteration count reaches anybody who already has an account.
public class PasswordHasherTests
{
    // A hash in the stored format at an arbitrary cost, standing in for one
    // written before the count went up. Built here rather than by calling
    // Hash(), which only ever writes the current figure.
    internal static string HashAtCost(string password, int iterations)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 32);

        return $"v1.{iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    [Fact]
    public void A_hash_made_with_less_work_than_we_now_do_needs_rehashing()
    {
        Assert.True(PasswordHasher.NeedsRehash(HashAtCost("correct horse battery", 100_000)));
    }

    [Fact]
    public void A_hash_made_at_the_current_cost_is_left_alone()
    {
        // Otherwise every sign-in rewrites a row that did not need rewriting.
        Assert.False(PasswordHasher.NeedsRehash(PasswordHasher.Hash("correct horse battery")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a hash")]
    [InlineData("v1.notanumber.c2FsdA==.aGFzaA==")]
    [InlineData("v2.600000.c2FsdA==.aGFzaA==")]
    public void Anything_unreadable_or_from_another_format_needs_rehashing(string stored)
    {
        // Erring towards replacing it: the alternative is leaving a row we
        // cannot reason about sitting there indefinitely.
        Assert.True(PasswordHasher.NeedsRehash(stored));
    }

    [Fact]
    public void A_hash_still_verifies_at_the_cost_it_was_made_with()
    {
        // The whole point of carrying the count inside the hash. If raising
        // Iterations invalidated stored hashes, nobody could sign in afterwards
        // and the upgrade path would be a lockout.
        var legacy = HashAtCost("correct horse battery", 1_000);

        Assert.True(PasswordHasher.Verify("correct horse battery", legacy));
        Assert.False(PasswordHasher.Verify("wrong", legacy));
    }
}
