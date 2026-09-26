using Xunit;

namespace Basin.Tests;

public sealed class TinyCompOverviewTests
{
    [Fact]
    public void The_full_layout_matches_the_worked_example()
    {
        var right = TinyComp.OverviewLayout.Full(3440, 3440, 1720, 1, 0.75, 0.10, 3440);
        Assert.True(right.Active);
        Assert.Equal(430, right.Border);
        Assert.Equal(344, right.Shelf);
        Assert.Equal(86, right.Slope);

        var end = TinyComp.OverviewLayout.At(right, 1.0, 3440, 3440, 1720, 1, 0.75, 0.4);
        Assert.Equal(0.75, end.Zoom, 12);
        Assert.Equal(115, end.Zone);
        Assert.Equal(573 - 115, end.Shelf);
        Assert.Equal(0.4 / 0.75, end.EdgeScale, 12);
        Assert.Equal(860, (int)Math.Round(right.Shelf / 0.4));
    }

    [Fact]
    public void At_no_progress_every_side_is_the_identity()
    {
        var right = TinyComp.OverviewLayout.Full(3440, 3440, 1720, 1, 0.75, 0.10, 3440);
        var start = TinyComp.OverviewLayout.At(right, 0.0, 3440, 3440, 1720, 1, 0.75, 0.4);
        Assert.Equal(0, start.Zone);
        Assert.Equal(1.0, start.Zoom, 12);
        Assert.Equal(1.0, start.EdgeScale, 12);

        var early = TinyComp.OverviewLayout.At(right, 0.02, 3440, 3440, 1720, 1, 0.75, 0.4);
        Assert.True(early.Zone > 0, "a side comes in as soon as it has a pixel");
        Assert.True(early.EdgeScale < 1.0 && early.EdgeScale > 0.9);
        var half = TinyComp.OverviewLayout.At(right, 0.5, 3440, 3440, 1720, 1, 0.75, 0.4);
        Assert.InRange(half.Zone, early.Zone, 115);
        Assert.Equal(0.875, half.Zoom, 12);
    }

    [Fact]
    public void A_thin_border_keeps_the_least_slope_or_leaves_the_side_off()
    {
        var wide = TinyComp.OverviewLayout.Full(3440, 3440, 1720, 1, 0.75, 0.12, 3440);
        Assert.Equal(TinyComp.OverviewLayout.MinSlope(3440), wide.Slope);
        Assert.Equal(430 - 34, wide.Shelf);

        var panel = TinyComp.OverviewLayout.Full(0, 200, 720, -1, 0.75, 0.10, 1440);
        Assert.False(panel.Active);
        Assert.True(panel.Border < 0);
        var off = TinyComp.OverviewLayout.At(panel, 1.0, 0, 200, 720, -1, 0.75, 0.4);
        Assert.Equal(0, off.Zone);
    }

    [Fact]
    public void The_edge_scale_never_reaches_one()
    {
        var right = TinyComp.OverviewLayout.Full(1920, 1920, 960, 1, 0.75, 0.10, 1920);
        var end = TinyComp.OverviewLayout.At(right, 1.0, 1920, 1920, 960, 1, 0.75, 0.75);
        Assert.Equal(TinyComp.OverviewLayout.MaxEdgeScale, end.EdgeScale, 12);
    }

    [Fact]
    public void The_threshold_goes_in_and_out_with_hysteresis()
    {
        const double inward = 0.667;
        const double outward = 0.333;
        Assert.False(TinyComp.OverviewThreshold.Step(false, 0.5, inward, outward));
        Assert.True(TinyComp.OverviewThreshold.Step(false, inward, inward, outward));
        Assert.True(TinyComp.OverviewThreshold.Step(false, 1.4, inward, outward));
        Assert.True(TinyComp.OverviewThreshold.Step(true, 0.5, inward, outward));
        Assert.False(TinyComp.OverviewThreshold.Step(true, outward, inward, outward));
        Assert.False(TinyComp.OverviewThreshold.Step(true, -0.5, inward, outward));

        var held = false;
        var flips = 0;
        foreach (var depth in new[] { 0.6, 0.66, 0.64, 0.668, 0.65, 0.7, 0.62, 0.35, 0.32, 0.34, 0.33, 0.36, 0.1 })
        {
            var next = TinyComp.OverviewThreshold.Step(held, depth, inward, outward);
            flips += next != held ? 1 : 0;
            held = next;
        }

        Assert.Equal(2, flips);
        Assert.False(held);
    }

