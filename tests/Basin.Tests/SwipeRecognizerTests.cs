using Basin.Seat;
using Xunit;

namespace Basin.Tests;

public abstract class SwipeRecognizerTests
{
    protected abstract SwipeAxis Axis { get; }

    private bool Move(SwipeRecognizer recognizer, double along, double across, uint timeMs) =>
        Axis == SwipeAxis.Vertical ? recognizer.Update(across, along, timeMs) : recognizer.Update(along, across, timeMs);

    private SwipeRecognizer Started(uint fingers = 3, double width = 1000)
    {
        var recognizer = new SwipeRecognizer(fingers) { Axis = Axis };
        Assert.True(recognizer.Begin(fingers, width, 0));
        return recognizer;
    }

    [Fact]
    public void A_different_finger_count_is_never_claimed()
    {
        var recognizer = new SwipeRecognizer(3) { Axis = Axis };

        Assert.False(recognizer.Begin(2, 1000, 0));
        Assert.False(recognizer.IsActive);
        Assert.False(Move(recognizer, -100, 0, 10));
        Assert.Equal(SwipeOutcome.None, recognizer.End(false, 20));
        Assert.Equal(0, recognizer.Progress);
    }

    [Fact]
    public void A_width_of_zero_is_never_claimed()
    {
        var recognizer = new SwipeRecognizer(3) { Axis = Axis };

        Assert.False(recognizer.Begin(3, 0, 0));
        Assert.False(recognizer.IsActive);
    }

    [Fact]
    public void Progress_is_the_travel_as_a_fraction_of_the_width()
    {
        var recognizer = Started();

        Assert.True(Move(recognizer, 500, 0, 10));

        Assert.Equal(0.5, recognizer.Progress, 6);
        Assert.Equal(1, recognizer.Direction);
    }

    [Fact]
    public void The_vertical_component_moves_nothing()
    {
        var recognizer = Started();

        Move(recognizer, 0, 800, 10);

        Assert.Equal(0, recognizer.Progress);
        Assert.Equal(0, recognizer.Direction);
    }

    [Fact]
    public void Progress_stops_at_one_neighbour()
    {
        var recognizer = Started();

        Move(recognizer, 2500, 0, 10);
        Assert.Equal(1, recognizer.Progress, 6);

        Move(recognizer, -5000, 0, 20);
        Assert.Equal(-1, recognizer.Progress, 6);
    }

    [Fact]
    public void A_drag_that_reverses_tracks_the_fingers()
    {
        var recognizer = Started();

        Move(recognizer, 1400, 0, 10);
        Assert.Equal(1, recognizer.Progress, 6);

        Move(recognizer, -800, 0, 20);
        Assert.Equal(0.6, recognizer.Progress, 6);
    }

    [Fact]
    public void A_drag_against_a_limit_is_damped()
    {
        var recognizer = Started();
        recognizer.ClampHigh = true;

        Move(recognizer, 250, 0, 10);

        var quarter = recognizer.Progress;
        Assert.True(quarter > 0);
        Assert.True(quarter < 0.25);

        Move(recognizer, 250, 0, 20);

        Assert.True(recognizer.Progress > quarter);
        Assert.True(recognizer.Progress < 0.5);
    }

    [Fact]
    public void A_drag_against_a_limit_never_commits()
    {
        var recognizer = Started();
        recognizer.ClampHigh = true;

        Move(recognizer, 5000, 0, 10);

        Assert.Equal(SwipeOutcome.Cancel, recognizer.End(false, 20));
    }

    [Fact]
    public void The_other_direction_is_unaffected_by_one_limit()
    {
        var recognizer = Started();
        recognizer.ClampHigh = true;

        Move(recognizer, -600, 0, 10);

        Assert.Equal(-0.6, recognizer.Progress, 6);
        Assert.Equal(SwipeOutcome.Commit, recognizer.End(false, 20));
    }

    [Theory]
    [InlineData(490, SwipeOutcome.Cancel)]
    [InlineData(510, SwipeOutcome.Commit)]
    public void Half_the_width_is_the_threshold(double travel, SwipeOutcome expected)
    {
        var recognizer = Started();

        for (var step = 1; step <= 3; step++)
        {
            Move(recognizer, travel / 3, 0, (uint)(step * 300));
        }

        Assert.True(recognizer.Velocity < recognizer.FlingPerSecond);
        Assert.Equal(expected, recognizer.End(false, 940));
    }

    [Fact]
    public void A_short_fast_flick_commits()
    {
        var recognizer = Started();

        Move(recognizer, 200, 0, 10);

        Assert.True(recognizer.Velocity >= recognizer.FlingPerSecond);
        Assert.Equal(SwipeOutcome.Commit, recognizer.End(false, 20));
    }

    [Fact]
    public void A_flick_back_toward_the_start_does_not_commit()
    {
        var recognizer = Started();

        Move(recognizer, 300, 0, 100);
        Move(recognizer, -200, 0, 110);

        Assert.True(recognizer.Progress > 0);
        Assert.True(recognizer.Velocity < 0);
        Assert.Equal(SwipeOutcome.Cancel, recognizer.End(false, 120));
    }

    [Fact]
    public void A_pause_before_release_spends_the_velocity()
    {
        var recognizer = Started();

        Move(recognizer, 200, 0, 10);

        Assert.Equal(SwipeOutcome.Cancel, recognizer.End(false, 400));
    }

    [Fact]
    public void A_backward_timestamp_contributes_no_velocity()
    {
        var recognizer = Started(width: 10000);

        Move(recognizer, 200, 0, 0);

        Assert.Equal(0, recognizer.Velocity);
        Assert.Equal(SwipeOutcome.Cancel, recognizer.End(false, 0));
    }

    [Fact]
    public void A_cancelled_gesture_never_commits()
    {
        var recognizer = Started();

        Move(recognizer, 900, 0, 10);

        Assert.Equal(SwipeOutcome.Cancel, recognizer.End(true, 20));
    }

    [Fact]
    public void A_gesture_that_never_moved_cancels()
    {
        var recognizer = Started();

        Assert.Equal(SwipeOutcome.Cancel, recognizer.End(false, 20));
    }

    [Fact]
    public void A_second_begin_starts_from_nothing()
    {
        var recognizer = Started();
        Move(recognizer, 900, 0, 10);

        Assert.True(recognizer.Begin(3, 1000, 20));

        Assert.Equal(0, recognizer.Progress);
        Assert.Equal(SwipeOutcome.Cancel, recognizer.End(false, 30));
    }

    [Fact]
    public void Abort_leaves_nothing_behind()
    {
        var recognizer = Started();
        Move(recognizer, 900, 0, 10);

        recognizer.Abort();

        Assert.False(recognizer.IsActive);
        Assert.Equal(0, recognizer.Progress);
        Assert.Equal(SwipeOutcome.None, recognizer.End(false, 20));
    }

    [Fact]
    public void The_thresholds_are_the_consumers_to_move()
    {
        var recognizer = Started();
        recognizer.CommitFraction = 0.25;

        Move(recognizer, 300, 0, 400);

        Assert.Equal(SwipeOutcome.Commit, recognizer.End(false, 440));
    }
}

public sealed class HorizontalSwipeRecognizerTests : SwipeRecognizerTests
{
    protected override SwipeAxis Axis => SwipeAxis.Horizontal;
}

public sealed class VerticalSwipeRecognizerTests : SwipeRecognizerTests
{
    protected override SwipeAxis Axis => SwipeAxis.Vertical;
}
