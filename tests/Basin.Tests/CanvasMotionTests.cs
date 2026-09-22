using Basin.Effects;
using Basin.Scene;
using Xunit;

namespace Basin.Tests;

public sealed class CanvasMotionTests
{
    private static FrameTick Tick(long millis) => new(millis * 1_000_000, 16_666_667);

    [Fact]
    public void A_motion_eases_from_start_to_target_and_lands_exactly()
    {
        var motion = new CanvasMotion();
        Assert.False(motion.Step(Tick(0), out _));
        motion.Begin(100, 500, 200_000_000);
        Assert.True(motion.IsRunning);

        Assert.Equal(100, motion.Current, 6);
        Assert.True(motion.Step(Tick(16), out var first));
        Assert.True(first >= 100 && first < 500, $"first step at {first}");
        var previous = first;
        var steps = 0;
        while (motion.Step(Tick(16 + (++steps * 16)), out var value))
        {
            Assert.True(value >= previous, $"step {steps}: {value} after {previous}");
            previous = value;
        }

        Assert.Equal(500, motion.Current, 6);
        Assert.False(motion.IsRunning);
        Assert.True(steps >= 12 && steps <= 15, $"{steps} steps");
    }

    [Fact]
    public void A_begin_during_a_motion_retargets_from_the_current_value()
    {
        var motion = new CanvasMotion();
        motion.Begin(0, 1000, 200_000_000);
        _ = motion.Step(Tick(16), out _);
        _ = motion.Step(Tick(116), out var midway);
        Assert.True(midway > 100 && midway < 1000, $"midway at {midway}");

        motion.Begin(0, 0, 200_000_000);
        Assert.True(motion.IsRunning);
        Assert.Equal(midway, motion.Current, 6);
        _ = motion.Step(Tick(132), out var restarted);
        Assert.True(restarted < midway && restarted > 0, $"restarted at {restarted} from {midway}");
        var steps = 0;
        while (steps < 100 && motion.Step(Tick(148 + (++steps * 16)), out _))
        {
        }

        Assert.True(steps < 100, "the retargeted motion lands");
        Assert.Equal(0, motion.Current, 6);
    }

    [Fact]
    public void A_zero_duration_or_no_travel_lands_at_once()
    {
        var motion = new CanvasMotion();
        motion.Begin(10, 20, 0);
        Assert.False(motion.IsRunning);
        Assert.Equal(20, motion.Current, 6);

        motion.Begin(20, 20, 200_000_000);
        Assert.False(motion.IsRunning);
    }
}
