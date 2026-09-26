using Basin.Effects;
using Xunit;

namespace Basin.Tests;

public sealed class CanvasWarpTests
{
    private static CanvasWarp Left3440()
    {
        var warp = new CanvasWarp();
        Assert.True(warp.Layout(seam: 413, direction: -1, zoneWidth: 413, extension: 1720, edgeScale: 0.2));
        return warp;
    }

    [Fact]
    public void The_defaults_derive_the_documented_exponent()
    {
        var warp = Left3440();
        Assert.Equal(18.94, warp.Exponent, 2);
        Assert.Equal(0.2, warp.EdgeScale, 6);
        Assert.Equal(413 - 1720, warp.FarEdge);
        Assert.Equal(0, warp.ScreenEdge);
        Assert.False(warp.Layout(413, -1, 413, 1720, 0.2));
    }

    [Fact]
    public void ToScreen_is_the_identity_in_the_flat_span_and_at_the_seam()
    {
        var warp = Left3440();
        Assert.Equal(413, warp.ToScreen(413), 9);
        Assert.Equal(1000, warp.ToScreen(1000), 9);
        Assert.Equal(1.0, warp.ScaleAt(413), 9);
        Assert.Equal(1.0, warp.ScaleAt(2000), 9);
        Assert.False(warp.ContainsCanvas(413));
        Assert.True(warp.ContainsCanvas(412.5));
    }

    [Fact]
    public void ToScreen_is_monotone_and_reaches_the_zone_width_at_the_far_edge()
    {
        var warp = Left3440();
        var previous = warp.ToScreen(413);
        for (var x = 412; x >= warp.FarEdge; x--)
        {
            var screen = warp.ToScreen(x);
            Assert.True(screen < previous, $"canvas {x} maps to {screen}, after {previous}");
            previous = screen;
        }

        Assert.Equal(0, warp.ToScreen(warp.FarEdge), 6);
        Assert.Equal(-100, warp.ToScreen(warp.FarEdge - 500), 6);
        Assert.Equal(0.2, warp.ScaleAt(warp.FarEdge), 6);
        Assert.Equal(0.2, warp.ScaleAt(warp.FarEdge - 1), 6);
    }

    [Fact]
    public void ToCanvas_round_trips_within_a_hundredth_of_a_pixel()
    {
        var warp = Left3440();
        for (var x = warp.FarEdge; x <= 413; x++)
        {
            var screen = warp.ToScreen(x);
            var back = warp.ToCanvas(screen);
            Assert.True(Math.Abs(back - x) < 0.01, $"canvas {x} -> screen {screen} -> canvas {back}");
        }

        Assert.Equal(1000, warp.ToCanvas(1000), 9);
        Assert.Equal(warp.FarEdge - 25, warp.ToCanvas(-5), 9);
    }

    [Fact]
    public void The_right_zone_mirrors_the_left()
    {
        var left = Left3440();
        var right = new CanvasWarp();
        Assert.True(right.Layout(seam: 3440 - 413, direction: 1, zoneWidth: 413, extension: 1720, edgeScale: 0.2));
        Assert.Equal(3440, right.ScreenEdge);
        for (var offset = 0; offset <= 1720; offset += 7)
        {
            var leftScreen = 413 - left.ToScreen(413 - offset);
            var rightScreen = right.ToScreen((3440 - 413) + offset) - (3440 - 413);
            Assert.Equal(leftScreen, rightScreen, 6);
        }
    }

    [Fact]
    public void An_edge_scale_above_the_constraint_is_clamped()
    {
        var warp = new CanvasWarp();
        Assert.True(warp.Layout(seam: 100, direction: -1, zoneWidth: 100, extension: 1000, edgeScale: 0.5));
        Assert.Equal(0.8 * 100 / 1000, warp.EdgeScale, 9);
        Assert.Equal(0.08, CanvasWarp.ClampEdgeScale(0.5, 100, 1000), 9);
        Assert.Equal(0.05, CanvasWarp.ClampEdgeScale(0.05, 100, 1000), 9);
        Assert.Equal(0, warp.ToScreen(100 - 1000), 6);
        Assert.Equal(0.08, warp.ScaleAt(100 - 1000), 6);
    }

