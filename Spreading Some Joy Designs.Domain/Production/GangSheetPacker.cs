namespace SpreadingJoy.Domain.Production;

// Decides where each transfer goes on the film.
//
// Pure functions, no repository and no database, for the same reason
// ImageUrlPolicy and Pricing are: this is the part somebody will argue with,
// and an argument about packing is only settleable if it can be run against a
// list of rectangles in a test.
//
// The algorithm is first-fit decreasing height — shelf packing. Sort the
// transfers tallest first, lay them left to right in a row, and when the row
// runs out of film width, start a new row below the tallest thing in the last
// one. It is not optimal; optimal rectangle packing is NP-hard and the sheet
// costs a few dollars. It is predictable, which matters more: the same set of
// transfers packs the same way every time, so a sheet that gets repacked after
// one removal doesn't rearrange itself wholesale under somebody who had already
// started cutting.
public static class GangSheetPacker
{
    // What a sheet of film allows. Separate from the entity so the packer can
    // be tested without one.
    public record SheetSpec(int WidthMm, int MaxLengthMm, int GutterMm, int MarginMm, bool AllowRotation)
    {
        // The film minus the border the feed rollers touch.
        public int UsableWidthMm => WidthMm - (2 * MarginMm);

        public int UsableLengthMm => MaxLengthMm - (2 * MarginMm);
    }

    // One transfer waiting to be placed. Key is whatever the caller wants back
    // in the result — the packer never looks at it.
    public record PackItem(int Key, int WidthMm, int HeightMm);

    public record Placement(int Key, int XMm, int YMm, bool Rotated);

    // Why a transfer didn't make it onto this sheet. Two different problems
    // with two different answers: too wide is never going to fit on this film
    // at all, whereas out of room just needs another sheet.
    public enum Rejection
    {
        TooWideForTheFilm,
        SheetIsFull,
    }

    public record Unplaced(int Key, Rejection Reason);

    public record PackResult(
        IReadOnlyList<Placement> Placed,
        IReadOnlyList<Unplaced> Unplaced,
        int UsedLengthMm);

    public static PackResult Pack(IReadOnlyList<PackItem> items, SheetSpec sheet)
    {
        var placed = new List<Placement>();
        var unplaced = new List<Unplaced>();

        if (sheet.UsableWidthMm <= 0 || sheet.UsableLengthMm <= 0)
        {
            // A sheet whose margins eat the whole film. Nothing fits, and
            // saying so beats dividing by zero further down.
            return new PackResult(placed, items.Select(i => new Unplaced(i.Key, Rejection.TooWideForTheFilm)).ToList(), 0);
        }

        // Orientation is decided before sorting, because rotating a transfer
        // changes which of its dimensions counts as its height — and the sort
        // is on height.
        var oriented = new List<(PackItem Item, int Width, int Height, bool Rotated)>();

        foreach (var item in items)
        {
            if (item.WidthMm <= sheet.UsableWidthMm)
            {
                oriented.Add((item, item.WidthMm, item.HeightMm, false));
            }
            else if (sheet.AllowRotation && item.HeightMm <= sheet.UsableWidthMm)
            {
                // Rotation is a fallback, not an optimisation. Turning things
                // only when they would otherwise not fit keeps the layout
                // predictable, and keeps the cut list from being full of
                // sideways transfers nobody asked for.
                oriented.Add((item, item.HeightMm, item.WidthMm, true));
            }
            else
            {
                unplaced.Add(new Unplaced(item.Key, Rejection.TooWideForTheFilm));
            }
        }

        // Tallest first. Key breaks the tie so the same input always packs the
        // same way — a repack that shuffled equal-sized transfers around would
        // invalidate a cut list somebody is holding.
        var queue = oriented
            .OrderByDescending(o => o.Height)
            .ThenByDescending(o => o.Width)
            .ThenBy(o => o.Item.Key)
            .ToList();

        var shelfX = sheet.MarginMm;
        var shelfY = sheet.MarginMm;
        var shelfHeight = 0;
        var bottom = 0;

        foreach (var (item, width, height, rotated) in queue)
        {
            // Row full: drop to a new one below the tallest thing in this one.
            if (shelfX + width > sheet.MarginMm + sheet.UsableWidthMm)
            {
                shelfY += shelfHeight + sheet.GutterMm;
                shelfX = sheet.MarginMm;
                shelfHeight = 0;
            }

            if (shelfY + height > sheet.MarginMm + sheet.UsableLengthMm)
            {
                // Out of film. Keep going rather than stopping: the queue is
                // sorted by height, so something shorter further down may still
                // fit in the row we're standing in.
                unplaced.Add(new Unplaced(item.Key, Rejection.SheetIsFull));
                continue;
            }

            placed.Add(new Placement(item.Key, shelfX, shelfY, rotated));

            shelfX += width + sheet.GutterMm;
            shelfHeight = Math.Max(shelfHeight, height);
            bottom = Math.Max(bottom, shelfY + height);
        }

        // What the studio actually pays for: the film up to the last transfer,
        // plus the bottom margin. Not MaxLengthMm — that was only ever a
        // ceiling.
        var usedLength = placed.Count == 0 ? 0 : bottom + sheet.MarginMm;

        return new PackResult(placed, unplaced, usedLength);
    }
}
