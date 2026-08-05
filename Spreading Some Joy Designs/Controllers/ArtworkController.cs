using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SpreadingJoy.Domain.EntityModels;
using SpreadingJoy.Domain.IRepositories;
using SpreadingJoy.Security;
using SpreadingJoy.ViewModels;

namespace SpreadingJoy.Controllers;

public class ArtworkController : Controller
{
    private readonly IArtworkLogic _artworkLogic;
    private readonly IImageStore _imageStore;
    private readonly IUserRepository _userRepository;

    public ArtworkController(
        IArtworkLogic artworkLogic,
        IImageStore imageStore,
        IUserRepository userRepository)
    {
        _artworkLogic = artworkLogic;
        _imageStore = imageStore;
        _userRepository = userRepository;
    }

    // GET /Artwork/File/{storedFileName}
    //
    // Artwork is stored outside wwwroot precisely so it can't be served without
    // passing through here. Rejected images stay reachable to staff — they're
    // the evidence for the rejection — and return 404 to everyone else, so a
    // guessed filename tells an outsider nothing.
    [AllowAnonymous]
    [HttpGet("Artwork/File/{storedFileName}")]
    public async Task<IActionResult> File(string storedFileName, CancellationToken cancellationToken)
    {
        // The record decides whether these bytes may be served — the file
        // existing on disk is not permission to hand it out.
        var record = await _artworkLogic.GetByStoredFileNameAsync(storedFileName);
        if (record == null)
            return NotFound();

        // Pending artwork is served: the customer who just added it has to be
        // able to see it in the designer. Rejected artwork is staff-only — it's
        // kept as the evidence for the rejection, not to keep hosting it.
        // 404 rather than 403, so a guessed filename tells an outsider nothing
        // about whether it exists.
        if (record.Status == ArtworkStatus.Rejected && !User.IsInRole(Roles.Admin) && !User.IsInRole(Roles.Manager))
            return NotFound();

        var bytes = await _imageStore.ReadAsync(storedFileName, cancellationToken);
        if (bytes == null)
            return NotFound();

        // Content-addressed filenames never change contents, so this can be
        // cached hard. Private, not public: an intermediary shouldn't hold other
        // people's artwork.
        Response.Headers.CacheControl = "private, max-age=604800, immutable";

        return base.File(bytes, record.ContentType);
    }

    // GET /Artwork/Queue — the moderation queue.
    [Authorize(Policy = Policies.ModerateArtwork)]
    public async Task<IActionResult> Queue(string status = ArtworkStatus.Pending)
    {
        if (!ArtworkStatus.All.Contains(status))
            status = ArtworkStatus.Pending;

        return View(await BuildQueueAsync(status));
    }

    // POST /Artwork/Approve
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = Policies.ModerateArtwork)]
    public async Task<IActionResult> Approve(int id, string? returnStatus)
    {
        var userId = User.UserId();
        if (userId == null)
            return Forbid();

        var result = await _artworkLogic.ApproveAsync(id, userId.Value);

        if (!result.Success)
            TempData["ArtworkError"] = result.ErrorMessage;
        else
            TempData["ArtworkSuccess"] = "Approved — it can be printed now.";

        return RedirectToAction(nameof(Queue), new { status = returnStatus ?? ArtworkStatus.Pending });
    }

    // POST /Artwork/Reject
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = Policies.ModerateArtwork)]
    public async Task<IActionResult> Reject(RejectArtworkViewModel model, string? returnStatus)
    {
        var userId = User.UserId();
        if (userId == null)
            return Forbid();

        if (!ModelState.IsValid)
        {
            var queue = await BuildQueueAsync(returnStatus ?? ArtworkStatus.Pending);
            queue.ErrorMessage = "Say why it's being rejected — the customer sees this.";
            return View(nameof(Queue), queue);
        }

        var result = await _artworkLogic.RejectAsync(model.ArtworkId, userId.Value, model.Reason);

        if (!result.Success)
            TempData["ArtworkError"] = result.ErrorMessage;
        else
            TempData["ArtworkSuccess"] = "Rejected. Any design using it can no longer be ordered.";

        return RedirectToAction(nameof(Queue), new { status = returnStatus ?? ArtworkStatus.Pending });
    }

    private async Task<ArtworkQueueViewModel> BuildQueueAsync(string status)
    {
        var artworks = await _artworkLogic.GetByStatusAsync(status);
        var pending = await _artworkLogic.GetPendingAsync();

        // One lookup for the reviewer names rather than one per row.
        var reviewerIds = artworks
            .Where(a => a.ReviewedByUserId.HasValue)
            .Select(a => a.ReviewedByUserId!.Value)
            .Distinct()
            .ToList();

        var reviewers = reviewerIds.Count == 0
            ? new Dictionary<int, string>()
            : (await _userRepository.FindByAsync(u => reviewerIds.Contains(u.UserId)))
                .ToDictionary(u => u.UserId, u => u.FullName);

        return new ArtworkQueueViewModel
        {
            Status = status,
            PendingCount = pending.Count,
            SuccessMessage = TempData["ArtworkSuccess"] as string,
            ErrorMessage = TempData["ArtworkError"] as string,
            Artworks = artworks.Select(a => new ArtworkRowViewModel
            {
                Id = a.ArtworkId,
                ImageUrl = _imageStore.PublicPath(a.StoredFileName),
                Status = a.Status,
                SourceUrl = a.SourceUrl,
                OriginalFileName = a.OriginalFileName,
                WidthPx = a.WidthPx,
                HeightPx = a.HeightPx,
                ByteSize = a.ByteSize,
                CreatedAt = a.CreatedAt,
                ReviewedAt = a.ReviewedAt,
                RejectionReason = a.RejectionReason,
                MaxPrintWidthMm = ImageLimits.MaxPrintableWidthMm(a.WidthPx),
                ReviewedBy = a.ReviewedByUserId.HasValue && reviewers.TryGetValue(a.ReviewedByUserId.Value, out var name)
                    ? name
                    : null
            }).ToList()
        };
    }

}
