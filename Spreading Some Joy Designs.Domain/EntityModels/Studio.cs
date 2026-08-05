using SpreadingJoy.Domain.Licensing;

namespace SpreadingJoy.Domain.EntityModels;

// The print shop itself. One row per studio; everything that varies between
// studios lives here rather than in configuration, so the studio can change its
// own hours and capacity from a screen instead of needing a deploy.
public partial class Studio
{
    public int StudioId { get; set; }

    public string Name { get; set; } = null!;

    public string? Phone { get; set; }

    public string? Email { get; set; }

    public string? AddressLine { get; set; }

    public string? City { get; set; }

    public string? State { get; set; }

    public string? PostalCode { get; set; }

    // Due dates are wall-clock dates at the studio, so the logic layer needs the
    // studio's zone rather than the server's. See IStudioClock.
    public string TimeZoneId { get; set; } = "America/Chicago";

    // How many garments can actually go through the press in one day. This is
    // the number that stops a single order swallowing a whole week.
    public int DailyPrintCapacity { get; set; }

    // The soonest a new order can be promised, in working days. A customer
    // asking for tomorrow is refused against this rather than against a guess.
    public int TurnaroundDays { get; set; }

    // Stored as a comma-separated string of DayOfWeek names, same trick the
    // schema uses everywhere else a small fixed set needs one column.
    public string ClosedDaysRaw { get; set; } = "Saturday,Sunday";

    public string TierName { get; set; } = nameof(Tier.Storefront);

    public DateTime CreatedAt { get; set; }

    // ---- Computed in C#, not columns. Mapped as Ignore in the context. ----

    public IReadOnlyCollection<DayOfWeek> ClosedDays =>
        ClosedDaysRaw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(d => Enum.TryParse<DayOfWeek>(d, ignoreCase: true, out var day) ? (DayOfWeek?)day : null)
            .Where(d => d.HasValue)
            .Select(d => d!.Value)
            .Distinct()
            .ToArray();

    // An unrecognised tier name reads as the lowest tier rather than throwing.
    // A studio that can't sign in at all because someone typo'd a column is a
    // worse failure than one that temporarily can't see its paid features.
    public Tier Tier =>
        Enum.TryParse<Tier>(TierName, ignoreCase: true, out var tier) ? tier : Tier.Storefront;
}