    [Fact]
    public void The_slope_fans_rows_from_the_centre_and_inverts()
    {
        var warp = new CanvasWarp();
        Assert.True(warp.Layout(413, -1, 413, 1720, 0.2, slope: 0.25, center: 720));
        Assert.Equal(0.25, warp.Slope, 9);
        Assert.Equal(720, warp.Center, 9);
        Assert.Equal(1.0, warp.FanAt(413), 9);
        Assert.Equal(1.0, warp.FanAt(2000), 9);
        Assert.Equal(1.25, warp.FanAt(warp.FarEdge), 9);
        Assert.Equal(1.25, warp.FanAtScreen(0), 9);
        Assert.Equal(1.0, warp.FanAtScreen(413), 9);
        Assert.Equal(720, warp.ToScreenY(warp.FarEdge, 720), 9);
        Assert.Equal(720 - 125, warp.ToScreenY(warp.FarEdge, 620), 9);
        Assert.Equal(720 + 250, warp.ToScreenY(warp.FarEdge, 920), 9);

        var previous = 1.0;
        for (var x = 412; x >= warp.FarEdge; x -= 3)
        {
            var fan = warp.FanAt(x);
            Assert.True(fan >= previous, $"fan {fan} at {x} after {previous}");
            previous = fan;
            var screenX = warp.ToScreen(x);
            foreach (var y in new[] { 0.0, 300.0, 720.0, 1100.0, 1440.0 })
            {
                var screenY = warp.ToScreenY(x, y);
                var back = warp.ToCanvasY(screenX, screenY);
                Assert.True(Math.Abs(back - y) < 0.02, $"canvas ({x},{y}) -> screen ({screenX},{screenY}) -> {back}");
            }
        }

        Assert.True(warp.Layout(413, -1, 413, 1720, 0.2, slope: 0.0, center: 720));
        Assert.Equal(1.0, warp.FanAt(warp.FarEdge), 9);
        Assert.Equal(620, warp.ToScreenY(warp.FarEdge, 620), 9);

        Assert.True(warp.Layout(413, -1, 413, 1720, 0.2, slope: -0.5, center: 720));
        Assert.Equal(0.5, warp.FanAt(warp.FarEdge), 9);
        Assert.Equal(720 - 50, warp.ToScreenY(warp.FarEdge, 620), 9);
        Assert.Equal(620, warp.ToCanvasY(0, 670), 9);
        Assert.True(warp.Layout(413, -1, 413, 1720, 0.2, slope: -5, center: 720));
        Assert.Equal(CanvasWarp.MinSlope, warp.Slope, 9);
        Assert.True(warp.FanAt(warp.FarEdge) > 0);
    }

    [Fact]
    public void A_zero_zone_is_the_identity()
    {
        var warp = new CanvasWarp();
        _ = warp.Layout(seam: 0, direction: -1, zoneWidth: 0, extension: 500, edgeScale: 0.2);
        Assert.True(warp.IsIdentity);
        Assert.Equal(-200, warp.ToScreen(-200), 9);
        Assert.Equal(-200, warp.ToCanvas(-200), 9);
        Assert.False(warp.ContainsCanvas(-200));
        Assert.False(warp.Layout(0, -1, 0, 500, 0.2));
    }

    private static CanvasWarp RightTerrace(double scale = 0.4, double slope = 0.25)
    {
        var warp = new CanvasWarp(1);
        Assert.True(warp.LayoutTerrace(
            seam: 3440 - 344 - 275, direction: 1, zoneWidth: 275, shelfWidth: 344, shelfScale: scale,
            exponent: CanvasWarp.TerraceExponent, slope: slope, center: 720));
        return warp;
    }

    [Fact]
    public void A_terrace_derives_its_extension_so_the_slope_ends_at_the_foot()
    {
        for (var tenth = 1; tenth <= 9; tenth++)
        {
            var scale = tenth / 10.0;
            var warp = RightTerrace(scale);
            Assert.True(warp.Terrace);
            Assert.Equal(CanvasWarp.TerraceExtension(275, scale, CanvasWarp.TerraceExponent), warp.Extension);
            Assert.Equal(warp.Foot, warp.ToScreen(warp.FarEdge), 6);
            Assert.Equal(3440, warp.OuterEdge);
            Assert.Equal(scale, warp.EdgeScale, 9);
            Assert.InRange(warp.Exponent, 1.8, 2.2);
        }

        Assert.Equal(458, CanvasWarp.TerraceExtension(275, 0.4, 2.0));
    }

