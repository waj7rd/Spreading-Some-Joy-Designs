using SpreadingJoy.Domain.EntityModels;

namespace SpreadingJoy.Domain.Shared;

// How the studio runs: contact details, hours, capacity, turnaround.
//
// Note what isn't here: the tier. A studio changing its own tier is a studio
// giving itself features it hasn't paid for, so the operation doesn't exist —
// not hidden in the UI, absent from the contract.
public interface IStudioLogic
{
    Task<Studio?> GetAsync();

    Task<StudioResult> UpdateAsync(
        string name,
        string? phone,
        string? email,
        string? addressLine,
        string? city,
        string? state,
        string? postalCode,
        string timeZoneId,
        int dailyPrintCapacity,
        int turnaroundDays,
        IReadOnlyCollection<DayOfWeek> closedDays);
}

public class StudioResult : IOperationResult
{
    public bool Success { get; private set; }
    public string? ErrorMessage { get; private set; }

    public static StudioResult Ok() => new() { Success = true };

    public static StudioResult Fail(string message) => new() { Success = false, ErrorMessage = message };
}
