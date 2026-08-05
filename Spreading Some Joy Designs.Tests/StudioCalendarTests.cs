namespace SpreadingJoy.Tests;

public class StudioCalendarTests
{
    // Monday 3 August 2026.
    private static readonly DateTime Monday = new(2026, 8, 3);

    private static StudioSettings Settings(int turnaround = 3) =>
        new(dailyPrintCapacity: 60,
            turnaroundDays: turnaround,
            closedDays: new[] { DayOfWeek.Saturday, DayOfWeek.Sunday });

    [Fact]
    public void Turnaround_is_counted_in_working_days()
    {
        // Monday + 3 working days = Thursday.
        Assert.Equal(new DateTime(2026, 8, 6), StudioCalendar.EarliestDueDate(Settings(), Monday));
    }

    [Fact]
    public void Turnaround_skips_the_weekend()
    {
        // Thursday + 3 working days lands on the following Tuesday, not Sunday.
        var thursday = new DateTime(2026, 8, 6);

        Assert.Equal(new DateTime(2026, 8, 11), StudioCalendar.EarliestDueDate(Settings(), thursday));
    }

    [Fact]
    public void A_date_in_the_past_is_refused()
    {
        var error = StudioCalendar.CheckDueDate(Settings(), Monday.AddDays(-1), Monday);

        Assert.NotNull(error);
        Assert.Contains("passed", error);
    }

    [Fact]
    public void A_closed_day_is_refused_and_says_so()
    {
        // Saturday 15 August — well past the turnaround, but the studio is shut.
        var error = StudioCalendar.CheckDueDate(Settings(), new DateTime(2026, 8, 15), Monday);

        Assert.NotNull(error);
        Assert.Contains("doesn't print on Saturday", error);
    }

    [Fact]
    public void A_date_inside_the_turnaround_window_is_refused_differently()
    {
        // Tomorrow: open, in the future, but not enough time.
        var error = StudioCalendar.CheckDueDate(Settings(), Monday.AddDays(1), Monday);

        Assert.NotNull(error);
        Assert.Contains("working days", error);
    }

    [Fact]
    public void The_earliest_date_the_calendar_suggests_is_one_it_accepts()
    {
        // Otherwise the order form prefills a date its own rules reject.
        var settings = Settings();
        var earliest = StudioCalendar.EarliestDueDate(settings, Monday);

        Assert.Null(StudioCalendar.CheckDueDate(settings, earliest, Monday));
    }

    [Fact]
    public void Next_open_day_skips_forward_over_closed_days()
    {
        var saturday = new DateTime(2026, 8, 8);

        Assert.Equal(new DateTime(2026, 8, 10), StudioCalendar.NextOpenDay(Settings(), saturday));
    }

    [Fact]
    public void Next_open_day_returns_the_given_day_when_it_is_already_open()
    {
        Assert.Equal(Monday, StudioCalendar.NextOpenDay(Settings(), Monday));
    }

    [Fact]
    public void A_studio_closed_every_day_terminates_instead_of_spinning()
    {
        // StudioLogic refuses to save this, but the search must not hang if a
        // row ever reaches that state some other way.
        var closedAlways = new StudioSettings(60, 3, Enum.GetValues<DayOfWeek>());

        var result = StudioCalendar.NextOpenDay(closedAlways, Monday);

        Assert.True(result >= Monday);
    }
}
