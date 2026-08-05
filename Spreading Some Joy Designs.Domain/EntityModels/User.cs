namespace SpreadingJoy.Domain.EntityModels;

// A member of studio staff. No ASP.NET Core Identity — this table plus
// PasswordHasher is the whole of it.
//
// Accounts are deactivated rather than deleted, so the artwork someone approved
// and the requests they handled keep resolving to a real person.
public partial class User
{
    public int UserId { get; set; }

    public string FullName { get; set; } = null!;

    public string Email { get; set; } = null!;

    public string PasswordHash { get; set; } = null!;

    public string Role { get; set; } = Identity.Roles.Associate;

    public bool IsActive { get; set; } = true;

    public int FailedLoginCount { get; set; }

    public DateTime? LockedOutUntil { get; set; }

    public DateTime? LastLoginAt { get; set; }

    public DateTime CreatedAt { get; set; }
}