    [Fact]
    public void A_terrace_scale_and_fan_are_smooth_at_the_foot()
    {
        foreach (var slope in new[] { 0.25, 0.0, -0.4 })
        {
            var warp = RightTerrace(slope: slope);
            var far = warp.FarEdge;
            var inside = (warp.ToScreen(far) - warp.ToScreen(far - 0.5)) / 0.5;
            var outside = (warp.ToScreen(far + 0.5) - warp.ToScreen(far)) / 0.5;
            Assert.True(Math.Abs(inside - outside) < 1e-3, $"slope {slope}: {inside} against {outside}");
            Assert.True(Math.Abs(warp.ScaleAt(far - 0.5) - warp.ScaleAt(far + 0.5)) < 1e-3);
            var fanInside = (warp.FanAt(far) - warp.FanAt(far - 0.5)) / 0.5;
            var fanOutside = (warp.FanAt(far + 0.5) - warp.FanAt(far)) / 0.5;
            Assert.True(Math.Abs(fanInside - fanOutside) < 1e-3, $"slope {slope}: fan {fanInside} against {fanOutside}");
        }
    }

    [Fact]
    public void A_terrace_shelf_is_a_uniform_scale_that_inverts()
    {
        var warp = RightTerrace();
        for (var depth = 0; depth < 2000; depth += 37)
        {
            var x = warp.FarEdge + depth;
            Assert.Equal(warp.Foot + (0.4 * depth), warp.ToScreen(x), 6);
            Assert.Equal(720 + ((100 - 720) * 0.4), warp.ToScreenY(x, 100), 6);
            Assert.Equal(x, warp.ToCanvas(warp.ToScreen(x)), 6);
            Assert.Equal(100, warp.ToCanvasY(warp.ToScreen(x), warp.ToScreenY(x, 100)), 6);
        }
    }

    [Fact]
    public void The_terrace_fan_runs_from_one_to_the_shelf_scale_and_the_slope_bows_it()
    {
        var warp = RightTerrace();
        Assert.Equal(1.0, warp.FanAt(warp.Seam), 9);
        Assert.Equal(0.4, warp.FanAt(warp.FarEdge), 9);
        Assert.Equal(0.4, warp.FanAt(warp.FarEdge + 500), 9);
        Assert.Equal(0.95, CanvasWarp.TerraceFan(0.5, 0.4, 0.25), 9);
        Assert.True(CanvasWarp.TerraceFan(0.5, 0.4, 0.25) > CanvasWarp.TerraceFan(0.5, 0.4, 0.0));
        Assert.True(CanvasWarp.TerraceFan(0.5, 0.4, -0.3) < CanvasWarp.TerraceFan(0.5, 0.4, 0.0));

        var clamped = RightTerrace(slope: -0.9);
        Assert.True(clamped.Slope > -0.9, $"the slope {clamped.Slope} was not clamped");
        Assert.InRange(clamped.Slope, -0.65, -0.55);
        Assert.True(clamped.MinFan >= CanvasWarp.MinTerraceFan - 1e-9, $"the fan falls to {clamped.MinFan}");
        for (var x = clamped.Seam; x <= clamped.FarEdge; x++)
        {
            Assert.True(clamped.FanAt(x) >= CanvasWarp.MinTerraceFan - 1e-4);
        }
    }

    [Fact]
    public void Laying_out_warp_after_terrace_restores_the_warp_curve()
    {
        var warp = RightTerrace();
        Assert.True(warp.Layout(seam: 3440 - 413, direction: 1, zoneWidth: 413, extension: 1720, edgeScale: 0.2));
        Assert.False(warp.Terrace);
        Assert.Equal(0, warp.ShelfWidth);
        Assert.Equal(18.94, warp.Exponent, 2);
        Assert.Equal(1.0, warp.FanAt(warp.FarEdge + 10), 9);
    }
}
