namespace SpreadingJoy.Tests;

public class FeatureFlagTests
{
    [Fact]
    public void A_tier_includes_everything_below_it()
    {
        var flags = new TierFeatureFlags(Tier.Production);

        Assert.True(flags.IsEnabled(Feature.OnlineOrdering));
        Assert.True(flags.IsEnabled(Feature.ArtworkModeration));
        Assert.True(flags.IsEnabled(Feature.ProductionBoard));
    }

    [Fact]
    public void A_tier_excludes_everything_above_it()
    {
        var flags = new TierFeatureFlags(Tier.Production);

        Assert.False(flags.IsEnabled(Feature.WholesalePricing));
        Assert.False(flags.IsEnabled(Feature.ApiAccess));
    }

    [Fact]
    public void The_lowest_tier_still_gets_the_things_the_site_cannot_run_without()
    {
        var flags = new TierFeatureFlags(Tier.Storefront);

        // Moderation isn't an upsell. A studio that can take orders has to be
        // able to review what it's printing.
        Assert.True(flags.IsEnabled(Feature.ArtworkModeration));
        Assert.True(flags.IsEnabled(Feature.OnlineOrdering));
    }

    [Fact]
    public void The_top_tier_gets_everything()
    {
        var flags = new TierFeatureFlags(Tier.Wholesale);

        foreach (var feature in Enum.GetValues<Feature>())
            Assert.True(flags.IsEnabled(feature));
    }

    [Fact]
    public void Every_feature_declares_a_tier()
    {
        // The switch in FeatureTiers throws for anything unplaced, so adding a
        // Feature without giving it a tier fails here rather than silently
        // becoming available everywhere.
        foreach (var feature in Enum.GetValues<Feature>())
            Assert.True(Enum.IsDefined(feature.MinimumTier()));
    }
}
