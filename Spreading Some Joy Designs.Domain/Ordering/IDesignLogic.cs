using SpreadingJoy.Domain.EntityModels;
using SpreadingJoy.Domain.Shared;

namespace SpreadingJoy.Domain.Ordering;

// Putting artwork on a garment.
public interface IDesignLogic
{
    Task<Design?> GetByIdAsync(int designId);

    Task<IList<Design>> GetForCustomerAsync(int customerId);

    Task<DesignResult> CreateAsync(DesignDetails details);

    Task<DesignResult> UpdateAsync(int designId, DesignDetails details);

    Task<DesignResult> SetActiveAsync(int designId, bool isActive);

    // Re-runs every rule against the design as it stands now. Called before an
    // order is placed, because the world moves between saving a design and
    // ordering it: the artwork can be rejected, the garment archived, the print
    // area shrunk.
    Task<DesignResult> ValidateForOrderAsync(int designId);
}

// One side's artwork and where it sits, in millimetres from the top-left of
// that side's print area.
public record Placement(int ArtworkId, int XMm, int YMm, int WidthMm, int HeightMm);

public record DesignDetails(
    string Name,
    int ProductId,
    int? CustomerId,
    Placement? Front,
    Placement? Back);

public class DesignResult : IOperationResult
{
    public bool Success { get; private set; }
    public string? ErrorMessage { get; private set; }
    public int DesignId { get; private set; }

    public static DesignResult Ok(int designId) => new() { Success = true, DesignId = designId };

    public static DesignResult Fail(string message) => new() { Success = false, ErrorMessage = message };
}
