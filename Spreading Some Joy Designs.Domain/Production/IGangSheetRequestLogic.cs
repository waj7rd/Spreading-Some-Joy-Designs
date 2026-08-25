using SpreadingJoy.Domain.EntityModels;
using SpreadingJoy.Domain.Shared;

namespace SpreadingJoy.Domain.Production;

// Gang sheets built by members of the public.
//
// The same two-stage path everything anonymous takes: a stranger arranges their
// own images on a sheet, asks for it, and it waits in a holding table. Nothing
// they typed becomes a Customer, and nothing they uploaded becomes a real
// GangSheet, until a member of staff accepts it. That rule is in the
// architecture notes and it is the reason this logic exists separately from
// IGangSheetLogic rather than as a flag on it.
//
// Acceptance goes through GangSheetLogic.AddItemsAsync, which refuses artwork
// that isn't approved. Deliberately: a second way of putting transfers on a
// sheet would be a second path to the press, and second paths get reached by
// accident.
public interface IGangSheetRequestLogic
{
    // Packs a sheet without saving anything, so the builder can show somebody
    // what they've got as they arrange it. The same packer that lays out the
    // real thing — a preview drawn by different code is a preview that lies.
    Task<SheetPreview?> PreviewAsync(int gangSheetSizeId, IReadOnlyCollection<BuilderItem> items);

    Task<GangSheetRequestResult> SubmitAsync(SubmitGangSheetRequest request);

    Task<IList<GangSheetRequest>> GetByStatusAsync(string status);

    Task<GangSheetRequest?> GetByIdAsync(int gangSheetRequestId);

    Task<int> CountPendingAsync();

    // Turns the request into a real sheet owned by a real customer, in one
    // transaction. Refuses if any of the artwork is still waiting for review.
    Task<GangSheetRequestResult> AcceptAsync(int gangSheetRequestId, int handledByUserId);

    Task<GangSheetRequestResult> DeclineAsync(int gangSheetRequestId, int handledByUserId, string reason);
}

// One image the visitor has put on their sheet, at a size, some number of times.
public record BuilderItem(int ArtworkId, string Label, int WidthMm, int HeightMm, int Quantity);

public record SubmitGangSheetRequest(
    string CustomerName,
    string? Email,
    string Phone,
    int GangSheetSizeId,
    IReadOnlyCollection<BuilderItem> Items,
    string? Notes,
    bool RightsAttested);

// What the packer made of it, for drawing on screen. Not stored: where each
// transfer actually lands is decided again when the request becomes a real
// sheet, and a layout computed for a preview is not one anybody printed from.
public class SheetPreview
{
    public int GangSheetSizeId { get; set; }
    public string SizeName { get; set; } = string.Empty;
    public int WidthMm { get; set; }
    public int LengthMm { get; set; }
    public decimal Price { get; set; }

    public int UsedLengthMm { get; set; }
    public double CoveragePercent { get; set; }

    public IReadOnlyList<PreviewPlacement> Placed { get; set; } = [];

    // Labels of anything that wouldn't go on. Shown rather than dropped: a
    // visitor whose fourth image silently vanished would submit a sheet missing
    // the thing they came for.
    public IReadOnlyList<string> TooBig { get; set; } = [];
    public IReadOnlyList<string> NoRoom { get; set; } = [];

    // Nothing left over. The condition for being allowed to submit.
    public bool Fits => TooBig.Count == 0 && NoRoom.Count == 0;
}

public record PreviewPlacement(
    int ArtworkId,
    string Label,
    int XMm,
    int YMm,
    int WidthMm,
    int HeightMm,
    bool Rotated);

public class GangSheetRequestResult : IOperationResult
{
    public bool Success { get; private set; }
    public string? ErrorMessage { get; private set; }
    public int GangSheetRequestId { get; private set; }
    public int? GangSheetId { get; private set; }

    public static GangSheetRequestResult Ok(int requestId, int? gangSheetId = null) =>
        new() { Success = true, GangSheetRequestId = requestId, GangSheetId = gangSheetId };

    public static GangSheetRequestResult Fail(string message) =>
        new() { Success = false, ErrorMessage = message };
}
