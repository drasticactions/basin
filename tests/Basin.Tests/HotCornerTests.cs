using Basin.Seat;
using Xunit;

namespace Basin.Tests;

public sealed class HotCornerTests
{
    private static readonly Box Output = new(100, 50, 1920, 1080);

    [Fact]
    public void It_fires_once_after_the_delay_and_rearms_on_leave()
    {
        var corner = new HotCorner { Corner = ScreenCorner.TopLeft, Size = 6, DelayMs = 150 };
        Assert.False(corner.Motion(Output, 102, 51, 1000));
        Assert.True(corner.IsArmed);
        Assert.Equal(1150u, corner.DueMs);
        Assert.False(corner.Motion(Output, 100, 50, 1100));
        Assert.True(corner.Motion(Output, 100, 50, 1150));
        Assert.False(corner.Motion(Output, 101, 50, 1300));
        Assert.False(corner.Poll(2000));

        Assert.False(corner.Motion(Output, 400, 400, 2100));
        Assert.False(corner.IsArmed);
        Assert.False(corner.Motion(Output, 100, 50, 2200));
        Assert.True(corner.Poll(2350));
    }

    [Fact]
    public void A_fast_pass_through_the_square_does_not_fire()
    {
        var corner = new HotCorner { Corner = ScreenCorner.TopRight, Size = 6, DelayMs = 150 };
        Assert.False(corner.Motion(Output, 2010, 60, 0));
        Assert.False(corner.Motion(Output, 2017, 52, 16));
        Assert.False(corner.Motion(Output, 2000, 70, 32));
        Assert.False(corner.Poll(400));
    }

    [Fact]
    public void Only_the_chosen_corner_arms_and_none_never_does()
    {
        var corner = new HotCorner { Corner = ScreenCorner.BottomRight, DelayMs = 0 };
        Assert.False(corner.Motion(Output, 100, 50, 0));
        Assert.True(corner.Motion(Output, 2019, 1129, 0));
        corner.Corner = ScreenCorner.None;
        corner.Reset();
        Assert.False(corner.Motion(Output, 2019, 1129, 10));
        Assert.Equal(ScreenCorner.BottomLeft, HotCorner.At(Output, 103, 1127, 6));
        Assert.Equal(ScreenCorner.None, HotCorner.At(Output, 900, 600, 6));
    }
}
