using SpreadingJoy.Tests.Fakes;

namespace SpreadingJoy.Tests;

// The settings screen is the one place a studio changes how it runs, so the
// rules that stop it saving something unworkable live here.
//
// Only the shipping settings are covered so far — they arrived with the
// shipping work, and a bad figure in the fee box is a number a customer gets
// charged.
public class StudioLogicTests
{
    private readonly FakeStudioRepository _studios = new();
    private readonly Studio _studio;
    private readonly FakeStudioContext _context;
    private readonly StudioLogic _logic;

    public StudioLogicTests()
    {
        _studio = new Studio
        {
            StudioId = 1,
            Name = "Spreading Some Joy Designs",
            TimeZoneId = "America/Chicago",
            DailyPrintCapacity = 60,
            TurnaroundDays = 3,
            ClosedDaysRaw = "Saturday,Sunday",
            TierName = nameof(Tier.Storefront),
            CreatedAt = new DateTime(2026, 1, 1)
        };

        _studios.Seed(_studio);
        _context = new FakeStudioContext(_studio);
        _logic = new StudioLogic(_studios, _context);
    }

    private Task<StudioResult> SaveAsync(bool offersShipping = false, decimal shippingFee = 0m) =>
        _logic.UpdateAsync(
            name: "Spreading Some Joy Designs",
            phone: null,
            email: null,
            addressLine: null,
            city: null,
            state: null,
            postalCode: null,
            timeZoneId: "America/Chicago",
            dailyPrintCapacity: 60,
            turnaroundDays: 3,
            closedDays: [DayOfWeek.Saturday, DayOfWeek.Sunday],
            offersShipping: offersShipping,
            shippingFee: shippingFee);

    [Fact]
    public async Task Shipping_can_be_switched_on_with_a_fee()
    {
        var result = await SaveAsync(offersShipping: true, shippingFee: 8.50m);

        Assert.True(result.Success);
        Assert.True(_studio.OffersShipping);
        Assert.Equal(8.50m, _studio.ShippingFee);
    }

    [Fact]
    public async Task Shipping_can_be_switched_back_off()
    {
        await SaveAsync(offersShipping: true, shippingFee: 8m);

        var result = await SaveAsync(offersShipping: false, shippingFee: 8m);

        Assert.True(result.Success);
        Assert.False(_studio.OffersShipping);

        // The fee is kept rather than cleared. Switching postage off for a month
        // shouldn't make somebody look the number up again.
        Assert.Equal(8m, _studio.ShippingFee);
    }

    [Fact]
    public async Task A_negative_shipping_fee_is_refused()
    {
        var result = await SaveAsync(offersShipping: true, shippingFee: -1m);

        Assert.False(result.Success);
        Assert.Equal(0m, _studio.ShippingFee);
    }

    [Fact]
    public async Task An_implausible_shipping_fee_is_refused()
    {
        // A slipped decimal point in a hand-typed box is a quote no customer
        // would agree to, and nothing downstream would question it.
        var result = await SaveAsync(offersShipping: true, shippingFee: 5000m);

        Assert.False(result.Success);
        Assert.Equal(0m, _studio.ShippingFee);
    }

    [Fact]
    public async Task The_fee_is_checked_even_when_shipping_is_switched_off()
    {
        // Otherwise a bad figure saves quietly today and starts charging the
        // moment somebody ticks the box.
        var result = await SaveAsync(offersShipping: false, shippingFee: -1m);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task The_fee_is_rounded_to_the_cent_it_will_be_charged_at()
    {
        var result = await SaveAsync(offersShipping: true, shippingFee: 8.005m);

        Assert.True(result.Success);
        Assert.Equal(8.01m, _studio.ShippingFee);
    }

    [Fact]
    public async Task A_saved_change_reloads_the_cached_studio_record()
    {
        // Without this the screen reports a saved change that no other part of
        // the application can see until it restarts.
        await SaveAsync(offersShipping: true, shippingFee: 8m);

        Assert.Equal(1, _context.ReloadCount);
    }

    [Fact]
    public async Task A_refused_change_does_not_reload_anything()
    {
        await SaveAsync(offersShipping: true, shippingFee: -1m);

        Assert.Equal(0, _context.ReloadCount);
    }
}
