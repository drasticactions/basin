using Xunit;

namespace Basin.Tests;

public sealed class PresentTimingScheduleTests
{
    [Fact]
    public void Relative_targets_count_refreshes_across_re_anchored_vblanks()
    {
        const long anchor = 1_000_000_000, cycle = 16_666_667;
        Assert.True(PresentTiming.TargetReached(1_033_333_334, 1_033_332_999, anchor, cycle, cycle));
        Assert.True(PresentTiming.TargetReached(1_033_333_334, 1_033_334_001, anchor, cycle, cycle));
        Assert.False(PresentTiming.TargetReached(1_033_333_334, 1_016_666_000, anchor, cycle, cycle));
        Assert.False(PresentTiming.TargetReached(1_025_000_000, 1_016_666_667, anchor, cycle, cycle));
        Assert.True(PresentTiming.TargetReached(1_025_000_000, 1_033_333_334, anchor, cycle, cycle));
        Assert.True(PresentTiming.TargetReached(900_000_000, 0, anchor, cycle, cycle));
    }

    [Fact]
    public void Counting_rounds_a_re_phased_grid_to_the_nearest_refresh()
    {
        const long anchor = 1_000_000_000, cycle = 16_666_667;
        Assert.True(PresentTiming.TargetReached(anchor + (2 * cycle), anchor + (2 * cycle) - 7_000_000, anchor, cycle, cycle));
        Assert.False(PresentTiming.TargetReached(anchor + (2 * cycle), anchor + (2 * cycle) - 9_000_000, anchor, cycle, cycle));
        Assert.True(PresentTiming.TargetReached(120, 115, 100, 10, 10));
        Assert.False(PresentTiming.TargetReached(120, 114, 100, 10, 10));
    }

    [Fact]
    public void Targets_compare_times_when_the_anchor_is_off_the_current_grid()
    {
        const long anchor = 1_000_000_000;
        Assert.False(PresentTiming.TargetReached(1_050_000_000, 1_041_666_667, anchor, 25_000_000, 16_666_667));
        Assert.True(PresentTiming.TargetReached(1_050_000_000, 1_058_333_334, anchor, 25_000_000, 16_666_667));
        Assert.False(PresentTiming.TargetReached(1_033_333_334, 1_033_332_999, anchor, 16_666_667, 0));
        Assert.True(PresentTiming.TargetReached(1_033_333_334, 1_033_333_334, 0, 0, 16_666_667));
    }

    [Fact]
    public void Without_an_anchor_a_fixed_grid_keeps_the_truncation_tolerance()
    {
        Assert.True(PresentTiming.TargetReached(1_033_333_334, 1_033_333_334 - 400, 0, 0, 16_666_667));
        Assert.False(PresentTiming.TargetReached(1_033_333_334, 1_033_333_334 - 100_000, 0, 0, 16_666_667));
    }

    [Fact]
    public void Vrr_prediction_and_wakes_cannot_reuse_a_stale_vblank()
    {
        Assert.Equal(105, PresentTiming.Predict(100, 5, 10, 16));
        Assert.Equal(115, PresentTiming.Predict(100, 5, 99, 16));
        Assert.Equal(115, PresentTiming.TargetWake(120, 5));
        Assert.Null(PresentTiming.TargetWake(3, 5));
    }

    [Fact]
    public void Fixed_prediction_stays_on_the_refresh_grid_after_a_stale_vblank()
    {
        Assert.Equal(100, PresentTiming.PredictFixed(90, 100, 16));
        Assert.Equal(100, PresentTiming.PredictFixed(100, 100, 16));
        Assert.Equal(116, PresentTiming.PredictFixed(101, 100, 16));
        Assert.Equal(132, PresentTiming.PredictFixed(132, 100, 16));
        Assert.Equal(148, PresentTiming.PredictFixed(133, 100, 16));
        Assert.Equal(long.MaxValue, PresentTiming.PredictFixed(long.MaxValue - 1, long.MaxValue - 4, 16));
        Assert.Equal(101, PresentTiming.PredictFixed(101, 100, 0));
    }

    [Fact]
    public void A_latch_after_the_timer_wakeup_misses_that_vblank()
    {
        Assert.Equal(100, PresentTiming.PredictFixedLatch(96, 100, 96, 16, true));
        Assert.Equal(116, PresentTiming.PredictFixedLatch(97, 100, 96, 16, false));
        Assert.Equal(116, PresentTiming.PredictFixedLatch(99, 100, 96, 16, false));
        Assert.Equal(116, PresentTiming.PredictFixedLatch(105, 100, 96, 16, false));
        Assert.Equal(132, PresentTiming.PredictFixedLatch(113, 100, 96, 16, false));
        Assert.Equal(116, PresentTiming.PredictFixedLatch(101, 100, 96, 16, true));
        Assert.Equal(100, PresentTiming.PredictFixedLatch(97, 100, 101, 16, false));
    }
}
