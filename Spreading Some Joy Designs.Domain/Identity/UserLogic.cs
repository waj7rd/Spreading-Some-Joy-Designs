using SpreadingJoy.Domain.EntityModels;
using SpreadingJoy.Domain.IRepositories;
using SpreadingJoy.Domain.Shared;

namespace SpreadingJoy.Domain.Identity;

public class UserLogic : IUserLogic
{
    // Five wrong guesses buys a fifteen-minute wait. Enough to make online
    // guessing pointless, without locking an associate out of their own studio
    // for the afternoon because they fat-fingered it twice.
    private const int MaxFailedAttempts = 5;
    private const int LockoutMinutes = 15;

    private readonly IUserRepository _userRepository;
    private readonly ILoginAuditRepository _auditRepository;
    private readonly IStudioClock _clock;

    public UserLogic(IUserRepository userRepository, ILoginAuditRepository auditRepository, IStudioClock clock)
    {
        _userRepository = userRepository;
        _auditRepository = auditRepository;
        _clock = clock;
    }

    public async Task<AuthenticationResult> AuthenticateAsync(string email, string password, string? ipAddress)
    {
        email = (email ?? string.Empty).Trim();

        var user = await _userRepository.GetByEmailAsync(email);

        // Unknown email. Audited with no UserId — those are the rows that show
        // someone probing. Reported as InvalidCredentials so the login form
        // can't be used to discover which addresses are real.
        if (user == null)
        {
            await AuditAsync(null, email, LoginAuditEvent.Failure, ipAddress);
            return AuthenticationResult.InvalidCredentials();
        }

        if (user.LockedOutUntil.HasValue && user.LockedOutUntil.Value > _clock.UtcNow)
        {
            await AuditAsync(user.UserId, email, LoginAuditEvent.LockedOut, ipAddress);
            return AuthenticationResult.LockedOut(user.LockedOutUntil.Value);
        }

        if (!PasswordHasher.Verify(password, user.PasswordHash))
        {
            user.FailedLoginCount++;

            var justLocked = user.FailedLoginCount >= MaxFailedAttempts;
            if (justLocked)
            {
                user.LockedOutUntil = _clock.UtcNow.AddMinutes(LockoutMinutes);
                user.FailedLoginCount = 0;
            }

            await _userRepository.SaveChangesAsync();
            await AuditAsync(user.UserId, email,
                justLocked ? LoginAuditEvent.LockedOut : LoginAuditEvent.Failure, ipAddress);

            return justLocked
                ? AuthenticationResult.LockedOut(user.LockedOutUntil!.Value)
                : AuthenticationResult.InvalidCredentials();
        }

        // The password is right. Only check activation now, so that someone
        // guessing can't tell a deactivated account from a wrong password.
        if (!user.IsActive)
        {
            await AuditAsync(user.UserId, email, LoginAuditEvent.Inactive, ipAddress);
            return AuthenticationResult.Inactive();
        }

        user.FailedLoginCount = 0;
        user.LockedOutUntil = null;
        user.LastLoginAt = _clock.UtcNow;
        await _userRepository.SaveChangesAsync();

        await AuditAsync(user.UserId, email, LoginAuditEvent.Success, ipAddress);
        return AuthenticationResult.Success(user);
    }

    public async Task RecordLogoutAsync(int userId, string email, string? ipAddress)
    {
        await AuditAsync(userId, email, LoginAuditEvent.Logout, ipAddress);
    }

    public async Task<IList<User>> GetStaffAsync()
    {
        var all = await _userRepository.GetAllAsync();
        return all.OrderByDescending(u => u.IsActive).ThenBy(u => u.FullName).ToList();
    }

    public async Task<User?> GetByIdAsync(int userId)
    {
        return await _userRepository.GetAsync(u => u.UserId == userId);
    }

    public async Task<StaffResult> CreateStaffAsync(string fullName, string email, string role, string password)
    {
        email = (email ?? string.Empty).Trim();

        if (!Roles.All.Contains(role))
            return StaffResult.Fail("Unknown role.");

        var existing = await _userRepository.GetByEmailAsync(email);
        if (existing != null)
            return StaffResult.Fail("An account with that email already exists.");

        var user = new User
        {
            FullName = fullName.Trim(),
            Email = email,
            Role = role,
            PasswordHash = PasswordHasher.Hash(password),
            IsActive = true,
            CreatedAt = _clock.UtcNow
        };

        await _userRepository.AddAsync(user);
        await _userRepository.SaveChangesAsync();

        return StaffResult.Ok(user.UserId);
    }

