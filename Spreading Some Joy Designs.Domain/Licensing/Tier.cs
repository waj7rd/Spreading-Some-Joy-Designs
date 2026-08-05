namespace SpreadingJoy.Domain.Licensing;

// What a studio has bought.
//
// Deliberately an ordered chain rather than a set of independent switches: each
// tier contains everything below it. Three tiers means three valid
// configurations, not eight — which is the difference between testing this and
// hoping.
//
// The numbers are explicit because the comparison is the whole mechanism, and
// because these values end up in a database column.
public enum Tier
{
    Storefront = 1,
    Production = 2,
    Wholesale = 3,
}

// Individual capabilities. Each declares the lowest tier that includes it — the
// feature owns that fact, rather than a central per-tier list that someone will
// eventually forget to update.
public enum Feature
{
    OnlineOrdering,
    ProductCatalog,
    StaffAccounts,
    ArtworkModeration,

    ProductionBoard,
    BulkDiscounts,
    OrderExports,

    WholesalePricing,
    ApiAccess,
}

public static class FeatureTiers
{
    // A switch expression rather than a dictionary on purpose: add a Feature
    // without placing it and the compiler complains, instead of it silently
    // defaulting to available-everywhere.
    public static Tier MinimumTier(this Feature feature) => feature switch
    {
        Feature.OnlineOrdering    => Tier.Storefront,
        Feature.ProductCatalog    => Tier.Storefront,
        Feature.StaffAccounts     => Tier.Storefront,
        Feature.ArtworkModeration => Tier.Storefront,

        Feature.ProductionBoard   => Tier.Production,
        Feature.BulkDiscounts     => Tier.Production,
        Feature.OrderExports      => Tier.Production,

        Feature.WholesalePricing  => Tier.Wholesale,
        Feature.ApiAccess         => Tier.Wholesale,

        _ => throw new ArgumentOutOfRangeException(nameof(feature), feature,
                 "Feature has no tier. Every feature must declare one."),
    };
}
