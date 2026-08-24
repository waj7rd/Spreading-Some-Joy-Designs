namespace SpreadingJoy.Domain.Shared;

// Facts about how this particular studio runs. Behind an interface so the
// Domain doesn't take a dependency on IConfiguration, and so tests can state
// the studio's shape directly.
public interface IStudioSettings
{
    // Garments that can genuinely go through the press in one day. This is the
    // number that stops a 400-shirt order landing on a Tuesday.
    int DailyPrintCapacity { get; }

    // The soonest anything can be promised, in working days from today.
    int TurnaroundDays { get; }

    // Days the studio doesn't print at all.
    IReadOnlyCollection<DayOfWeek> ClosedDays { get; }

    // Whether the studio will post an order out. Read on the storefront to decide
    // whether to offer the choice at all, and again in the logic layer, so a
    // hand-built POST cannot order shipping from a studio that does not do it.
    bool OffersShipping { get; }

    // The flat charge for posting one order out.
    decimal ShippingFee { get; }
}

public class StudioSettings : IStudioSettings
{
    public StudioSettings(
        int dailyPrintCapacity,
        int turnaroundDays = 3,
        IReadOnlyCollection<DayOfWeek>? closedDays = null,
        bool offersShipping = false,
        decimal shippingFee = 0m)
    {
        DailyPrintCapacity = dailyPrintCapacity;
        TurnaroundDays = turnaroundDays;
        ClosedDays = closedDays ?? new[] { DayOfWeek.Saturday, DayOfWeek.Sunday };
        OffersShipping = offersShipping;
        ShippingFee = shippingFee;
    }

    public int DailyPrintCapacity { get; }
    public int TurnaroundDays { get; }
    public IReadOnlyCollection<DayOfWeek> ClosedDays { get; }
    public bool OffersShipping { get; }
    public decimal ShippingFee { get; }
}

// Which days the studio can actually promise work for.
public static class StudioCalendar
{
    // Returns why the date doesn't work, or null if it does.
    //
    // Two separate refusals, and they read differently to a customer: a closed
    // day is "we're not here", a date inside the turnaround window is "we're
    // here, but we can't have it ready by then".
    public static string? CheckDueDate(IStudioSettings settings, DateTime dueOn, DateTime today)
    {
        var due = dueOn.Date;

        if (due < today.Date)
            return "That date has already passed.";

        if (settings.ClosedDays.Contains(due.DayOfWeek))
            return $"The studio doesn't print on {due.DayOfWeek}s.";

        var earliest = EarliestDueDate(settings, today);
        if (due < earliest)
            return $"We need {settings.TurnaroundDays} working days. The soonest we can promise is {earliest:dddd d MMMM}.";

        return null;
    }

    // Today plus the turnaround, counted in working days rather than calendar
    // days — a three-day turnaround requested on a Friday afternoon is the
    // following Wednesday, not Monday.
    public static DateTime EarliestDueDate(IStudioSettings settings, DateTime today)
    {
        var date = today.Date;
        var remaining = settings.TurnaroundDays;

        // A studio closed every day would spin forever; cap the search well
        // beyond any sane turnaround and return what we reached.
        for (var attempt = 0; attempt < 365 && remaining > 0; attempt++)
        {
            date = date.AddDays(1);
            if (!settings.ClosedDays.Contains(date.DayOfWeek))
                remaining--;
        }

        return date;
    }

    // The next open day on or after the given date, used to prefill the date
    // field. A default the rules would immediately reject is worse than no
    // default — the customer fixes an error they didn't cause.
    public static DateTime NextOpenDay(IStudioSettings settings, DateTime from)
    {
        var date = from.Date;

        for (var attempt = 0; attempt < 365; attempt++)
        {
            if (!settings.ClosedDays.Contains(date.DayOfWeek))
                return date;

            date = date.AddDays(1);
        }

        return date;
    }
}
