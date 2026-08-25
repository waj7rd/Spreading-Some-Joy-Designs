using SpreadingJoy.Domain.EntityModels;
using SpreadingJoy.Domain.Shared;

namespace SpreadingJoy.Domain.Production;

// Building sheets of film for the press.
//
// The rule worth knowing before reading any of this: a gang sheet is the last
// thing that happens before ink meets film, which makes it the last place the
// artwork approval gate can be applied. It is applied twice — when a transfer
// is added, and again when the sheet is marked ready — for the same reason
// Fulfilment.Check runs at both submission and acceptance. The first is a
// courtesy so nobody builds a sheet they can't print; the second is the one
// that counts, because artwork can be rejected while a draft sits open.
public interface IGangSheetLogic
{
    Task<IList<GangSheet>> GetAllAsync();

    Task<GangSheet?> GetAsync(int gangSheetId);

    Task<GangSheetResult> CreateAsync(GangSheetDetails details, int? createdByUserId);

    // Renaming, resizing, changing the gutter. Repacks, because every one of
    // those changes where the transfers land.
    Task<GangSheetResult> UpdateAsync(int gangSheetId, GangSheetDetails details);

    Task<GangSheetResult> DeleteAsync(int gangSheetId);

    // Adds transfers and repacks. A request with a quantity becomes that many
    // rows: each copy has to go somewhere on the film.
    Task<GangSheetResult> AddItemsAsync(int gangSheetId, IReadOnlyCollection<GangSheetItemRequest> requests);

    Task<GangSheetResult> RemoveItemAsync(int gangSheetId, int gangSheetItemId);

    // Runs the packer again over what's already on the sheet.
    Task<GangSheetResult> RepackAsync(int gangSheetId);

    // Locks the sheet for printing. Refuses if any artwork on it isn't
    // approved, or if anything on it didn't fit.
    Task<GangSheetResult> MarkReadyAsync(int gangSheetId);

    Task<GangSheetResult> MarkPrintedAsync(int gangSheetId);

    // Back to a draft, so a mistake spotted at the press can be fixed. A sheet
    // that already printed stays printed — that piece of film exists.
    Task<GangSheetResult> ReopenAsync(int gangSheetId);

    // What's waiting to be printed, one entry per printed side of every line on
    // every open order.
    Task<IList<TransferCandidate>> GetCandidatesAsync();

    // Hands a freshly built sheet to the customer who ordered it, at the price
    // they were quoted rather than whatever the catalogue says now.
    //
    // Called only from GangSheetRequestLogic.AcceptAsync, inside its
    // transaction. Separate from CreateAsync on purpose: a sheet is built the
    // same way whoever it turns out to be for, so ownership is the last step
    // rather than a second constructor — which is what stops there being two
    // ways to make a sheet.
    Task<GangSheetResult> MarkAsCustomerSheetAsync(
        int gangSheetId, int customerId, int gangSheetSizeId, decimal price);
}

// The editable fields of a sheet, as one parameter object — same reasoning as
// ProductDetails. Seven positional arguments is a call site nobody can read.
public record GangSheetDetails(
    string Name,
    int WidthMm,
    int MaxLengthMm,
    int GutterMm,
    int MarginMm,
    bool AllowRotation,
    string? Notes);

// A request to put something on a sheet. Quantity is expanded into one row per
// copy by the logic layer, because that is what the film has to hold.
public record GangSheetItemRequest(
    int ArtworkId,
    int? OrderLineId,
    int? DesignId,
    string Side,
    string Label,
    int WidthMm,
    int HeightMm,
    int Quantity);

// One printable side of one order line, ready to be added to a sheet. Built by
// the logic layer so the screen doesn't have to know that a two-sided design is
// two transfers.
public class TransferCandidate
{
    public int OrderLineId { get; set; }

    public int OrderId { get; set; }

    public int DesignId { get; set; }

    public int ArtworkId { get; set; }

    public string Side { get; set; } = GangSheetSide.Front;

    public string Label { get; set; } = null!;

    public string DesignName { get; set; } = null!;

    public string? CustomerName { get; set; }

    public string SizeCode { get; set; } = null!;

    public DateTime DueOn { get; set; }

    public int Quantity { get; set; }

    public int WidthMm { get; set; }

    public int HeightMm { get; set; }

    // The artwork's own pixel width, so the screen can work out what the
    // resolution comes to at the size it's being printed. Print quality is a
    // property of the image and the size together, never the file alone.
    public int ArtworkWidthPx { get; set; }

    public string ArtworkStatus { get; set; } = EntityModels.ArtworkStatus.Pending;

    public string StoredFileName { get; set; } = null!;

    // How many copies of this exact side are already sitting on some sheet.
    // Shown rather than filtered on: a reprint is a normal thing to want.
    public int AlreadyPlaced { get; set; }
}

public class GangSheetResult : IOperationResult
{
    public bool Success { get; private set; }
    public string? ErrorMessage { get; private set; }
    public int GangSheetId { get; private set; }

    public static GangSheetResult Ok(int gangSheetId) => new() { Success = true, GangSheetId = gangSheetId };

    public static GangSheetResult Fail(string message) => new() { Success = false, ErrorMessage = message };
}
