using SpreadingJoy.Domain.EntityModels;
using SpreadingJoy.Domain.Licensing;

namespace SpreadingJoy.Domain.Shared;

// Supplies the studio record that everything else reads its settings from.
//
// Cached rather than fetched per request: the row changes when someone saves
// the settings screen, which is roughly never, and a database round trip on
// every capacity check to re-read the same three values would be wasteful.
// Whoever writes the record calls Reload.
//
// When this becomes multi-tenant, this is the seam — Current stops meaning "the
// studio" and starts meaning "the studio this request belongs to", and the
// cache becomes keyed rather than singular.
public interface IStudioContext
{
    Studio Current { get; }

    void Reload();
}

// IStudioSettings and IFeatureFlags both read from the studio record, so
// they're thin adapters over it rather than separate sources of truth.
// Registered against the same cached context, which is why a settings change
// takes effect everywhere at once.
public class StudioSettingsFromContext : IStudioSettings
{
    private readonly IStudioContext _context;

    public StudioSettingsFromContext(IStudioContext context) => _context = context;

    public int DailyPrintCapacity => _context.Current.DailyPrintCapacity;
    public int TurnaroundDays => _context.Current.TurnaroundDays;
    public IReadOnlyCollection<DayOfWeek> ClosedDays => _context.Current.ClosedDays;
}

public class FeatureFlagsFromContext : IFeatureFlags
{
    private readonly IStudioContext _context;

    public FeatureFlagsFromContext(IStudioContext context) => _context = context;

    public Tier CurrentTier => _context.Current.Tier;

    public bool IsEnabled(Feature feature) => CurrentTier >= feature.MinimumTier();
}

// The clock resolves its timezone per call rather than capturing it once. A
// studio that corrects its timezone on the settings screen would otherwise keep
// being judged against the old one until the application restarted — exactly
// the class of bug a studio clock exists to remove.
public class StudioClockFromContext : IStudioClock
{
    private readonly TimeProvider _timeProvider;
    private readonly IStudioContext _context;

    public StudioClockFromContext(TimeProvider timeProvider, IStudioContext context)
    {
        _timeProvider = timeProvider;
        _context = context;
    }

    public TimeZoneInfo TimeZone => TimeZoneInfo.FindSystemTimeZoneById(_context.Current.TimeZoneId);

    public DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

    public DateTime LocalNow =>
        DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(UtcNow, TimeZone), DateTimeKind.Unspecified);
}
