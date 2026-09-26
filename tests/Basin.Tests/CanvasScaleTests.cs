using Basin.Effects;
using Xunit;

namespace Basin.Tests;

public sealed class CanvasScaleTests
{
    private const int Width = 1000;
    private const int Height = 800;

    private static CanvasWarpTransform Map(bool top = false)
    {
        var left = new CanvasWarp();
        left.Layout(seam: 120, direction: -1, zoneWidth: 120, extension: 500, edgeScale: 0.15, slope: 0.25, center: Height / 2.0);
        var right = new CanvasWarp(1);
        right.Layout(seam: Width - 120, direction: 1, zoneWidth: 120, extension: 500, edgeScale: 0.15, slope: 0.25, center: Height / 2.0);
        var map = new CanvasWarpTransform { Left = left, Right = right };
        if (top)
        {
            var upper = new CanvasWarp();
            upper.Layout(seam: 96, direction: -1, zoneWidth: 96, extension: 400, edgeScale: 0.15, slope: 0.25, center: Width / 2.0);
            map.Top = upper;
            var lower = new CanvasWarp(1);
            lower.Layout(seam: Height - 96, direction: 1, zoneWidth: 0, extension: 0, edgeScale: 0.15);
            map.Bottom = lower;
        }

        return map;
    }

    [Fact]
    public void Axis_scale_is_one_up_to_the_seam_and_follows_the_zone_curve()
    {
        var map = Map();
        var right = map.Right!;
        var scale = new CanvasScale();
        Assert.Equal(1.0, scale.AxisScale(right, right.Seam - 40));
        Assert.Equal(1.0, scale.AxisScale(right, right.Seam));
        Assert.Equal(1.0, scale.AxisScale(null, 5000));
        for (var depth = 1; depth < 600; depth += 17)
        {
            Assert.Equal(right.ScaleAt(right.Seam + depth), scale.AxisScale(right, right.Seam + depth), 12);
        }

        scale.Reach = 2.0;
        for (var depth = 1; depth < 600; depth += 17)
        {
            Assert.Equal(right.ScaleAt(right.Seam + (depth / 2.0)), scale.AxisScale(right, right.Seam + depth), 12);
        }
    }

    [Fact]
    public void The_scale_never_falls_below_the_floor_and_a_floor_below_the_edge_scale_is_dead()
    {
        var map = Map();
        var deep = new Box(map.Right!.Seam + 400, 300, 200, 150);
        var scale = new CanvasScale { MinScale = 0.35 };
        Assert.Equal(0.35, scale.ScaleFor(map, deep), 12);

        scale.MinScale = 0.05;
        Assert.Equal(map.Right.ScaleAt(deep.X), scale.ScaleFor(map, deep), 12);
        Assert.True(scale.ScaleFor(map, deep) >= map.Right.EdgeScale);

        scale.MinScale = 1.0;
        Assert.Equal(1.0, scale.ScaleFor(map, deep));
    }

    [Fact]
    public void A_box_in_the_flat_area_or_straddling_at_full_scale_draws_through_the_identity()
    {
        var map = Map(top: true);
        var scale = new CanvasScale();
        Assert.True(scale.PlacementFor(map, new Box(300, 300, 200, 150)).IsIdentity);

        var straddle = new Box(map.Right!.Seam - 100, 300, 200, 150);
        Assert.Equal(1.0, scale.ScaleFor(map, straddle));
        Assert.True(scale.PlacementFor(map, straddle).IsIdentity);

        var atSeam = new Box(map.Right.Seam, 300, 200, 150);
        Assert.True(scale.PlacementFor(map, atSeam).IsIdentity);
    }

    [Fact]
    public void Sweeping_a_box_across_a_side_seam_never_jumps()
    {
        var map = Map(top: true);
        var scale = new CanvasScale();
        var previous = scale.DrawnBox(scale.PlacementFor(map, new Box(400, 300, 300, 200)), new Box(400, 300, 300, 200));
        for (var x = 401; x <= map.Right!.FarEdge; x++)
        {
            var box = new Box(x, 300, 300, 200);
            var drawn = scale.DrawnBox(scale.PlacementFor(map, box), box);
            AssertNear(previous, drawn, 7);
            previous = drawn;
        }
    }

