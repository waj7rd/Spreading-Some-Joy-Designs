using SpreadingJoy.Tests.Fakes;

namespace SpreadingJoy.Tests;

public class UserLogicTests
{
    private static readonly DateTime Now = new(2026, 8, 4, 10, 0, 0);
    private const string GoodPassword = "correct horse battery";

    private readonly FakeUserRepository _users = new();
    private readonly FakeLoginAuditRepository _audit = new();
    private readonly UserLogic _logic;

    public UserLogicTests()
    {
        _logic = new UserLogic(_users, _audit, new FixedStudioClock(Now));
    }

    // hashCost is deliberately nothing like the real one. These tests are about
    // who may sign in, not about how expensive guessing is, and hashing at the
    // production count turned a three-second suite into a thirty-second one —
    // which is how a suite that gets run constantly stops being one.
    //
    // The count travels inside the hash, so Verify still works against these.
    // Anything that genuinely cares about the cost sets its own hash.
    private User SeedUser(
        string email = "sam@studio.test",
        string role = Roles.Admin,
        bool isActive = true,
        string password = GoodPassword,
        int hashCost = 1_000)
    {
        var user = new User
        {
            UserId = _users.All.Count + 1,
            FullName = "Sam Ortiz",
            Email = email,
            Role = role,
            IsActive = isActive,
            PasswordHash = PasswordHasherTests.HashAtCost(password, hashCost),
            CreatedAt = Now
        };

        _users.Seed(user);
        return user;
    }

    // ---- signing in ----

    [Fact]
    public async Task Correct_credentials_succeed_and_are_audited()
    {
        SeedUser();

        var result = await _logic.AuthenticateAsync("sam@studio.test", GoodPassword, "10.0.0.1");

        Assert.True(result.Succeeded);
        Assert.Equal(LoginAuditEvent.Success, _audit.All.Single().Event);
        Assert.Equal(Now, _users.All.Single().LastLoginAt);
    }

    [Fact]
    public async Task An_unknown_email_reads_the_same_as_a_wrong_password()
    {
        SeedUser();

        var unknown = await _logic.AuthenticateAsync("nobody@studio.test", GoodPassword, null);
        var wrongPassword = await _logic.AuthenticateAsync("sam@studio.test", "wrong", null);

        // Identical outcome, so the login form can't be used to discover which
        // addresses are real.
        Assert.Equal(AuthenticationOutcome.InvalidCredentials, unknown.Outcome);
        Assert.Equal(AuthenticationOutcome.InvalidCredentials, wrongPassword.Outcome);
    }

    [Fact]
    public async Task An_attempt_against_an_unknown_email_is_audited_with_no_user()
    {
        await _logic.AuthenticateAsync("nobody@studio.test", "guess", "10.0.0.1");

        var entry = Assert.Single(_audit.All);
        Assert.Null(entry.UserId);
        Assert.Equal("nobody@studio.test", entry.EmailAttempted);
        Assert.Equal(LoginAuditEvent.Failure, entry.Event);
    }

    // ---- bringing old hashes up to the current cost ----

    [Fact]
    public async Task An_old_hash_is_upgraded_on_a_successful_sign_in()
    {
        var user = SeedUser();
        user.PasswordHash = PasswordHasherTests.HashAtCost(GoodPassword, 1_000);

        var result = await _logic.AuthenticateAsync("sam@studio.test", GoodPassword, null);

        Assert.True(result.Succeeded);

        // Rewritten at the current cost, and still the same password — an
        // upgrade that locked the account out would be worse than none.
        Assert.False(PasswordHasher.NeedsRehash(user.PasswordHash));
        Assert.True(PasswordHasher.Verify(GoodPassword, user.PasswordHash));
    }

    [Fact]
    public async Task A_hash_already_at_the_current_cost_is_left_untouched()
    {
        var user = SeedUser();

        // One of the few places the real cost is the point of the test.
        user.PasswordHash = PasswordHasher.Hash(GoodPassword);
        var before = user.PasswordHash;

        await _logic.AuthenticateAsync("sam@studio.test", GoodPassword, null);

        // Every sign-in writing the row again would be churn for nothing.
        Assert.Equal(before, user.PasswordHash);
    }

    [Fact]
    public async Task A_failed_sign_in_does_not_touch_the_hash()
    {
        var user = SeedUser();
        var before = PasswordHasherTests.HashAtCost(GoodPassword, 1_000);
        user.PasswordHash = before;

        await _logic.AuthenticateAsync("sam@studio.test", "wrong", null);

        // The upgrade rides on knowing the password is right. Rehashing on a
        // failure would mean rehashing whatever the guesser typed.
        Assert.Equal(before, user.PasswordHash);
    }

    // ---- lockout ----

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account()
    {
        SeedUser();

        for (var attempt = 0; attempt < 4; attempt++)
        {
            var interim = await _logic.AuthenticateAsync("sam@studio.test", "wrong", null);
            Assert.Equal(AuthenticationOutcome.InvalidCredentials, interim.Outcome);
        }

        var fifth = await _logic.AuthenticateAsync("sam@studio.test", "wrong", null);

        Assert.Equal(AuthenticationOutcome.LockedOut, fifth.Outcome);
        Assert.NotNull(_users.All.Single().LockedOutUntil);
    }