    [Fact]
    public void A_corner_depth_is_the_deeper_side()
    {
        Assert.Equal(0.8, TinyComp.OverviewThreshold.CornerDepth(0.8, 0.3), 12);
        Assert.Equal(0.9, TinyComp.OverviewThreshold.CornerDepth(-0.2, 0.9), 12);
        Assert.True(TinyComp.OverviewThreshold.Step(false, TinyComp.OverviewThreshold.CornerDepth(0.5, 0.7), 0.667, 0.333));
    }

    [Fact]
    public void The_step_layout_matches_the_worked_example()
    {
        var right = TinyComp.OverviewLayout.StepFull(3440, 3440, 1720, 1, 0.75, 0.04, 3440);
        Assert.True(right.Active);
        Assert.Equal(430, right.Border);
        Assert.Equal(138, right.Slope);
        Assert.Equal(292, right.Shelf);

        var map = new Basin.Effects.CanvasStepMap();
        var box = new Box(0, 0, 3440, 1440);
        Span<TinyComp.OverviewSide> full =
        [
            TinyComp.OverviewLayout.StepFull(0, 0, 1720, -1, 0.75, 0.04, 3440),
            right,
            TinyComp.OverviewLayout.StepFull(0, 0, 720, -1, 0.75, 0.04, 1440),
            TinyComp.OverviewLayout.StepFull(1440, 1440, 720, 1, 0.75, 0.04, 1440),
        ];
        _ = TinyComp.OverviewLayout.LayoutStep(map, box, box, full, 1.0, 0.75, 0.4);
        Assert.Equal(3148, map.Inner.Right, 9);
        var rho = 1.0 + (2.0 * 0.04 / 0.75);
        Assert.Equal(1720 + ((3440 - 1720) * 0.75 * rho), map.Inner.Right, 0);
        Assert.Equal(720 - (720 * 0.75 * rho), map.Inner.Y, 0);
        var rayX = (map.Inner.Right - 1720) / (map.Outline.Right - 1720);
        var rayY = (720 - map.Inner.Y) / (720 - map.Outline.Y);
        Assert.True(Math.Abs(rayX - rayY) < 0.002, $"{rayX} vs {rayY}");
        Assert.Equal(5290, 1720 + ((map.Inner.Right - 1720) / 0.4), 9);
        Assert.Equal(6020, 1720 + ((3440 - 1720) / 0.4), 9);
    }

    [Fact]
    public void A_border_below_the_wall_and_the_least_shelf_has_no_step()
    {
        var thin = TinyComp.OverviewLayout.StepFull(0, 150, 720, -1, 0.75, 0.04, 1440);
        Assert.False(thin.Active);
        Assert.Equal(29, TinyComp.OverviewLayout.MinShelf(1440));
        Assert.Equal(0.0, TinyComp.OverviewLayout.StepWallAt(thin, 1.0, 0, 150, 720, -1, 0.75));
        var right = TinyComp.OverviewLayout.StepFull(3440, 3440, 1720, 1, 0.75, 0.04, 3440);
        Assert.Equal(69, TinyComp.OverviewLayout.StepWallAt(right, 0.5, 3440, 3440, 1720, 1, 0.75), 9);
        Assert.Equal(0.0, TinyComp.OverviewLayout.StepWallAt(right, 0.0, 3440, 3440, 1720, 1, 0.75));
    }
}
