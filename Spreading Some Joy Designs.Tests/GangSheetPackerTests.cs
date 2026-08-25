using static SpreadingJoy.Domain.Production.GangSheetPacker;

namespace SpreadingJoy.Tests;

// The packer is a pure function over rectangles, which is the whole reason it
// was written as one: where a transfer ends up on a piece of film is the sort
// of thing somebody argues with, and an argument is only settleable if it can
// be run.
public class GangSheetPackerTests
{
    // 220mm of film with a 5mm border leaves 210mm to print across, and a 10mm
    // gutter between neighbours. Round numbers so the arithmetic in each test
    // can be checked by eye.
    private static SheetSpec Sheet(int maxLengthMm = 600, bool allowRotation = true) =>
        new(WidthMm: 220, MaxLengthMm: maxLengthMm, GutterMm: 10, MarginMm: 5, AllowRotation: allowRotation);

    private static PackItem Item(int key, int width, int height) => new(key, width, height);

    [Fact]
    public void The_first_transfer_starts_inside_the_margin()
    {
        var result = Pack([Item(0, 100, 50)], Sheet());

        var placement = Assert.Single(result.Placed);
        Assert.Equal(5, placement.XMm);
        Assert.Equal(5, placement.YMm);
    }

    [Fact]
    public void Transfers_sit_side_by_side_with_a_gutter_between_them()
    {
        var result = Pack([Item(0, 100, 50), Item(1, 100, 50)], Sheet());

        Assert.Equal(2, result.Placed.Count);

        var second = result.Placed.Single(p => p.Key == 1);

        // 5mm margin + 100mm of transfer + 10mm gutter.
        Assert.Equal(115, second.XMm);
        Assert.Equal(5, second.YMm);
    }

    [Fact]
    public void A_transfer_that_runs_off_the_edge_starts_a_new_row()
    {
        // Three 100mm transfers across 210mm of usable film: two fit, the third
        // drops to a new row below the tallest thing in the first.
        var result = Pack([Item(0, 100, 50), Item(1, 100, 50), Item(2, 100, 50)], Sheet());

        var third = result.Placed.Single(p => p.Key == 2);

        Assert.Equal(5, third.XMm);
        Assert.Equal(65, third.YMm);
    }

    [Fact]
    public void The_length_reported_is_what_was_used_not_what_was_allowed()
    {
        // Two rows of 50mm transfers on a sheet allowed to run to 600mm.
        var result = Pack([Item(0, 100, 50), Item(1, 100, 50), Item(2, 100, 50)], Sheet());

        // 5 margin + 50 + 10 gutter + 50 + 5 margin.
        Assert.Equal(120, result.UsedLengthMm);
    }

    [Fact]
    public void An_empty_sheet_uses_no_film()
    {
        var result = Pack([], Sheet());

        Assert.Empty(result.Placed);
        Assert.Equal(0, result.UsedLengthMm);
    }

    [Fact]
    public void Taller_transfers_are_laid_down_first()
    {
        var result = Pack([Item(0, 100, 20), Item(1, 100, 80)], Sheet());

        // The tall one takes the first position, whatever order they arrived in.
        Assert.Equal(1, result.Placed[0].Key);
    }

    [Fact]
    public void A_transfer_wider_than_the_film_is_refused_and_says_why()
    {
        // Too wide in both directions, so rotating it doesn't help.
        var result = Pack([Item(0, 300, 300)], Sheet());

        Assert.Empty(result.Placed);
        var refused = Assert.Single(result.Unplaced);
        Assert.Equal(Rejection.TooWideForTheFilm, refused.Reason);
    }

    [Fact]
    public void A_transfer_too_wide_to_lie_flat_is_turned_to_fit()
    {
        // 300mm across is more film than there is, but 50mm is not.
        var result = Pack([Item(0, 300, 50)], Sheet());

        var placement = Assert.Single(result.Placed);
        Assert.True(placement.Rotated);
    }

    [Fact]
    public void Rotation_is_a_fallback_not_a_habit()
    {
        // It already fits lying flat, so it is left alone. A cut list full of
        // sideways transfers nobody asked for is its own kind of mistake.
        var result = Pack([Item(0, 100, 50)], Sheet());

        Assert.False(Assert.Single(result.Placed).Rotated);
    }

    [Fact]
    public void With_rotation_off_a_wide_transfer_is_simply_refused()
    {
        var result = Pack([Item(0, 300, 50)], Sheet(allowRotation: false));

        Assert.Empty(result.Placed);
        Assert.Equal(Rejection.TooWideForTheFilm, Assert.Single(result.Unplaced).Reason);
    }

    [Fact]
    public void Running_out_of_film_is_a_different_answer_from_being_too_wide()
    {
        // Fits across the film easily; there is simply no length left for it.
        var result = Pack([Item(0, 200, 100), Item(1, 200, 100)], Sheet(maxLengthMm: 120));

        Assert.Single(result.Placed);
        Assert.Equal(Rejection.SheetIsFull, Assert.Single(result.Unplaced).Reason);
    }

    [Fact]
    public void A_transfer_that_will_not_fit_does_not_stop_the_ones_behind_it()
    {
        // 250mm of usable length. The 60mm transfer opens a new row it can't
        // fit in, but the 30mm one behind it fits that same row — so it has to
        // still be tried rather than abandoned with the rest.
        var result = Pack(
            [Item(0, 100, 200), Item(1, 100, 100), Item(2, 100, 60), Item(3, 100, 30)],
            Sheet(maxLengthMm: 260));

        Assert.Equal(Rejection.SheetIsFull, Assert.Single(result.Unplaced).Reason);
        Assert.Equal(2, Assert.Single(result.Unplaced).Key);

        var last = result.Placed.Single(p => p.Key == 3);
        Assert.Equal(5, last.XMm);
        Assert.Equal(215, last.YMm);
    }

    [Fact]
    public void The_same_transfers_always_pack_the_same_way()
    {
        // Identical rectangles are separated by key, not by whatever order the
        // sort happened to leave them in. A repack that shuffled equal
        // transfers around would invalidate a cut list somebody is holding.
        PackItem[] items = [Item(7, 100, 50), Item(3, 100, 50), Item(5, 100, 50)];

        var first = Pack(items, Sheet());
        var second = Pack(items.Reverse().ToArray(), Sheet());

        Assert.Equal(
            first.Placed.OrderBy(p => p.Key).Select(p => (p.Key, p.XMm, p.YMm)),
            second.Placed.OrderBy(p => p.Key).Select(p => (p.Key, p.XMm, p.YMm)));

        // Lowest key takes the first position.
        Assert.Equal(3, first.Placed[0].Key);
    }

    [Fact]
    public void Margins_that_eat_the_whole_film_refuse_everything()
    {
        // Caught rather than divided by. GangSheetLogic refuses these settings
        // before they are ever saved, but the packer is a public function and
        // shouldn't fall over if it's handed them.
        var spec = new SheetSpec(WidthMm: 100, MaxLengthMm: 100, GutterMm: 0, MarginMm: 50, AllowRotation: true);

        var result = Pack([Item(0, 10, 10)], spec);

        Assert.Empty(result.Placed);
        Assert.Equal(0, result.UsedLengthMm);
        Assert.Equal(Rejection.TooWideForTheFilm, Assert.Single(result.Unplaced).Reason);
    }
}