    [Fact]
    public async Task The_right_password_during_a_lockout_still_fails()
    {
        var user = SeedUser();
        user.LockedOutUntil = Now.AddMinutes(10);

        var result = await _logic.AuthenticateAsync("sam@studio.test", GoodPassword, null);

        Assert.Equal(AuthenticationOutcome.LockedOut, result.Outcome);
    }

    [Fact]
    public async Task An_expired_lockout_no_longer_blocks()
    {
        var user = SeedUser();
        user.LockedOutUntil = Now.AddMinutes(-1);

        var result = await _logic.AuthenticateAsync("sam@studio.test", GoodPassword, null);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task A_successful_sign_in_clears_the_failure_count()
    {
        SeedUser();

        await _logic.AuthenticateAsync("sam@studio.test", "wrong", null);
        await _logic.AuthenticateAsync("sam@studio.test", "wrong", null);
        await _logic.AuthenticateAsync("sam@studio.test", GoodPassword, null);

        Assert.Equal(0, _users.All.Single().FailedLoginCount);
    }

    [Fact]
    public async Task A_deactivated_account_is_only_told_apart_after_the_password_is_right()
    {
        // Checking IsActive first would let somebody guessing distinguish a
        // disabled account from a wrong password — which confirms the address.
        SeedUser(isActive: false);

        var wrongPassword = await _logic.AuthenticateAsync("sam@studio.test", "wrong", null);
        Assert.Equal(AuthenticationOutcome.InvalidCredentials, wrongPassword.Outcome);

        var rightPassword = await _logic.AuthenticateAsync("sam@studio.test", GoodPassword, null);
        Assert.Equal(AuthenticationOutcome.Inactive, rightPassword.Outcome);
    }

    // ---- managing accounts ----

    [Fact]
    public async Task The_last_active_admin_cannot_be_deactivated()
    {
        var admin = SeedUser(role: Roles.Admin);
        SeedUser(email: "pat@studio.test", role: Roles.Manager);

        var result = await _logic.SetActiveAsync(admin.UserId, false);

        Assert.False(result.Success);
        Assert.True(_users.All.First().IsActive);
    }

    [Fact]
    public async Task The_last_active_admin_cannot_be_demoted()
    {
        var admin = SeedUser(role: Roles.Admin);

        var result = await _logic.UpdateStaffAsync(admin.UserId, "Sam Ortiz", "sam@studio.test", Roles.Manager);

        Assert.False(result.Success);
        Assert.Equal(Roles.Admin, _users.All.Single().Role);
    }

    [Fact]
    public async Task An_admin_can_be_deactivated_when_another_one_remains()
    {
        var first = SeedUser(role: Roles.Admin);
        SeedUser(email: "pat@studio.test", role: Roles.Admin);

        var result = await _logic.SetActiveAsync(first.UserId, false);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task A_deactivated_admin_does_not_count_as_the_remaining_one()
    {
        var active = SeedUser(role: Roles.Admin);
        SeedUser(email: "pat@studio.test", role: Roles.Admin, isActive: false);

        var result = await _logic.SetActiveAsync(active.UserId, false);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task Deactivating_clears_any_lockout()
    {
        var admin = SeedUser(role: Roles.Admin);
        var target = SeedUser(email: "pat@studio.test", role: Roles.Associate);
        target.LockedOutUntil = Now.AddMinutes(10);
        target.FailedLoginCount = 5;

        await _logic.SetActiveAsync(target.UserId, false);

        Assert.Null(target.LockedOutUntil);
        Assert.Equal(0, target.FailedLoginCount);
    }

    [Fact]
    public async Task A_duplicate_email_is_refused()
    {
        SeedUser();

        var result = await _logic.CreateStaffAsync("Pat Lee", "sam@studio.test", Roles.Associate, "another password");

        Assert.False(result.Success);
        Assert.Single(_users.All);
    }

    [Fact]
    public async Task An_unknown_role_is_refused()
    {
        var result = await _logic.CreateStaffAsync("Pat Lee", "pat@studio.test", "Supervisor", "a password");

        Assert.False(result.Success);
        Assert.Empty(_users.All);
    }

    [Fact]
    public async Task Changing_your_own_password_requires_the_current_one()
    {
        var user = SeedUser();

        var wrong = await _logic.ChangeOwnPasswordAsync(user.UserId, "not it", "a new password", null);
        Assert.False(wrong.Success);

        var right = await _logic.ChangeOwnPasswordAsync(user.UserId, GoodPassword, "a new password", null);
        Assert.True(right.Success);

        var signIn = await _logic.AuthenticateAsync("sam@studio.test", "a new password", null);
        Assert.True(signIn.Succeeded);
    }

    [Fact]
    public async Task An_admin_reset_clears_the_lockout_too()
    {
        var user = SeedUser();
        user.LockedOutUntil = Now.AddMinutes(10);
        user.FailedLoginCount = 5;

        await _logic.SetPasswordAsync(user.UserId, "a fresh password");

        Assert.Null(user.LockedOutUntil);
        Assert.Equal(0, user.FailedLoginCount);

        var signIn = await _logic.AuthenticateAsync("sam@studio.test", "a fresh password", null);
        Assert.True(signIn.Succeeded);
    }
}
