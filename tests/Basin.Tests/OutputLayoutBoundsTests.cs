using Xunit;

namespace Basin.Tests;

public sealed class OutputLayoutBoundsTests
{
    private sealed class FakeOutput(string name) : OutputBase(name)
    {
        protected override bool TestCommitCore(OutputState state) => true;

        protected override bool CommitCore(OutputState state) => true;
    }

    private static FakeOutput OutputOf(string name, int width, int height)
    {
        var output = new FakeOutput(name);
        using var state = new OutputState();
        output.Commit(state.SetEnabled(true).SetMode(new OutputMode(width, height, 60000)));
        return output;
    }

    [Fact]
    public void Empty_layout_has_empty_bounds()
    {
        var layout = new OutputLayout();
        Assert.True(layout.Bounds.IsEmpty);
        Assert.Equal(default, layout.Bounds);
    }

    [Fact]
    public void Bounds_is_the_union_of_every_output()
    {
        var layout = new OutputLayout();
        layout.Add(OutputOf("L", 100, 100), 0, 0);
        layout.Add(OutputOf("R", 200, 150), 100, 0);
        Assert.Equal(new Box(0, 0, 300, 150), layout.Bounds);
    }

    [Fact]
    public void Bounds_follows_a_move()
    {
        var layout = new OutputLayout();
        var left = OutputOf("L", 100, 100);
        var right = OutputOf("R", 100, 100);
        layout.Add(left, 0, 0);
        layout.Add(right, 100, 0);
        layout.Move(right, 200, 50);
        Assert.Equal(new Box(0, 0, 300, 150), layout.Bounds);
    }

    [Fact]
    public void Bounds_shrinks_on_remove_and_empties_with_the_last_output()
    {
        var layout = new OutputLayout();
        var left = OutputOf("L", 100, 100);
        var right = OutputOf("R", 200, 100);
        layout.Add(left, 0, 0);
        layout.Add(right, 100, 0);
        layout.Remove(right);
        Assert.Equal(new Box(0, 0, 100, 100), layout.Bounds);
        layout.Remove(left);
        Assert.True(layout.Bounds.IsEmpty);
    }

    [Fact]
    public void Bounds_spans_a_negative_origin()
    {
        var layout = new OutputLayout();
        layout.Add(OutputOf("L", 100, 100), -50, -20);
        layout.Add(OutputOf("R", 100, 100), 100, 0);
        Assert.Equal(new Box(-50, -20, 250, 120), layout.Bounds);
    }

    [Fact]
    public void Bounds_reflects_a_mode_change()
    {
        var layout = new OutputLayout();
        var output = OutputOf("O", 100, 100);
        layout.Add(output, 0, 0);
        using var state = new OutputState();
        output.Commit(state.SetMode(new OutputMode(640, 480, 60000)));
        Assert.Equal(new Box(0, 0, 640, 480), layout.Bounds);
    }
}
