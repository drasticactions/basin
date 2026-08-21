using Basin;
using EightWm;
using Xunit;

namespace Basin.Tests;

public sealed class TabletViewStateTests
{
    private sealed class FakeApp : IShellApp
    {
        public FakeApp(string name, int minWidth = 0)
        {
            Name = name;
            MinWidth = minWidth;
        }

        public string Name { get; }

        public int MinWidth { get; }

        public Box Cell { get; private set; }

        public bool Visible { get; private set; }

        public void Placed(in Box cell)
        {
            Cell = cell;
            Visible = true;
        }

        public void Hidden() => Visible = false;

        public override string ToString() => Name;
    }

    private static readonly Box Landscape = new(0, 0, 1366, 768);
    private static readonly Box Portrait = new(0, 0, 768, 1366);

    private static AppHost<FakeApp> Host(int minWidth = 500, int gutter = 22)
    {
        return new AppHost<FakeApp> { MinWidth = minWidth, Gutter = gutter };
    }

    private static AppHost<FakeApp> With(params FakeApp[] apps)
    {
        var host = Host();
        foreach (var app in apps)
        {
            host.Adopt(app);
        }

        host.Layout(Landscape, portrait: false);
        return host;
    }

    [Fact]
    public void One_cell_takes_the_whole_area()
    {
        var only = new FakeApp("a");
        var host = With(only);

        Assert.Equal(new Box(0, 0, 1366, 768), only.Cell);
        Assert.True(only.Visible);
    }

    [Fact]
    public void A_split_leaves_the_gutter_between_the_cells()
    {
        var first = new FakeApp("a");
        var second = new FakeApp("b");
        var host = With(first, second);

        Assert.Equal(672, first.Cell.Width);
        Assert.Equal(672, second.Cell.Width);
        Assert.Equal(694, second.Cell.X);
        Assert.Equal(new Box(672, 0, 22, 768), host.GutterBox(0));
    }

    [Fact]
    public void A_new_cell_takes_half_of_the_one_it_splits()
    {
        var first = new FakeApp("a");
        var host = With(first);

        Assert.True(host.TrySplit(new FakeApp("b"), at: 1, fraction: 0.5));
        host.Layout(Landscape, portrait: false);

        Assert.Equal(672, host.Widths[0]);
        Assert.Equal(672, host.Widths[1]);
    }

    [Fact]
    public void A_new_cell_can_take_the_width_the_gesture_released_at()
    {
        var host = Host(minWidth: 200);
        host.Adopt(new FakeApp("a"));
        host.Layout(Landscape, portrait: false);

        Assert.True(host.TrySplit(new FakeApp("b"), at: 1, fraction: 0.25));
        host.Layout(Landscape, portrait: false);

        Assert.Equal(1008, host.Widths[0]);
        Assert.Equal(336, host.Widths[1]);
    }

    [Fact]
    public void Dragging_the_splitter_resizes_both_cells()
    {
        var first = new FakeApp("a");
        var second = new FakeApp("b");
        var host = With(first, second);

        Assert.True(host.TrySetSplit(0, 800));
        host.Layout(Landscape, portrait: false);

        Assert.Equal(800, first.Cell.Width);
        Assert.Equal(544, second.Cell.Width);
        Assert.Equal(822, second.Cell.X);
    }

    [Fact]
    public void A_splitter_drag_stops_at_the_minimum()
    {
        var first = new FakeApp("a");
        var second = new FakeApp("b");
        var host = With(first, second);

        Assert.True(host.TrySetSplit(0, 1300));
        host.Layout(Landscape, portrait: false);

        Assert.Equal(844, first.Cell.Width);
        Assert.Equal(500, second.Cell.Width);
    }

    [Fact]
    public void A_rule_can_opt_one_app_into_a_narrower_minimum()
    {
        var wide = new FakeApp("a");
        var narrow = new FakeApp("b", minWidth: 320);
        var host = With(wide, narrow);

        Assert.True(host.TrySetSplit(0, 1300));
        host.Layout(Landscape, portrait: false);

        Assert.Equal(1024, wide.Cell.Width);
        Assert.Equal(320, narrow.Cell.Width);
    }

