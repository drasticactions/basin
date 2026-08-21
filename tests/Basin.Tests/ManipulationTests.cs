using EightWm;
using Xunit;

namespace Basin.Tests;

public sealed class ManipulationTests
{
    private static Manipulation Pannable(double minimum = -1000, double maximum = 0) =>
        new() { Minimum = minimum, Maximum = maximum };

    [Fact]
    public void Nothing_moves_before_the_rail_slop_is_passed()
    {
        var pan = Pannable();
        pan.Begin(100, 100, 0);

        pan.Pan(106, 100, 10);

        Assert.Equal(PanAxis.Undecided, pan.Axis);
        Assert.Equal(0, pan.Raw);
    }

    [Fact]
    public void The_dominant_axis_wins_and_the_other_component_is_suppressed()
    {
        var pan = Pannable();
        pan.Begin(100, 100, 0);

        pan.Pan(60, 108, 10);
        pan.Pan(40, 200, 20);

        Assert.Equal(PanAxis.Horizontal, pan.Axis);
        Assert.Equal(-60, pan.Raw, 6);
    }

    [Fact]
    public void A_vertical_gesture_takes_the_vertical_rail()
    {
        var pan = Pannable();
        pan.Begin(100, 100, 0);

        pan.Pan(108, 60, 10);
        pan.Pan(300, 40, 20);

        Assert.Equal(PanAxis.Vertical, pan.Axis);
        Assert.Equal(-60, pan.Raw, 6);
    }

    [Fact]
    public void A_railed_scroller_ignores_a_gesture_on_the_other_axis()
    {
        var pan = Pannable();
        pan.Rail = PanAxis.Horizontal;
        pan.Begin(100, 100, 0);

        pan.Pan(108, 60, 10);
        pan.Pan(300, 40, 20);

        Assert.Equal(PanAxis.Vertical, pan.Axis);
        Assert.Equal(0, pan.Raw);
    }

    [Fact]
    public void A_railed_scroller_still_moves_on_its_own_axis()
    {
        var pan = Pannable();
        pan.Rail = PanAxis.Horizontal;
        pan.Begin(100, 100, 0);

        pan.Pan(60, 108, 10);
        pan.Pan(40, 200, 20);

        Assert.Equal(PanAxis.Horizontal, pan.Axis);
        Assert.Equal(-60, pan.Raw, 6);
    }

    [Fact]
    public void A_railed_scroller_still_reports_the_speed_of_the_gesture_it_ignores()
    {
        var pan = Pannable();
        pan.Rail = PanAxis.Horizontal;
        pan.Begin(100, 100, 0);

        pan.Pan(108, 60, 10);
        pan.Pan(300, 20, 20);

        Assert.Equal(PanAxis.Vertical, pan.Axis);
        Assert.Equal(0, pan.Raw);
        Assert.True(pan.Velocity < 0);
    }

    [Fact]
    public void A_release_with_speed_carries_on_past_the_finger()
    {
        var pan = Pannable();
        pan.Friction = 4;
        pan.Begin(500, 100, 0);
        pan.Pan(400, 100, 10);
        pan.Pan(300, 100, 20);

        var atRelease = pan.Raw;
        pan.Release(25);

        Assert.True(pan.IsSettling);
        pan.Advance(2000);
        Assert.False(pan.IsSettling);
        Assert.True(pan.Raw < atRelease, "inertia carried the pan further");
    }

    [Fact]
    public void A_release_after_a_pause_carries_nothing()
    {
        var pan = Pannable();
        pan.Begin(500, 100, 0);
        pan.Pan(400, 100, 10);
        pan.Pan(300, 100, 20);

        pan.Release(500);
        pan.Advance(2000);

        Assert.Equal(-200, pan.Raw, 6);
    }

    [Fact]
    public void Mandatory_snapping_always_lands_on_a_page()
    {
        var pan = Pannable(minimum: -3000, maximum: 0);
        pan.Snap = SnapKind.Mandatory;
        pan.SnapInterval = 1000;
        pan.Begin(500, 100, 0);
        pan.Pan(380, 100, 400);

        pan.Release(2000);
        pan.Advance(5000);

        Assert.Equal(0, pan.Raw, 6);
    }

    [Fact]
    public void Mandatory_snapping_takes_the_next_page_once_past_the_middle()
    {
        var pan = Pannable(minimum: -3000, maximum: 0);
        pan.Snap = SnapKind.Mandatory;
        pan.SnapInterval = 1000;
        pan.Begin(1000, 100, 0);
        pan.Pan(400, 100, 400);

        pan.Release(2000);
        pan.Advance(5000);

        Assert.Equal(-1000, pan.Raw, 6);
    }

