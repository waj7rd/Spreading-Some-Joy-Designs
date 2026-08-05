namespace SpreadingJoy.Domain.Shared;

// The studio's clock, not the server's.
//
// Due dates are wall-clock dates where the press is. Hosted on a UTC machine,
// DateTime.Now would reject a same-day order as being in the past for several
// hours every evening — and would do it only in production, which is the worst
// possible place to find out.
public interface IStudioClock
{
    TimeZoneInfo TimeZone { get; }

    DateTime UtcNow { get; }

    // Studio-local wall clock. Kind is Unspecified on purpose: it is a local
    // time at a place, and letting it claim to be Local would invite a
    // conversion against the server's zone somewhere downstream.
    DateTime LocalNow { get; }

    // Today at the studio. Almost every rule in the ordering logic wants this
    // rather than the full timestamp.
    DateTime Today => LocalNow.Date;
}

// A fixed clock for tests: no timezone database, no ambient time, no flake at
// midnight or on the last day of a month.
public class FixedStudioClock : IStudioClock
{
    public FixedStudioClock(DateTime localNow, TimeZoneInfo? timeZone = null)
    {
        LocalNow = DateTime.SpecifyKind(localNow, DateTimeKind.Unspecified);
        TimeZone = timeZone ?? TimeZoneInfo.Utc;
    }

    public TimeZoneInfo TimeZone { get; }

    public DateTime LocalNow { get; }

    public DateTime UtcNow => TimeZoneInfo.ConvertTimeToUtc(LocalNow, TimeZone);
}
