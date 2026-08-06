using SpreadingJoy.Domain.EntityModels;
using SpreadingJoy.Domain.Shared;

namespace SpreadingJoy.Domain.Ordering;

// Putting artwork on a garment.
public interface IDesignLogic
{
    Task<Design?> GetByIdAsync(int designId);

    // For the anonymous order page. Keyed on the unguessable token so the page
    // can't be walked by counting upwards.
    Task<Design?> GetByPublicTokenAsync(Guid publicToken);

    Task<IList<Design>> GetForCustomerAsync(int customerId);

    // The shop: the studio's own designs, ready to order. Active only.
    Task<IList<Design>> GetStudioDesignsAsync();

    // Everything the studio has made, archived included, for the staff screen.
    Task<IList<Design>> GetAllStudioDesignsAsync();

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
    Placement? Back,

    // Set only by the staff path. A customer has no way to supply this — the
    // designer's public flow passes false, and the flag isn't on any view model
    // the model binder could fill from a post.
    bool IsStudioDesign = false);

public class DesignResult : IOperationResult
{
    public bool Success { get; private set; }
    public string? ErrorMessage { get; private set; }
    public int DesignId { get; private set; }

    // The unguessable identifier the caller should put in a URL. Returned
    // alongside the id so a controller never has to reach for the key to build
    // a link.
    public Guid PublicToken { get; private set; }

    public static DesignResult Ok(int designId, Guid publicToken = default) =>
        new() { Success = true, DesignId = designId, PublicToken = publicToken };

    public static DesignResult Fail(string message) => new() { Success = false, ErrorMessage = message };
}
