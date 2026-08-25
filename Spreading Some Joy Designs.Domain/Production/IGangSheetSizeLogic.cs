using SpreadingJoy.Domain.EntityModels;
using SpreadingJoy.Domain.Shared;

namespace SpreadingJoy.Domain.Production;

// The gang sheet catalogue: which sheets of film the studio sells, and for how
// much.
//
// Separate from IProductLogic because a sheet of film is not a garment. It has
// no blank cost, no size run and no per-side print charge, and putting it in
// Products would have meant every one of those columns carrying a meaningless
// value on a third of the rows.
public interface IGangSheetSizeLogic
{
    // What the public builder offers.
    Task<IList<GangSheetSize>> GetActiveAsync();

    // Everything, withdrawn ones included, for the management screen.
    Task<IList<GangSheetSize>> GetAllAsync();

    Task<GangSheetSize?> GetByIdAsync(int gangSheetSizeId);

    Task<GangSheetSizeResult> CreateAsync(GangSheetSizeDetails details);

    Task<GangSheetSizeResult> UpdateAsync(int gangSheetSizeId, GangSheetSizeDetails details);

    // Withdrawn rather than deleted, so a sheet already sold at this size keeps
    // resolving to the row it was sold under.
    Task<GangSheetSizeResult> SetActiveAsync(int gangSheetSizeId, bool isActive);
}

public record GangSheetSizeDetails(string Name, int WidthMm, int LengthMm, decimal Price);

public class GangSheetSizeResult : IOperationResult
{
    public bool Success { get; private set; }
    public string? ErrorMessage { get; private set; }
    public int GangSheetSizeId { get; private set; }

    public static GangSheetSizeResult Ok(int id) => new() { Success = true, GangSheetSizeId = id };

    public static GangSheetSizeResult Fail(string message) => new() { Success = false, ErrorMessage = message };
}