    [Fact]
    public void Sweeping_a_side_parked_box_up_into_the_top_zone_never_jumps()
    {
        var map = Map(top: true);
        var scale = new CanvasScale();
        var x = map.Right!.Seam + 200;
        var start = new Box(x, 400, 300, 200);
        var previous = scale.DrawnBox(scale.PlacementFor(map, start), start);
        for (var y = 399; y >= map.Top!.FarEdge; y--)
        {
            var box = new Box(x, y, 300, 200);
            var drawn = scale.DrawnBox(scale.PlacementFor(map, box), box);
            AssertNear(previous, drawn, 7);
            previous = drawn;
        }
    }

    [Fact]
    public void Sweeping_a_box_down_across_the_top_seam_never_jumps()
    {
        var map = Map(top: true);
        var scale = new CanvasScale();
        var start = new Box(400, map.Top!.FarEdge, 300, 200);
        var previous = scale.DrawnBox(scale.PlacementFor(map, start), start);
        for (var y = start.Y + 1; y <= 400; y++)
        {
            var box = new Box(400, y, 300, 200);
            var drawn = scale.DrawnBox(scale.PlacementFor(map, box), box);
            AssertNear(previous, drawn, 7);
            previous = drawn;
        }
    }

    [Fact]
    public void A_point_round_trips_through_the_placement_and_its_inverse()
    {
        var map = Map(top: true);
        var scale = new CanvasScale();
        var box = new Box(map.Right!.Seam + 150, 20, 300, 200);
        var placement = scale.PlacementFor(map, box);
        Assert.True(placement.IsAxisAlignedScale);
        Assert.False(placement.IsIdentity);
        Assert.True(placement.TryInvert(out var inverse));
        for (var i = 0; i < 20; i++)
        {
            var x = box.X + (i * 13.7);
            var y = box.Y + (i * 9.1);
            var (sx, sy) = placement.Map(x, y);
            var (cx, cy) = inverse.Map(sx, sy);
            Assert.Equal(x, cx, 9);
            Assert.Equal(y, cy, 9);
        }
    }

    [Fact]
    public void A_side_box_puts_its_inner_edge_on_the_zone_and_its_center_row_on_the_fan()
    {
        var map = Map();
        var scale = new CanvasScale();
        var box = new Box(map.Right!.Seam + 60, 500, 200, 100);
        var placement = scale.PlacementFor(map, box);
        var (anchorX, anchorY) = scale.AnchorFor(map, box);
        Assert.Equal(box.X, anchorX);
        Assert.Equal(550, anchorY);
        var (x, y) = placement.Map(anchorX, anchorY);
        var (expectedX, expectedY) = map.ToScreenPoint(anchorX, anchorY);
        Assert.Equal(expectedX, x, 9);
        Assert.Equal(expectedY, y, 9);
        Assert.NotEqual(550, y, 3);
    }

    [Fact]
    public void A_corner_box_puts_its_inner_corner_on_the_ring_and_takes_the_smaller_axis_scale()
    {
        var map = Map(top: true);
        var scale = new CanvasScale { MinScale = 0.1 };
        var box = new Box(map.Right!.Seam + 40, map.Top!.Seam - 30 - 120, 200, 120);
        var (anchorX, anchorY) = scale.AnchorFor(map, box);
        Assert.Equal(box.X, anchorX);
        Assert.Equal(box.Bottom, anchorY);
        var placement = scale.PlacementFor(map, box);
        var (x, y) = placement.Map(anchorX, anchorY);
        var (ringX, ringY) = map.ToScreenPoint(anchorX, anchorY);
        Assert.Equal(ringX, x, 9);
        Assert.Equal(ringY, y, 9);
        var kx = scale.AxisScale(map.Right, box.X);
        var ky = scale.AxisScale(map.Top, box.Bottom);
        Assert.Equal(Math.Max(0.1, Math.Min(kx, ky)), placement.M11, 12);
    }

