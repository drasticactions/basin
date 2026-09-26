using Basin.Effects;
using Xunit;

namespace Basin.Tests;

public sealed class CanvasStepMapTests
{
    private static CanvasStepMap Map(CanvasStepSides sides = CanvasStepSides.Horizontal)
    {
        var map = new CanvasStepMap();
        Span<TinyComp.OverviewSide> full = stackalloc TinyComp.OverviewSide[4];
        full[0] = (sides & CanvasStepSides.Left) != 0 ? TinyComp.OverviewLayout.StepFull(0, 0, 1720, -1, 0.75, 0.04, 3440) : default;
        full[1] = (sides & CanvasStepSides.Right) != 0 ? TinyComp.OverviewLayout.StepFull(3440, 3440, 1720, 1, 0.75, 0.04, 3440) : default;
        full[2] = (sides & CanvasStepSides.Top) != 0 ? TinyComp.OverviewLayout.StepFull(0, 0, 720, -1, 0.75, 0.04, 1440) : default;
        full[3] = (sides & CanvasStepSides.Bottom) != 0 ? TinyComp.OverviewLayout.StepFull(1440, 1440, 720, 1, 0.75, 0.04, 1440) : default;
        var box = new Box(0, 0, 3440, 1440);
        _ = TinyComp.OverviewLayout.LayoutStep(map, box, box, full, 1.0, 0.75, 0.4);
        return map;
    }

    [Fact]
    public void The_two_planes_are_uniform_scales_about_the_center()
    {
        var map = Map();
        Assert.Equal(new FBox(430, 180, 2580, 1080), map.Outline);
        Assert.Equal((1720 + (100 * 0.75), 720.0), map.ToScreen(CanvasStepPlane.Desktop, 1820, 720));
        Assert.Equal((1720 + (3570 * 0.4), 720.0), map.ToScreen(CanvasStepPlane.Shelf, 5290, 720));
        var (x, y) = map.ToCanvas(CanvasStepPlane.Shelf, 3148, 300);
        Assert.Equal(5290, x, 9);
        Assert.Equal(720 + ((300 - 720) / 0.4), y, 9);
        Assert.Equal(CanvasStepPlane.Desktop, map.PlaneAt(3000, 720));
        Assert.Equal(CanvasStepPlane.Shelf, map.PlaneAt(6000, 720));
    }

    [Fact]
    public void A_screen_point_on_a_wall_has_no_canvas_point()
    {
        var map = Map();
        Assert.True(map.TryToCanvas(1000, 700, out var plane, out var cx, out _));
        Assert.Equal(CanvasStepPlane.Desktop, plane);
        Assert.Equal(1720 + ((1000 - 1720) / 0.75), cx, 9);
        Assert.False(map.TryToCanvas(3080, 700, out _, out _, out _));
        Assert.False(map.TryToCanvas(360, 700, out _, out _, out _));
        Assert.True(map.TryToCanvas(3300, 100, out plane, out cx, out _));
        Assert.Equal(CanvasStepPlane.Shelf, plane);
        Assert.Equal(1720 + ((3300 - 1720) / 0.4), cx, 9);
        Assert.False(map.TryToCanvas(3440, 100, out _, out _, out _));
    }

    [Fact]
    public void A_box_takes_the_plane_of_its_center()
    {
        var map = Map();
        var right = map.Inner.Right;
        var edge = 1720 + ((right - 1720) / 0.4);
        Assert.Equal(CanvasStepPlane.Shelf, map.PlaneOf(new Box((int)Math.Ceiling(edge) - 50, 100, 100, 100)));
        Assert.Equal(CanvasStepPlane.Desktop, map.PlaneOf(new Box((int)Math.Floor(edge) - 52, 100, 100, 100)));
        Assert.Equal(CanvasStepPlane.Desktop, map.PlaneOf(new Box(3400, 100, 200, 100)));
    }

    [Fact]
    public void The_wall_depth_runs_from_the_desktop_edge_to_the_base_line()
    {
        var map = Map(CanvasStepSides.All);
        Assert.Equal(0.0, map.WallDepth(3010, 720, out var side), 9);
        Assert.Equal(CanvasStepSides.Right, side);
        Assert.Equal(1.0, map.WallDepth(3148, 720, out _), 9);
        Assert.Equal(0.5, map.WallDepth(3079, 720, out _), 9);
        Assert.True(map.WallDepth(1720, 720, out _) < 0);
        Assert.Equal(0.5, map.WallDepth(1720, 180 - 29, out side), 9);
        Assert.Equal(CanvasStepSides.Top, side);

        var corner = map.WallDepth(3300, 50, out side);
        Assert.Equal(CanvasStepSides.Right | CanvasStepSides.Top, side);
        Assert.Equal(Math.Max((3300 - 3010) / 138.0, (180 - 50) / 58.0), corner, 9);

        _ = map.WallDepth(3200, 150, out side);
        Assert.Equal(CanvasStepSides.Right, side);
    }

