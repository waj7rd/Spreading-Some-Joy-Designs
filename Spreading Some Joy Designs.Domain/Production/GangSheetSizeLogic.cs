using SpreadingJoy.Domain.EntityModels;
using SpreadingJoy.Domain.IRepositories;
using SpreadingJoy.Domain.Shared;

namespace SpreadingJoy.Domain.Production;

public class GangSheetSizeLogic : IGangSheetSizeLogic
{
    // Nobody sells a sheet of film for four figures, and a stray zero on a price
    // is the kind of mistake that only shows up in an angry email.
    private const decimal MaxPrice = 1000m;

    private const int MaxNameLength = 60;

    private readonly IGangSheetSizeRepository _sizeRepository;
    private readonly IStudioClock _clock;

    public GangSheetSizeLogic(IGangSheetSizeRepository sizeRepository, IStudioClock clock)
    {
        _sizeRepository = sizeRepository;
        _clock = clock;
    }

    public Task<IList<GangSheetSize>> GetActiveAsync() => _sizeRepository.GetActiveAsync();

    public async Task<IList<GangSheetSize>> GetAllAsync()
    {
        var all = await _sizeRepository.GetAllAsync();

        return all
            .OrderByDescending(s => s.IsActive)
            .ThenBy(s => s.LengthMm)
            .ToList();
    }

    public Task<GangSheetSize?> GetByIdAsync(int gangSheetSizeId) =>
        _sizeRepository.GetAsync(s => s.GangSheetSizeId == gangSheetSizeId);

    public async Task<GangSheetSizeResult> CreateAsync(GangSheetSizeDetails details)
    {
        var problem = Validate(details);
        if (problem != null)
            return GangSheetSizeResult.Fail(problem);

        var name = details.Name.Trim();

        var clash = await _sizeRepository.GetAsync(s => s.Name == name);
        if (clash != null)
            return GangSheetSizeResult.Fail($"There's already a sheet called \"{name}\".");

        var size = new GangSheetSize
        {
            IsActive = true,
            CreatedAt = _clock.UtcNow
        };

        Apply(size, details);

        await _sizeRepository.AddAsync(size);
        await _sizeRepository.SaveChangesAsync();

        return GangSheetSizeResult.Ok(size.GangSheetSizeId);
    }

    public async Task<GangSheetSizeResult> UpdateAsync(int gangSheetSizeId, GangSheetSizeDetails details)
    {
        var problem = Validate(details);
        if (problem != null)
            return GangSheetSizeResult.Fail(problem);

        var size = await _sizeRepository.GetAsync(s => s.GangSheetSizeId == gangSheetSizeId);
        if (size == null)
            return GangSheetSizeResult.Fail("Sheet size not found.");

        var name = details.Name.Trim();

        var clash = await _sizeRepository.GetAsync(s => s.Name == name && s.GangSheetSizeId != gangSheetSizeId);
        if (clash != null)
            return GangSheetSizeResult.Fail($"There's already a sheet called \"{name}\".");

        // Changing the price here does not restate anything already sold. Both
        // GangSheetRequests.PriceQuoted and GangSheets.Price are snapshots, so a
        // rise applies to the next customer rather than the last one.
        Apply(size, details);

        await _sizeRepository.SaveChangesAsync();
        return GangSheetSizeResult.Ok(size.GangSheetSizeId);
    }

    public async Task<GangSheetSizeResult> SetActiveAsync(int gangSheetSizeId, bool isActive)
    {
        var size = await _sizeRepository.GetAsync(s => s.GangSheetSizeId == gangSheetSizeId);
        if (size == null)
            return GangSheetSizeResult.Fail("Sheet size not found.");

        // Withdrawing the last one would leave the public builder with nothing
        // to sell, which reads as the site being broken rather than as the
        // studio having stopped offering sheets.
        if (!isActive)
        {
            var others = await _sizeRepository.FindByAsync(s => s.IsActive && s.GangSheetSizeId != gangSheetSizeId);
            if (others.Count == 0)
                return GangSheetSizeResult.Fail("That's the only sheet on offer — add another before withdrawing this one.");
        }

        size.IsActive = isActive;
        await _sizeRepository.SaveChangesAsync();

        return GangSheetSizeResult.Ok(size.GangSheetSizeId);
    }

    private static void Apply(GangSheetSize size, GangSheetSizeDetails details)
    {
        size.Name = details.Name.Trim();
        size.WidthMm = details.WidthMm;
        size.LengthMm = details.LengthMm;
        size.Price = details.Price;
    }

    private static string? Validate(GangSheetSizeDetails details)
    {
        if (string.IsNullOrWhiteSpace(details.Name))
            return "Give the sheet a name.";

        if (details.Name.Trim().Length > MaxNameLength)
            return $"Keep the name under {MaxNameLength} characters.";

        if (details.WidthMm < FilmSizes.MinWidthMm || details.WidthMm > FilmSizes.MaxWidthMm)
            return $"Film width has to be between {FilmSizes.MinWidthMm}mm and {FilmSizes.MaxWidthMm}mm.";

        if (details.LengthMm < FilmSizes.MinLengthMm || details.LengthMm > FilmSizes.MaxLengthMm)
            return $"Sheet length has to be between {FilmSizes.MinLengthMm}mm and {FilmSizes.MaxLengthMm}mm.";

        // Zero is allowed — a studio running an offer, or handing one out with a
        // bulk order, shouldn't have to invent a price to do it.
        if (details.Price < 0 || details.Price > MaxPrice)
            return $"Price has to be between 0 and {MaxPrice:C}.";

        return null;
    }
}