    [Fact]
    public void Proximity_snapping_only_attracts_a_release_that_lands_near_a_point()
    {
        Span<double> points = [0, -1000, -2000];

        var near = Pannable(minimum: -2000, maximum: 0);
        near.Snap = SnapKind.Proximity;
        near.ProximityRange = 120;
        near.Begin(500, 100, 0);
        near.Pan(-440, 100, 400);
        near.Release(2000, points);
        near.Advance(5000);
        Assert.Equal(-1000, near.Raw, 6);

        var far = Pannable(minimum: -2000, maximum: 0);
        far.Snap = SnapKind.Proximity;
        far.ProximityRange = 120;
        far.Begin(500, 100, 0);
        far.Pan(0, 100, 400);
        far.Release(2000, points);
        far.Advance(5000);
        Assert.Equal(-500, far.Raw, 6);
    }

    [Fact]
    public void Overpan_compresses_and_springs_back()
    {
        var pan = Pannable(minimum: -1000, maximum: 0);
        pan.Begin(100, 100, 0);
        pan.Pan(400, 100, 400);

        Assert.Equal(300, pan.Raw, 6);
        Assert.True(pan.Offset < 300, "the overpan is compressed");
        Assert.True(pan.Offset > 0);

        pan.Release(2000);
        pan.Advance(5000);
        Assert.Equal(0, pan.Raw, 6);
    }

    [Fact]
    public void The_rubber_band_is_the_same_shape_at_both_ends()
    {
        var high = Pannable(minimum: -1000, maximum: 0);
        high.Begin(100, 100, 0);
        high.Pan(400, 100, 400);
        var over = high.Offset;

        var low = Pannable(minimum: -1000, maximum: 0);
        low.Begin(100, 100, 0);
        low.Pan(-1200, 100, 400);
        var under = low.Offset;

        Assert.Equal(over, -1000 - under, 6);
    }

    [Fact]
    public void A_chained_scroller_hands_the_remainder_to_its_parent()
    {
        var pan = Pannable(minimum: -100, maximum: 0);
        pan.RubberBand = false;
        pan.ChainToParent = true;
        pan.Begin(500, 100, 0);

        var spare = pan.Pan(560, 100, 10);
        Assert.Equal(0, pan.Raw, 6);
        Assert.Equal(60, spare, 6);

        spare = pan.Pan(400, 100, 20);
        Assert.Equal(-100, pan.Raw, 6);
        Assert.Equal(-60, spare, 6);
    }

    [Fact]
    public void An_unchained_scroller_keeps_everything_it_is_given()
    {
        var pan = Pannable(minimum: -100, maximum: 0);
        pan.RubberBand = false;

        pan.Begin(500, 100, 0);
        var spare = pan.Pan(560, 100, 10);

        Assert.Equal(0, spare);
        Assert.Equal(60, pan.Raw, 6);
    }

    [Fact]
    public void Settling_runs_on_the_deceleration_curve_and_ends_exactly()
    {
        var pan = Pannable(minimum: -1000, maximum: 0);
        pan.Snap = SnapKind.Mandatory;
        pan.SnapInterval = 1000;
        pan.Begin(1000, 100, 0);
        pan.Pan(200, 100, 400);
        pan.Release(2000);

        var start = pan.Raw;
        pan.Advance(2100);
        var quarter = pan.Raw;
        Assert.True(quarter < start);

        var linear = start + ((-1000 - start) * 0.1);
        Assert.True(quarter < linear, "deceleration is ahead of linear early on");

        while (pan.Advance(9000))
        {
        }

        Assert.Equal(-1000, pan.Raw, 6);
        Assert.False(pan.IsSettling);
    }

    [Fact]
    public void A_locked_axis_survives_a_release()
    {
        var pan = Pannable();
        pan.Locked = true;
        pan.Begin(100, 100, 0);
        pan.Pan(40, 100, 10);

        pan.Release(20);

        Assert.Equal(PanAxis.Horizontal, pan.Axis);
    }

    [Fact]
    public void Abort_stops_a_settle_where_it_stands()
    {
        var pan = Pannable(minimum: -1000, maximum: 0);
        pan.Begin(500, 100, 0);
        pan.Pan(300, 100, 10);
        pan.Release(15);
        pan.Advance(100);
        var where = pan.Raw;

        pan.Abort();

        Assert.False(pan.Advance(5000));
        Assert.Equal(where, pan.Raw, 6);
    }
}