    [Fact]
    public void No_progress_leaves_both_planes_at_the_identity()
    {
        var map = new CanvasStepMap();
        Span<TinyComp.OverviewSide> full = stackalloc TinyComp.OverviewSide[4];
        full[1] = TinyComp.OverviewLayout.StepFull(3440, 3440, 1720, 1, 0.75, 0.04, 3440);
        var box = new Box(0, 0, 3440, 1440);
        _ = TinyComp.OverviewLayout.LayoutStep(map, box, box, full, 0.0, 0.75, 0.4);
        Assert.True(map.IsIdentity);
        Assert.True(map.DesktopPlacement(1.0, 10, 10).IsIdentity);
        Assert.True(map.ShelfPlacement(1.0, 10, 10).IsIdentity);
        Assert.Equal(0.0, map.WallWidth(CanvasStepSides.Right));
        Assert.Equal((5000.0, 70.0), map.ToScreen(5000, 70));
    }

    [Fact]
    public void The_drag_scale_meets_each_plane_without_a_step()
    {
        var map = Map();
        Assert.Equal(0.75, map.DragScale(-2), 12);
        Assert.Equal(0.75, map.DragScale(0), 12);
        Assert.Equal(0.4, map.DragScale(1), 12);
        Assert.Equal(0.4, map.DragScale(3), 12);
        var previous = map.DragScale(0);
        for (var i = 1; i <= 1000; i++)
        {
            var next = map.DragScale(i / 1000.0);
            Assert.True(next <= previous && previous - next < 0.001, $"step at {i}");
            previous = next;
        }

        Assert.True(map.DragScale(0.01) > 0.7495, "flat at the desktop edge");
        Assert.True(map.DragScale(0.99) < 0.4005, "flat at the base line");
    }

    [Fact]
    public void A_held_window_keeps_the_grabbed_point_under_the_cursor_across_the_plane_flip()
    {
        var map = Map();
        const double grabX = 30;
        const double grabY = 12;
        foreach (var cursorX in (ReadOnlySpan<double>)[3060, 3078.9, 3079, 3079.1, 3100, 3200])
        {
            var (canvasX, canvasY) = map.ToCanvasNearest(cursorX, 600, out _);
            var windowX = Math.Round(canvasX - grabX);
            var windowY = Math.Round(canvasY - grabY);
            var depth = map.WallDepth(cursorX, 600, out _);
            var drawn = CanvasScale.About(map.DragScale(depth), windowX + grabX, windowY + grabY, cursorX, 600);
            var (x, y) = drawn.Map(windowX + grabX, windowY + grabY);
            Assert.Equal(cursorX, x, 9);
            Assert.Equal(600, y, 9);
        }

        _ = map.ToCanvasNearest(3078, 600, out var inner);
        _ = map.ToCanvasNearest(3080, 600, out var outer);
        Assert.Equal(CanvasStepPlane.Desktop, inner);
        Assert.Equal(CanvasStepPlane.Shelf, outer);
    }

    [Fact]
    public void A_shelf_strip_includes_its_corners_and_the_fit_shrinks_to_it()
    {
        var map = Map(CanvasStepSides.All);
        Assert.Equal(new FBox(3148, 0, 292, 1440), map.ShelfStrip(CanvasStepSides.Right));
        Assert.Equal(new FBox(3148, 0, 292, 122), map.ShelfStrip(CanvasStepSides.Right | CanvasStepSides.Top));
        Assert.Equal(1.0, map.ShelfFit(600, 200, CanvasStepSides.Right, 0.2), 12);
        Assert.Equal(292 / (1000 * 0.4), map.ShelfFit(1000, 200, CanvasStepSides.Right, 0.2), 12);
        Assert.Equal(0.2 / 0.4, map.ShelfFit(4000, 200, CanvasStepSides.Right, 0.2), 12);
        var placed = map.ShelfPlacement(0.5, 100, 50);
        Assert.Equal(0.2, placed.M11, 12);
        Assert.Equal(map.ToScreen(CanvasStepPlane.Shelf, 100, 50), placed.Map(100, 50));
    }
}
