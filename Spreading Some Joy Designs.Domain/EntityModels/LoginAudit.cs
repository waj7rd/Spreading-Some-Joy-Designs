namespace SpreadingJoy.Domain.EntityModels;

// Every sign-in attempt, successful or not.
//
// UserId is nullable because an attempt against an unknown address has no user
// to point at — and those are exactly the rows worth keeping, since a run of
// them is what someone probing for accounts looks like.
public partial class LoginAudit
{
    public int LoginAuditId { get; set; }

    public int? UserId { get; set; }

    // Recorded as typed. The user may not exist, and if it does, this is the
    // evidence of which address was tried rather than which account was found.
    public string EmailAttempted { get; set; } = null!;

    public string Event { get; set; } = null!;

    public string? IpAddress { get; set; }

    public DateTime OccurredAt { get; set; }

    public virtual User? User { get; set; }
}

// What happened. Note there's no separate "unknown email" event: a failed
// attempt is a failed attempt, whether or not the address exists. Splitting
// them would put the answer to "is this a real account?" in a log that gets
// read out loud in support calls.
public static class LoginAuditEvent
{
    public const string Success = "Success";
    public const string Failure = "Failure";
    public const string LockedOut = "LockedOut";
    public const string Inactive = "Inactive";
    public const string Logout = "Logout";
    public const string PasswordChanged = "PasswordChanged";
}