    [Fact]
    public void A_cell_that_cannot_meet_its_minimum_ejects_rather_than_shrinks()
    {
        var host = Host();
        var first = new FakeApp("a");
        var second = new FakeApp("b");
        var third = new FakeApp("c");
        host.Adopt(first);
        host.Adopt(second);
        host.Adopt(third);

        host.Layout(Landscape, portrait: false);

        Assert.Equal(2, host.Cells.Count);
        Assert.DoesNotContain(third, host.Cells);
        Assert.False(third.Visible);
        Assert.Contains(third, host.Mru);
    }

    [Fact]
    public void An_ejected_app_stays_in_the_mru()
    {
        var host = With(new FakeApp("a"));
        var ejected = new FakeApp("b");
        host.Adopt(ejected);
        host.Eject(ejected);

        Assert.DoesNotContain(ejected, host.Cells);
        Assert.Contains(ejected, host.Mru);
        Assert.Same(ejected, host.Previous());
    }

    [Fact]
    public void A_narrow_output_holds_one_cell_only()
    {
        var host = Host();
        host.Adopt(new FakeApp("a"));
        host.Adopt(new FakeApp("b"));

        host.Layout(new Box(0, 0, 900, 600), portrait: false);

        Assert.Single(host.Cells);
    }

    [Fact]
    public void Portrait_stacks_the_cells_vertically()
    {
        var host = Host();
        var first = new FakeApp("a");
        var second = new FakeApp("b");
        host.Adopt(first);
        host.Adopt(second);

        host.Layout(Portrait, portrait: true);

        Assert.Equal(768, first.Cell.Width);
        Assert.Equal(672, first.Cell.Height);
        Assert.Equal(694, second.Cell.Y);
        Assert.Equal(new Box(0, 672, 768, 22), host.GutterBox(0));
    }

    [Fact]
    public void Four_cells_is_the_ceiling()
    {
        var host = Host(minWidth: 200);
        for (var i = 0; i < 6; i++)
        {
            host.Adopt(new FakeApp($"a{i}"));
        }

        host.Layout(Landscape, portrait: false);

        Assert.Equal(4, host.Cells.Count);
    }

    [Fact]
    public void The_splitter_hit_target_is_wider_than_the_rail()
    {
        var host = With(new FakeApp("a"), new FakeApp("b"));

        Assert.Equal(0, host.SplitterAt(676, 400, slop: 8));
        Assert.Equal(0, host.SplitterAt(666, 400, slop: 8));
        Assert.Equal(-1, host.SplitterAt(600, 400, slop: 8));
    }

    [Fact]
    public void Each_host_lays_out_on_its_own_area()
    {
        var first = Host();
        var second = Host();
        var onFirst = new FakeApp("a");
        var onSecond = new FakeApp("b");
        first.Adopt(onFirst);
        second.Adopt(onSecond);

        first.Layout(new Box(0, 0, 1366, 768), portrait: false);
        second.Layout(new Box(1366, 0, 800, 1280), portrait: true);

        Assert.Equal(new Box(0, 0, 1366, 768), onFirst.Cell);
        Assert.Equal(new Box(1366, 0, 800, 1280), onSecond.Cell);
    }

    [Fact]
    public void The_mru_never_shows_an_app_twice()
    {
        var host = Host();
        var first = new FakeApp("a");
        var second = new FakeApp("b");
        host.Adopt(first);
        host.Adopt(second);
        host.Activate(first);
        host.Activate(second);
        host.Activate(first);

        Assert.Equal(2, host.Mru.Count);
        Assert.Same(first, host.Mru[0]);
    }

    [Fact]
    public void A_closed_app_leaves_the_mru_at_once()
    {
        var host = Host();
        var app = new FakeApp("a");
        host.Adopt(app);

        host.Forget(app);

        Assert.Empty(host.Mru);
        Assert.Empty(host.Cells);
    }

    [Fact]
    public void A_snap_into_an_empty_host_leaves_the_other_side_vacant()
    {
        var only = new FakeApp("a");
        var host = Host();
        host.Layout(Landscape, portrait: false);

        Assert.True(host.TrySplit(only, at: 0, fraction: 0.5));
        host.Layout(Landscape, portrait: false);

        Assert.Single(host.Cells);
        Assert.Equal(2, host.SlotCount);
        Assert.True(host.HasVacancy);
        Assert.Equal(1, host.VacantSlot);
        Assert.Equal(new Box(0, 0, 672, 768), only.Cell);
        Assert.Equal(new Box(694, 0, 672, 768), host.VacantArea);
    }

