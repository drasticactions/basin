using Basin.Shell.Xdg;
using Xunit;

namespace Basin.Tests;

public sealed class RestoreGeometryTests
{
    [Fact]
    public void Nothing_is_saved_until_something_is()
    {
        var restore = RestoreGeometry.None;
        Assert.False(restore.HasValue);
        Assert.False(restore.TryGet(out _));
    }

    [Fact]
    public void The_first_save_wins_and_a_second_is_ignored()
    {
        var restore = RestoreGeometry.None.Saving(new Box(10, 20, 300, 200));
        Assert.True(restore.HasValue);

        restore = restore.Saving(new Box(0, 0, 1920, 1080));
        Assert.True(restore.TryGet(out var frame));
        Assert.Equal(new Box(10, 20, 300, 200), frame);
    }

    [Fact]
    public void An_empty_frame_is_not_worth_saving()
    {
        var restore = RestoreGeometry.None.Saving(default);
        Assert.False(restore.HasValue);

        restore = restore.Saving(new Box(4, 8, 100, 80));
        Assert.True(restore.TryGet(out var frame));
        Assert.Equal(new Box(4, 8, 100, 80), frame);
    }

    [Fact]
    public void Clearing_lets_the_next_transition_save_again()
    {
        var restore = RestoreGeometry.None.Saving(new Box(10, 20, 300, 200));
        restore = RestoreGeometry.None;
        Assert.False(restore.HasValue);

        restore = restore.Saving(new Box(50, 60, 400, 300));
        Assert.True(restore.TryGet(out var frame));
        Assert.Equal(new Box(50, 60, 400, 300), frame);
    }
}