    [Fact]
    public void A_park_puts_the_drawn_outer_edge_on_the_screen_edge()
    {
        var map = Map();
        var scale = new CanvasScale();
        var right = map.Right!;
        var box = new Box(400, 300, 80, 150);
        var target = scale.ParkTarget(map, right, box);
        var parked = box with { X = target };
        var drawn = scale.DrawnBox(scale.PlacementFor(map, parked), parked);
        var placement = scale.PlacementFor(map, parked);
        var outer = (placement.M11 * parked.Right) + placement.M13;
        Assert.True(outer <= right.ScreenEdge + 1e-6, $"outer {outer} past {right.ScreenEdge}");
        Assert.True(right.ScreenEdge - outer < 1.0, $"outer {outer} short of {right.ScreenEdge}");
        Assert.True(drawn.Right <= Width);

        var left = map.Left!;
        var leftTarget = scale.ParkTarget(map, left, box);
        var leftParked = box with { X = leftTarget };
        var leftPlacement = scale.PlacementFor(map, leftParked);
        var leftOuter = (leftPlacement.M11 * leftParked.X) + leftPlacement.M13;
        Assert.True(leftOuter >= left.ScreenEdge - 1e-6);
        Assert.True(leftOuter - left.ScreenEdge < 1.0, $"outer {leftOuter} short of {left.ScreenEdge}");
    }

    [Fact]
    public void A_window_too_wide_to_fit_parks_where_it_overhangs_least()
    {
        var map = Map();
        var scale = new CanvasScale();
        var right = map.Right!;
        var box = new Box(400, 300, 600, 150);
        var target = scale.ParkTarget(map, right, box);
        var least = double.PositiveInfinity;
        for (var x = right.Seam; x <= right.FarEdge; x++)
        {
            least = Math.Min(least, Overhang(scale, map, box with { X = x }));
        }

        Assert.True(least > 0);
        Assert.Equal(least, Overhang(scale, map, box with { X = target }), 9);
    }

    [Fact]
    public void A_corner_park_keeps_both_drawn_outer_edges_inside_the_screen()
    {
        var map = Map(top: true);
        var scale = new CanvasScale();
        var box = new Box(400, 300, 80, 60);
        var (x, y) = scale.CornerParkTarget(map, map.Right!, map.Top!, box);
        var parked = new Box(x, y, box.Width, box.Height);
        var placement = scale.PlacementFor(map, parked);
        var outerX = (placement.M11 * parked.Right) + placement.M13;
        var outerY = (placement.M22 * parked.Y) + placement.M23;
        Assert.True(outerX <= map.Right!.ScreenEdge + 1e-6);
        Assert.True(outerY >= map.Top!.ScreenEdge - 1e-6);
        Assert.True(x > map.Right.Seam && parked.Bottom < map.Top.Seam, $"parked at {x},{y}");
    }

    [Fact]
    public void A_drag_keeps_the_grabbed_point_under_the_cursor()
    {
        var map = Map();
        var scale = new CanvasScale();
        var box = new Box(map.Right!.Seam + 90, 200, 500, 300);
        var placement = scale.DragPlacementFor(map, box, box.X + 123.5, box.Y + 40.25, 911.0, 233.0);
        var (x, y) = placement.Map(box.X + 123.5, box.Y + 40.25);
        Assert.Equal(911.0, x, 9);
        Assert.Equal(233.0, y, 9);
        Assert.Equal(scale.ScaleFor(map, box), placement.M11, 12);
    }

