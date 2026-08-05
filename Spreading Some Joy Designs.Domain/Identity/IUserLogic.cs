using SpreadingJoy.Domain.EntityModels;

namespace SpreadingJoy.Domain.Identity;

// Business-logic contract for staff accounts: signing in, and managing who has
// an account at all.
public interface IUserLogic
{
    // Checks credentials and applies lockout rules. Records the attempt either
    // way. ipAddress goes on the audit row and may be null.
    Task<AuthenticationResult> AuthenticateAsync(string email, string password, string? ipAddress);

    // Records a sign-out. Nothing else to do — the cookie is the session.
    Task RecordLogoutAsync(int userId, string email, string? ipAddress);

    // Every staff account, active and inactive.
    Task<IList<User>> GetStaffAsync();

    Task<User?> GetByIdAsync(int userId);

    // Fails if the email is already taken.
    Task<StaffResult> CreateStaffAsync(string fullName, string email, string role, string password);

    // Name, email and role. Password is changed separately.
    Task<StaffResult> UpdateStaffAsync(int userId, string fullName, string email, string role);

    // Deactivating clears any lockout — there's nothing left to lock out of.
    // Refuses to deactivate the last active Admin.
    Task<StaffResult> SetActiveAsync(int userId, bool isActive);

    // An Admin resetting somebody else's password. Clears lockout too.
    Task<StaffResult> SetPasswordAsync(int userId, string newPassword);

    // A user changing their own password; requires the current one.
    Task<StaffResult> ChangeOwnPasswordAsync(int userId, string currentPassword, string newPassword, string? ipAddress);

    // Clears a lockout without touching the password.
    Task<StaffResult> UnlockAsync(int userId);

    Task<IList<LoginAudit>> GetRecentLoginActivityAsync(int count);
}