    public async Task<StaffResult> UpdateStaffAsync(int userId, string fullName, string email, string role)
    {
        email = (email ?? string.Empty).Trim();

        if (!Roles.All.Contains(role))
            return StaffResult.Fail("Unknown role.");

        var user = await _userRepository.GetAsync(u => u.UserId == userId);
        if (user == null)
            return StaffResult.Fail("Staff member not found.");

        var clash = await _userRepository.GetAsync(u => u.Email == email && u.UserId != userId);
        if (clash != null)
            return StaffResult.Fail("Another account already uses that email.");

        // Demoting the last Admin would leave nobody able to manage accounts.
        if (user.Role == Roles.Admin && role != Roles.Admin && await IsLastActiveAdminAsync(user))
            return StaffResult.Fail("This is the only active Admin — promote someone else first.");

        user.FullName = fullName.Trim();
        user.Email = email;
        user.Role = role;

        await _userRepository.SaveChangesAsync();
        return StaffResult.Ok(user.UserId);
    }

    public async Task<StaffResult> SetActiveAsync(int userId, bool isActive)
    {
        var user = await _userRepository.GetAsync(u => u.UserId == userId);
        if (user == null)
            return StaffResult.Fail("Staff member not found.");

        if (!isActive && user.Role == Roles.Admin && await IsLastActiveAdminAsync(user))
            return StaffResult.Fail("This is the only active Admin — you'd lock everyone out.");

        user.IsActive = isActive;

        if (!isActive)
        {
            // Nothing left to be locked out of.
            user.LockedOutUntil = null;
            user.FailedLoginCount = 0;
        }

        await _userRepository.SaveChangesAsync();
        return StaffResult.Ok(user.UserId);
    }

    public async Task<StaffResult> SetPasswordAsync(int userId, string newPassword)
    {
        var user = await _userRepository.GetAsync(u => u.UserId == userId);
        if (user == null)
            return StaffResult.Fail("Staff member not found.");

        user.PasswordHash = PasswordHasher.Hash(newPassword);
        user.LockedOutUntil = null;
        user.FailedLoginCount = 0;

        await _userRepository.SaveChangesAsync();
        return StaffResult.Ok(user.UserId);
    }

    public async Task<StaffResult> ChangeOwnPasswordAsync(int userId, string currentPassword, string newPassword, string? ipAddress)
    {
        var user = await _userRepository.GetAsync(u => u.UserId == userId);
        if (user == null)
            return StaffResult.Fail("Staff member not found.");

        if (!PasswordHasher.Verify(currentPassword, user.PasswordHash))
            return StaffResult.Fail("Current password is incorrect.");

        user.PasswordHash = PasswordHasher.Hash(newPassword);
        await _userRepository.SaveChangesAsync();

        await AuditAsync(user.UserId, user.Email, LoginAuditEvent.PasswordChanged, ipAddress);
        return StaffResult.Ok(user.UserId);
    }

    public async Task<StaffResult> UnlockAsync(int userId)
    {
        var user = await _userRepository.GetAsync(u => u.UserId == userId);
        if (user == null)
            return StaffResult.Fail("Staff member not found.");

        user.LockedOutUntil = null;
        user.FailedLoginCount = 0;

        await _userRepository.SaveChangesAsync();
        return StaffResult.Ok(user.UserId);
    }

    public async Task<IList<LoginAudit>> GetRecentLoginActivityAsync(int count)
    {
        return await _auditRepository.GetRecentAsync(count);
    }

    // True when this user is the only active Admin left.
    private async Task<bool> IsLastActiveAdminAsync(User user)
    {
        var otherAdmins = await _userRepository.FindByAsync(u =>
            u.Role == Roles.Admin && u.IsActive && u.UserId != user.UserId);

        return otherAdmins.Count == 0;
    }

    private async Task AuditAsync(int? userId, string email, string @event, string? ipAddress)
    {
        await _auditRepository.AddAsync(new LoginAudit
        {
            UserId = userId,
            EmailAttempted = email,
            Event = @event,
            IpAddress = ipAddress,
            OccurredAt = _clock.UtcNow
        });

        await _auditRepository.SaveChangesAsync();
    }
}