    [Fact]
    public void The_field_inverse_moves_the_canvas_position_monotonically_across_the_retreat()
    {
        var map = Map();
        var previous = double.NegativeInfinity;
        for (var cursor = 700.0; cursor < Width; cursor += 0.5)
        {
            var (canvasX, _) = map.ToCanvasPoint(cursor, 300);
            Assert.True(canvasX >= previous);
            previous = canvasX;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_resize_puts_the_grabbed_edge_on_the_cursor_and_keeps_the_fixed_corner(bool inner)
    {
        var map = Map();
        var scale = new CanvasScale();
        var start = new Box(map.Right!.Seam + 60, 200, 300, 200);
        var rest = scale.PlacementFor(map, start);
        var fixedCanvasX = inner ? start.Right : start.X;
        var (fixedX, fixedY) = rest.Map(fixedCanvasX, start.Y);
        var cursorX = inner ? fixedX - 150.0 : fixedX + 70.0;
        var (k, canvasX, _) = scale.ResizeCursor(map, start, left: inner, right: !inner, top: false, bottom: false, fixedX, fixedY, cursorX, 400);
        var width = (int)Math.Round(Math.Abs(canvasX - fixedCanvasX));
        var resized = inner ? start with { X = fixedCanvasX - width, Width = width } : start with { Width = width };
        Assert.Equal(scale.ScaleFor(map, resized), k, 2);
        var placement = CanvasScale.About(k, fixedCanvasX, start.Y, fixedX, fixedY);
        var (movedX, movedY) = placement.Map(fixedCanvasX, start.Y);
        Assert.Equal(fixedX, movedX, 9);
        Assert.Equal(fixedY, movedY, 9);
        var edge = inner ? resized.X : resized.Right;
        var (drawnEdge, _) = placement.Map(edge, start.Y);
        Assert.True(Math.Abs(drawnEdge - cursorX) <= 1.0, $"edge {drawnEdge} cursor {cursorX}");
    }

    [Fact]
    public void A_corner_resize_on_the_inner_edges_solves_one_scale()
    {
        var map = Map(top: true);
        var scale = new CanvasScale { MinScale = 0.1 };
        var start = new Box(map.Right!.Seam + 40, map.Top!.Seam - 60 - 150, 250, 150);
        var rest = scale.PlacementFor(map, start);
        var (fixedX, fixedY) = rest.Map(start.Right, start.Y);
        var (k, canvasX, canvasY) = scale.ResizeCursor(map, start, left: true, right: false, top: false, bottom: true, fixedX, fixedY, fixedX - 120, fixedY + 80);
        var width = (int)Math.Round(start.Right - canvasX);
        var height = (int)Math.Round(canvasY - start.Y);
        var resized = new Box(start.Right - width, start.Y, width, height);
        Assert.Equal(scale.ScaleFor(map, resized), k, 2);
    }

    [Fact]
    public void Blend_is_a_lerp_between_two_scales()
    {
        var from = CanvasScale.About(0.5, 100, 100, 300, 200);
        var to = RenderTransform.Identity;
        var half = CanvasScale.Blend(from, to, 0.5);
        Assert.True(half.IsAxisAlignedScale);
        Assert.Equal(0.75, half.M11, 12);
        Assert.Equal(from, CanvasScale.Blend(from, to, 0));
        Assert.Equal(to, CanvasScale.Blend(from, to, 1));
    }

    [Fact]
    public void Axis_aligned_scale_admits_scales_and_translations_only()
    {
        Assert.True(RenderTransform.Identity.IsAxisAlignedScale);
        Assert.True(RenderTransform.Translation(10, -4).IsAxisAlignedScale);
        Assert.True(RenderTransform.Scale(0.5, 0.25).IsAxisAlignedScale);
        Assert.False(RenderTransform.Scale(-1, 1).IsAxisAlignedScale);
        Assert.False(RenderTransform.RotationAbout(0.3, 10, 10).IsAxisAlignedScale);
        Assert.False(new RenderTransform(1, 0, 0, 0, 1, 0, 0.001, 0, 1).IsAxisAlignedScale);
    }

    [Fact]
    public void The_hand_scale_eases_from_one_at_the_seam_to_the_floor_at_the_screen_edge()
    {
        var map = Map(top: true);
        var scale = new CanvasScale { MinScale = 0.35 };
        var right = map.Right!;
        Assert.Equal(1.0, scale.ScaleAtScreen(map, 500, 400));
        Assert.Equal(1.0, scale.ScaleAtScreen(map, right.Seam, 400));
        Assert.Equal(0.35, scale.ScaleAtScreen(map, right.ScreenEdge, 400), 12);
        Assert.Equal(0.35, scale.ScaleAtScreen(map, right.ScreenEdge + 50, 400), 12);
        Assert.Equal(1.0 - (0.65 * 0.5), scale.ScaleAtScreen(map, right.Seam + (right.ZoneWidth / 2.0), 400), 12);
        Assert.Equal(scale.ScaleAtScreen(map, right.Seam + 90, 400), scale.ScaleAtScreen(map, right.Seam + 90, 20 + map.Top!.ScreenEdge + 60), 12);
        var previous = 1.0;
        for (var x = (double)right.Seam; x <= right.ScreenEdge; x += 0.5)
        {
            var k = scale.ScaleAtScreen(map, x, 400);
            Assert.True(k <= previous && previous - k < 0.01, $"step at {x}: {previous} -> {k}");
            previous = k;
        }
    }

    [Fact]
    public void An_anchored_placement_is_the_identity_when_flat()
    {
        var map = Map(top: true);
        var scale = new CanvasScale();
        var box = new Box(300, 300, 200, 100);
        Assert.True(scale.AnchoredPlacementFor(map, box, 380, 310).IsIdentity);
    }

    [Theory]
    [InlineData(0.1)]
    [InlineData(0.5)]
    [InlineData(0.9)]
    public void A_hand_placement_reaches_the_floor_exactly_when_the_leading_edge_meets_the_screen_edge(double grab)
    {
        var map = Map();
        var scale = new CanvasScale { MinScale = 0.35 };
        var right = map.Right!;
        var box = new Box(0, 300, 300, 200);
        var anchorX = box.X + (grab * box.Width);
        var previous = 1.0;
        var floorAt = double.NaN;
        for (var cursor = 500.0; cursor <= right.ScreenEdge; cursor += 1)
        {
            var placement = scale.HandPlacementFor(map, box, anchorX, 350, cursor, 350);
            var k = placement.M11;
            var outer = (k * box.Right) + placement.M13;
            Assert.True(k <= previous + 1e-9 && previous - k < 0.05, $"the scale jumped at {cursor}: {previous} -> {k}");
            Assert.Equal(cursor, placement.Map(anchorX, 350).X, 6);
            if (k > scale.MinScale + 1e-6)
            {
                Assert.True(outer <= right.ScreenEdge + 1e-6, $"the window left the screen above the floor at {cursor}: {outer}");
            }
            else if (double.IsNaN(floorAt))
            {
                floorAt = cursor;
                Assert.True(Math.Abs(outer - right.ScreenEdge) < 1.5, $"the floor arrived with the outer edge at {outer}");
            }

            previous = k;
        }

        Assert.Equal(scale.MinScale, previous, 6);
        Assert.False(double.IsNaN(floorAt));
    }

    [Fact]
    public void A_hand_placement_starts_to_shrink_when_the_leading_edge_crosses_the_seam()
    {
        var map = Map();
        var scale = new CanvasScale { MinScale = 0.35 };
        var right = map.Right!;
        var box = new Box(0, 300, 300, 200);
        var before = scale.HandPlacementFor(map, box, 50, 350, right.Seam - 251, 350);
        var after = scale.HandPlacementFor(map, box, 50, 350, right.Seam - 240, 350);
        Assert.Equal(1.0, before.M11);
        Assert.True(after.M11 < 1.0);
    }

    private static double Overhang(CanvasScale scale, CanvasWarpTransform map, in Box box)
    {
        var placement = scale.PlacementFor(map, box);
        return (placement.M11 * box.Right) + placement.M13 - map.Right!.ScreenEdge;
    }

    private static void AssertNear(in Box previous, in Box drawn, int bound)
    {
        Assert.True(Math.Abs(drawn.X - previous.X) <= bound, $"x {previous} -> {drawn}");
        Assert.True(Math.Abs(drawn.Y - previous.Y) <= bound, $"y {previous} -> {drawn}");
        Assert.True(Math.Abs(drawn.Right - previous.Right) <= bound, $"right {previous} -> {drawn}");
        Assert.True(Math.Abs(drawn.Bottom - previous.Bottom) <= bound, $"bottom {previous} -> {drawn}");
    }
}
