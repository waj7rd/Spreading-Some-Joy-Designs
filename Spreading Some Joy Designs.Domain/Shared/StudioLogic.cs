using SpreadingJoy.Domain.EntityModels;
using SpreadingJoy.Domain.IRepositories;

namespace SpreadingJoy.Domain.Shared;

public class StudioLogic : IStudioLogic
{
    // A press that can do nothing is a studio that can take no orders; a press
    // that can do ten thousand shirts a day is a typo. Both are worth catching
    // here rather than discovering through a schedule nobody can meet.
    private const int MinCapacity = 1;
    private const int MaxCapacity = 5000;

    private const int MinTurnaround = 1;
    private const int MaxTurnaround = 90;

    // A ceiling on the postage charge, for the same reason capacity has one: the
    // number is typed by hand into a box, and a slipped decimal point here is a
    // quote no customer would agree to.
    private const decimal MaxShippingFee = 500m;

    private readonly IStudioRepository _studioRepository;
    private readonly IStudioContext _studioContext;

    public StudioLogic(IStudioRepository studioRepository, IStudioContext studioContext)
    {
        _studioRepository = studioRepository;
        _studioContext = studioContext;
    }

    public async Task<Studio?> GetAsync()
    {
        var all = await _studioRepository.GetAllAsync();
        return all.OrderBy(s => s.StudioId).FirstOrDefault();
    }

    public async Task<StudioResult> UpdateAsync(
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
        IReadOnlyCollection<DayOfWeek> closedDays,
        bool offersShipping,
        decimal shippingFee)
    {
        if (string.IsNullOrWhiteSpace(name))
            return StudioResult.Fail("Give the studio a name.");

        if (dailyPrintCapacity < MinCapacity || dailyPrintCapacity > MaxCapacity)
            return StudioResult.Fail($"Daily capacity has to be between {MinCapacity} and {MaxCapacity} garments.");

        if (turnaroundDays < MinTurnaround || turnaroundDays > MaxTurnaround)
            return StudioResult.Fail($"Turnaround has to be between {MinTurnaround} and {MaxTurnaround} working days.");

        // A studio closed every day can never satisfy a due date, and the
        // earliest-date search would run to its cap on every page load.
        if (closedDays.Distinct().Count() >= 7)
            return StudioResult.Fail("The studio has to be open at least one day a week.");

        // Checked even when shipping is switched off, so a saved-and-forgotten
        // bad figure cannot start charging the moment somebody ticks the box.
        if (shippingFee < 0m)
            return StudioResult.Fail("The shipping fee cannot be negative.");

        if (shippingFee > MaxShippingFee)
            return StudioResult.Fail($"The shipping fee has to be {MaxShippingFee:C} or less.");

        // Validated here rather than trusted: a bad id would throw from
        // StudioClock on the next request, and the settings screen is a much
        // better place to find out than the order form.
        if (!IsKnownTimeZone(timeZoneId))
            return StudioResult.Fail($"'{timeZoneId}' isn't a timezone this server recognises.");

        var studio = await GetAsync();
        if (studio == null)
            return StudioResult.Fail("Studio record not found.");

        studio.Name = name.Trim();
        studio.Phone = Blank(phone);
        studio.Email = Blank(email);
        studio.AddressLine = Blank(addressLine);
        studio.City = Blank(city);
        studio.State = Blank(state);
        studio.PostalCode = Blank(postalCode);
        studio.TimeZoneId = timeZoneId;
        studio.DailyPrintCapacity = dailyPrintCapacity;
        studio.TurnaroundDays = turnaroundDays;
        studio.ClosedDaysRaw = string.Join(',', closedDays.Distinct().OrderBy(d => d));
        studio.OffersShipping = offersShipping;
        studio.ShippingFee = decimal.Round(shippingFee, 2, MidpointRounding.AwayFromZero);

        await _studioRepository.SaveChangesAsync();

        // The context caches this row. Without the reload, the screen would
        // report a saved change that nothing else in the application can see.
        _studioContext.Reload();

        return StudioResult.Ok();
    }

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsKnownTimeZone(string timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
            return false;

        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }
}
