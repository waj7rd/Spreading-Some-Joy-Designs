using System.Security.Cryptography;

namespace SpreadingJoy.Domain.Identity;

// PBKDF2-SHA256 password hashing. No external Identity package — just the
// standard .NET crypto primitives. Format: "v1.{iterations}.{saltB64}.{hashB64}",
// so the iteration count travels with the hash and can be bumped later without
// invalidating already-stored hashes.
public static class PasswordHasher
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iterations = 100_000;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);

        return $"v1.{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string storedHash)
    {
        var parts = (storedHash ?? string.Empty).Split('.');
        if (parts.Length != 4 || parts[0] != "v1")
            return false;

        if (!int.TryParse(parts[1], out var iterations))
            return false;

        byte[] salt;
        byte[] expectedHash;

        // A malformed hash is a corrupt row, not an exception worth taking the
        // sign-in page down for. Fail the verification and let the attempt be
        // audited like any other.
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expectedHash = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actualHash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expectedHash.Length);

        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }
}