    [Fact]
    public void A_snap_to_the_far_side_leaves_the_vacancy_in_front_of_it()
    {
        var only = new FakeApp("a");
        var host = Host();
        host.Layout(Landscape, portrait: false);

        Assert.True(host.TrySplit(only, at: 1, fraction: 0.5));
        host.Layout(Landscape, portrait: false);

        Assert.Equal(0, host.VacantSlot);
        Assert.Equal(new Box(694, 0, 672, 768), only.Cell);
        Assert.Equal(new Box(0, 0, 672, 768), host.VacantArea);
    }

    [Fact]
    public void A_snap_keeps_the_width_the_gesture_released_at()
    {
        var only = new FakeApp("a", minWidth: 200);
        var host = Host();
        host.Layout(Landscape, portrait: false);

        Assert.True(host.TrySplit(only, at: 0, fraction: 0.25));
        host.Layout(Landscape, portrait: false);

        Assert.Equal(336, only.Cell.Width);
        Assert.Equal(1008, host.VacantArea.Width);
    }

    [Fact]
    public void The_next_app_takes_the_vacancy_rather_than_splitting_again()
    {
        var first = new FakeApp("a");
        var second = new FakeApp("b");
        var host = Host();
        host.Layout(Landscape, portrait: false);
        Assert.True(host.TrySplit(first, at: 0, fraction: 0.5));
        host.Layout(Landscape, portrait: false);

        Assert.True(host.TrySplit(second, at: 0, fraction: 0.5));
        host.Layout(Landscape, portrait: false);

        Assert.False(host.HasVacancy);
        Assert.Equal(2, host.SlotCount);
        Assert.Equal(new Box(0, 0, 672, 768), first.Cell);
        Assert.Equal(new Box(694, 0, 672, 768), second.Cell);
    }

    [Fact]
    public void A_mapped_app_fills_the_vacancy_and_leaves_the_snap_alone()
    {
        var snapped = new FakeApp("a");
        var mapped = new FakeApp("b");
        var host = Host();
        host.Layout(Landscape, portrait: false);
        Assert.True(host.TrySplit(snapped, at: 0, fraction: 0.5));
        host.Layout(Landscape, portrait: false);

        host.Replace(mapped);
        host.Layout(Landscape, portrait: false);

        Assert.False(host.HasVacancy);
        Assert.True(snapped.Visible);
        Assert.Equal(new Box(694, 0, 672, 768), mapped.Cell);
    }

    [Fact]
    public void The_splitter_beside_the_vacancy_resizes_the_snapped_cell()
    {
        var only = new FakeApp("a");
        var host = Host();
        host.Layout(Landscape, portrait: false);
        Assert.True(host.TrySplit(only, at: 0, fraction: 0.5));
        host.Layout(Landscape, portrait: false);

        Assert.Equal(new Box(672, 0, 22, 768), host.GutterBox(0));
        Assert.True(host.TrySetSplit(0, 800));
        host.Layout(Landscape, portrait: false);

        Assert.Equal(800, only.Cell.Width);
        Assert.Equal(544, host.VacantArea.Width);
    }

    [Fact]
    public void Closing_the_snapped_app_takes_the_vacancy_with_it()
    {
        var only = new FakeApp("a");
        var host = Host();
        host.Layout(Landscape, portrait: false);
        Assert.True(host.TrySplit(only, at: 0, fraction: 0.5));
        host.Layout(Landscape, portrait: false);

        host.Forget(only);
        host.Layout(Landscape, portrait: false);

        Assert.False(host.HasVacancy);
        Assert.Equal(0, host.SlotCount);
    }

    [Fact]
    public void A_narrow_output_drops_the_vacancy_before_it_ejects_the_cell()
    {
        var only = new FakeApp("a");
        var host = Host();
        host.Layout(Landscape, portrait: false);
        Assert.True(host.TrySplit(only, at: 0, fraction: 0.5));
        host.Layout(Landscape, portrait: false);

        host.Layout(new Box(0, 0, 900, 600), portrait: false);

        Assert.False(host.HasVacancy);
        Assert.Single(host.Cells);
        Assert.Equal(new Box(0, 0, 900, 600), only.Cell);
    }

    [Fact]
    public void Portrait_stacks_the_vacancy_under_the_cell()
    {
        var only = new FakeApp("a");
        var host = Host();
        host.Layout(Portrait, portrait: true);

        Assert.True(host.TrySplit(only, at: 0, fraction: 0.5));
        host.Layout(Portrait, portrait: true);

        Assert.Equal(new Box(0, 0, 768, 672), only.Cell);
        Assert.Equal(new Box(0, 694, 768, 672), host.VacantArea);
    }

