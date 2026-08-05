namespace SpreadingJoy.Domain.Licensing;

// Whether a capability is available to the studio currently being served.
//
// Behind an interface so the backing store can change without touching a single
// call site: a column on the studio record today, something a billing system
// owns later. Same trick as IStudioClock.
public interface IFeatureFlags
{
    Tier CurrentTier { get; }

    bool IsEnabled(Feature feature);
}

// Tier-driven implementation. The entire mechanism is one comparison — that is
// the payoff for keeping tiers as a chain rather than a set.
public class TierFeatureFlags : IFeatureFlags
{
    public TierFeatureFlags(Tier currentTier)
    {
        CurrentTier = currentTier;
    }

    public Tier CurrentTier { get; }

    public bool IsEnabled(Feature feature) => CurrentTier >= feature.MinimumTier();
}
