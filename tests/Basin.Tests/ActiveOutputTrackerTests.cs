using Basin.Host;
using Xunit;

namespace Basin.Tests;

public sealed class ActiveOutputTrackerTests
{
    private sealed class FakeOutput(string name) : OutputBase(name)
    {
        protected override bool TestCommitCore(OutputState state) => true;

        protected override bool CommitCore(OutputState state) => true;
    }

    private static FakeOutput OutputOf(string name)
    {
        var output = new FakeOutput(name);
        using var state = new OutputState();
        output.Commit(state.SetEnabled(true).SetMode(new OutputMode(1280, 720, 60000)));
        return output;
    }

    [Fact]
    public void The_first_output_is_current_and_motion_moves_it_with_one_change_per_crossing()
    {
        var layout = new OutputLayout();
        var left = OutputOf("LEFT");
        var right = OutputOf("RIGHT");
        layout.Add(left, 0, 0);
        layout.Add(right, 1280, 0);
        var tracker = new ActiveOutputTracker(layout);
        var changes = new List<string>();
        tracker.Changed += output => changes.Add(output.Name);

        tracker.Added(left);
        tracker.Added(right);
        Assert.Same(left, tracker.Current);

        tracker.Moved(100, 100);
        tracker.Moved(1500, 300);
        tracker.Moved(1600, 300);
        tracker.Moved(5000, 5000);
        tracker.Moved(10, 10);

        Assert.Equal(["RIGHT", "LEFT"], changes);
        Assert.Same(left, tracker.Current);
    }

    [Fact]
    public void Removing_the_current_output_moves_to_the_one_under_the_pointer_or_the_first_left()
    {
        var layout = new OutputLayout();
        var a = OutputOf("A");
        var b = OutputOf("B");
        var c = OutputOf("C");
        layout.Add(a, 0, 0);
        layout.Add(b, 1280, 0);
        layout.Add(c, 2560, 0);
        var tracker = new ActiveOutputTracker(layout);
        tracker.Added(a);
        tracker.Added(b);
        tracker.Added(c);
        tracker.Moved(1300, 10);
        Assert.Same(b, tracker.Current);

        tracker.Removed(a, 1300, 10);
        Assert.Same(b, tracker.Current);

        tracker.Removed(b, 2600, 10);
        Assert.Same(c, tracker.Current);

        layout.Remove(b);
        tracker.Removed(c, 1300, 10);
        Assert.Null(tracker.Current);
    }
}
