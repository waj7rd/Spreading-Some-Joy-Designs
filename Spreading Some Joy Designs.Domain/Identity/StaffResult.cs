using SpreadingJoy.Domain.Shared;

namespace SpreadingJoy.Domain.Identity;

// Outcome of a staff-account change. Mirrors OrderResult so the controllers all
// read the same way.
public class StaffResult : IOperationResult
{
    public bool Success { get; private set; }

    public string? ErrorMessage { get; private set; }

    public int UserId { get; private set; }

    public static StaffResult Ok(int userId) => new() { Success = true, UserId = userId };

    public static StaffResult Fail(string message) => new() { Success = false, ErrorMessage = message };
}
