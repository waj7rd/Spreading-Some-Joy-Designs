using SpreadingJoy.Domain.EntityModels;

namespace SpreadingJoy.Domain.Identity;

public enum AuthenticationOutcome
{
    Success,

    // Wrong email or wrong password. Deliberately one outcome for both, so the
    // login page can't be used to find out which addresses are real.
    InvalidCredentials,

    // Too many failed attempts; try again after LockedOutUntil.
    LockedOut,

    // Correct password, but the account has been deactivated.
    Inactive
}

// Outcome of a sign-in attempt.
public class AuthenticationResult
{
    public AuthenticationOutcome Outcome { get; private set; }

    // Only set when Outcome is Success.
    public User? User { get; private set; }

    // Only set when Outcome is LockedOut. UTC.
    public DateTime? LockedOutUntil { get; private set; }

    public bool Succeeded => Outcome == AuthenticationOutcome.Success;

    public static AuthenticationResult Success(User user) =>
        new() { Outcome = AuthenticationOutcome.Success, User = user };

    public static AuthenticationResult InvalidCredentials() =>
        new() { Outcome = AuthenticationOutcome.InvalidCredentials };

    public static AuthenticationResult LockedOut(DateTime until) =>
        new() { Outcome = AuthenticationOutcome.LockedOut, LockedOutUntil = until };

    public static AuthenticationResult Inactive() =>
        new() { Outcome = AuthenticationOutcome.Inactive };
}
