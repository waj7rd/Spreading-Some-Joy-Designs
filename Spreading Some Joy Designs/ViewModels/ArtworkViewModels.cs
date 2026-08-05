using System.ComponentModel.DataAnnotations;

namespace SpreadingJoy.ViewModels;

public class ArtworkRowViewModel
{
    public int Id { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? SourceUrl { get; set; }
    public string? OriginalFileName { get; set; }
    public int WidthPx { get; set; }
    public int HeightPx { get; set; }
    public long ByteSize { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? RejectionReason { get; set; }

    // How wide this can be printed and still hold 150 DPI. The single most
    // useful number for a moderator: it's the difference between "fine" and
    // "this will look terrible across a chest".
    public int MaxPrintWidthMm { get; set; }

    public string Dimensions => $"{WidthPx} × {HeightPx}";

    public string SizeDisplay => ByteSize < 1024 * 1024
        ? $"{ByteSize / 1024} KB"
        : $"{ByteSize / (1024.0 * 1024.0):0.#} MB";

    // Where it came from, in one phrase, for the queue.
    public string Provenance => SourceUrl != null
        ? "Pasted from the web"
        : OriginalFileName != null
            ? $"Uploaded — {OriginalFileName}"
            : "Uploaded";
}

public class ArtworkQueueViewModel
{
    public string? SuccessMessage { get; set; }
    public string? ErrorMessage { get; set; }
    public string Status { get; set; } = string.Empty;
    public IList<ArtworkRowViewModel> Artworks { get; set; } = [];

    public int PendingCount { get; set; }
}

public class RejectArtworkViewModel
{
    public int ArtworkId { get; set; }

    [Required(ErrorMessage = "Say why it's being rejected — the customer sees this.")]
    [StringLength(500)]
    [Display(Name = "Reason")]
    public string Reason { get; set; } = string.Empty;
}