    [Fact]
    public void Filling_the_last_vacancy_is_the_only_way_past_the_cell_ceiling()
    {
        var host = Host(minWidth: 200);
        host.Layout(Landscape, portrait: false);
        for (var i = 0; i < 3; i++)
        {
            host.Adopt(new FakeApp($"a{i}"));
        }

        host.Layout(Landscape, portrait: false);
        Assert.True(host.TrySplit(new FakeApp("b"), at: 3, fraction: 0.5));
        host.Layout(Landscape, portrait: false);

        Assert.Equal(4, host.Cells.Count);
        Assert.False(host.TrySplit(new FakeApp("c"), at: 4, fraction: 0.5));
    }

    [Fact]
    public void A_measured_split_is_the_box_the_split_would_produce()
    {
        var first = new FakeApp("a", minWidth: 300);
        var second = new FakeApp("b", minWidth: 300);
        var arriving = new FakeApp("c", minWidth: 300);
        var host = Host(minWidth: 300);
        host.Adopt(first);
        host.Adopt(second);
        host.Layout(Landscape, portrait: false);

        Assert.True(host.TryMeasureSplit(arriving, at: 0, fraction: 0.5, out var measured));
        Assert.True(host.TrySplit(arriving, at: 0, fraction: 0.5));
        host.Layout(Landscape, portrait: false);

        Assert.Equal(arriving.Cell, measured);
    }

    [Fact]
    public void A_measured_split_leaves_the_host_exactly_as_it_was()
    {
        var first = new FakeApp("a");
        var second = new FakeApp("b");
        var host = Host();
        host.Adopt(first);
        host.Adopt(second);
        host.Layout(new Box(0, 0, 2000, 768), portrait: false);
        var cells = host.Cells.ToArray();
        var widths = host.Widths.ToArray();
        var mru = host.Mru.ToArray();
        var boxes = new[] { first.Cell, second.Cell };

        Assert.True(host.TryMeasureSplit(new FakeApp("c"), at: 0, fraction: 0.5, out _));

        Assert.Equal(cells, host.Cells);
        Assert.Equal(widths, host.Widths);
        Assert.Equal(mru, host.Mru);
        Assert.Equal(boxes, new[] { first.Cell, second.Cell });
        Assert.True(first.Visible);
        Assert.True(second.Visible);
    }

    [Fact]
    public void A_measured_split_answers_the_vacancy_when_there_is_one()
    {
        var snapped = new FakeApp("a");
        var host = Host();
        host.Layout(Landscape, portrait: false);
        Assert.True(host.TrySplit(snapped, at: 0, fraction: 0.5));
        host.Layout(Landscape, portrait: false);

        Assert.True(host.TryMeasureSplit(new FakeApp("b"), at: 0, fraction: 0.5, out var measured));

        Assert.Equal(host.VacantArea, measured);
        Assert.True(host.HasVacancy);
    }

    [Fact]
    public void A_measured_split_of_the_only_cell_is_the_side_it_would_snap_to()
    {
        var only = new FakeApp("a");
        var host = Host();
        host.Adopt(only);
        host.Layout(Landscape, portrait: false);

        Assert.True(host.TryMeasureSplit(only, at: 1, fraction: 0.5, out var measured));

        Assert.Equal(new Box(694, 0, 672, 768), measured);
        Assert.Equal(new Box(0, 0, 1366, 768), only.Cell);
    }

    [Fact]
    public void A_measured_split_with_no_room_answers_nothing()
    {
        var host = Host();
        host.Adopt(new FakeApp("a"));
        host.Adopt(new FakeApp("b"));
        host.Layout(new Box(0, 0, 900, 600), portrait: false);

        Assert.False(host.TryMeasureSplit(new FakeApp("c"), at: 0, fraction: 0.5, out var measured));
        Assert.True(measured.IsEmpty);
    }

    [Fact]
    public void Previous_is_the_most_recent_app_that_holds_no_cell()
    {
        var host = Host();
        var shown = new FakeApp("a");
        var hidden = new FakeApp("b");
        host.Adopt(shown);
        host.Adopt(hidden);
        host.Layout(new Box(0, 0, 900, 600), portrait: false);

        Assert.Same(hidden, host.Previous());
    }
}
