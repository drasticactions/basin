using Basin;
using Basin.Effects;
using Basin.Scene;
using Xunit;

namespace Basin.Tests;

public sealed class SlideTransitionTests
{
    private static readonly Box Area = new(0, 0, 800, 600);

    private static FrameTick Tick(long millis) => new(millis * 1_000_000, 16_666_667);

    private static void RunOut(SlideTransition slide)
    {
        for (var millis = 0; millis < 1000 && slide.Step(Tick(millis)); millis += 16)
        {
        }
    }

    private static (SceneTree Tree, TransformStack Stack) Workspace(CompositorTestHost host)
    {
        var tree = new SceneTree(host.Scene.Root);
        _ = new SceneRect(tree, 800, 600, new RenderColor(0.2f, 0.2f, 0.2f, 1f));
        return (tree, new TransformStack(tree));
    }

    private static double OffsetOf(TransformStack stack) =>
        stack.Get("workspace-slide") is { } node ? node.Matrix.M13 : 0;

    [Fact]
    public void An_interactive_slide_starts_still()
    {
        using var host = new CompositorTestHost();
        var from = Workspace(host);
        var to = Workspace(host);

        var slide = new SlideTransition();
        slide.BeginInteractive(from.Stack, to.Stack, Area, direction: 1);

        Assert.True(slide.IsActive);
        Assert.True(slide.IsInteractive);
        Assert.False(slide.IsAnimating);
        Assert.Equal(0, OffsetOf(from.Stack), 6);
        Assert.Equal(800, OffsetOf(to.Stack), 6);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(-1, -1)]
    public void Progress_moves_both_trees_together(int direction, int side)
    {
        using var host = new CompositorTestHost();
        var from = Workspace(host);
        var to = Workspace(host);

        var slide = new SlideTransition();
        slide.BeginInteractive(from.Stack, to.Stack, Area, direction);

        slide.Progress = 0.5;

        Assert.Equal(-side * 400, OffsetOf(from.Stack), 6);
        Assert.Equal(side * 400, OffsetOf(to.Stack), 6);
        Assert.Equal(0.5, slide.Progress, 6);

        slide.Progress = 1;

        Assert.Equal(-side * 800, OffsetOf(from.Stack), 6);
        Assert.Equal(0, OffsetOf(to.Stack), 6);
    }

    [Fact]
    public void Settling_to_a_commit_finishes_the_travel()
    {
        using var host = new CompositorTestHost();
        var from = Workspace(host);
        var to = Workspace(host);

        var slide = new SlideTransition();
        slide.BeginInteractive(from.Stack, to.Stack, Area, direction: 1);
        slide.Progress = 0.7;

        slide.Settle(commit: true);
        Assert.True(slide.IsAnimating);
        Assert.False(slide.IsInteractive);

        Assert.True(slide.Step(Tick(0)));
        Assert.True(slide.Step(Tick(100)));

        var outgoing = OffsetOf(from.Stack);
        var incoming = OffsetOf(to.Stack);
        Assert.InRange(outgoing, -800, -560);
        Assert.InRange(incoming, 0, 240);

        RunOut(slide);
        Assert.False(slide.IsActive);
    }

    [Fact]
    public void Settling_to_a_cancel_returns_to_rest()
    {
        using var host = new CompositorTestHost();
        var from = Workspace(host);
        var to = Workspace(host);

        var slide = new SlideTransition();
        slide.BeginInteractive(from.Stack, to.Stack, Area, direction: 1);
        slide.Progress = 0.4;

        slide.Settle(commit: false);
        _ = slide.Step(Tick(0));
        var midway = OffsetOf(from.Stack);
        RunOut(slide);

        Assert.True(Math.Abs(midway) > 0);
        Assert.False(slide.IsActive);
        Assert.Equal(0, OffsetOf(from.Stack), 6);
    }

    [Fact]
    public void A_settle_starts_from_where_the_drag_left_off()
    {
        using var host = new CompositorTestHost();
        var from = Workspace(host);
        var to = Workspace(host);

        var slide = new SlideTransition();
        slide.BeginInteractive(from.Stack, to.Stack, Area, direction: 1);
        slide.Progress = 0.8;

        slide.Settle(commit: true);
        _ = slide.Step(Tick(0));

        Assert.InRange(OffsetOf(from.Stack), -800, -640);
    }

    [Fact]
    public void A_drag_with_no_neighbour_moves_only_the_outgoing_tree()
    {
        using var host = new CompositorTestHost();
        var from = Workspace(host);

        var slide = new SlideTransition();
        slide.BeginInteractive(from.Stack, null, Area, direction: 1);

        slide.Progress = 0.1;

        Assert.Equal(-80, OffsetOf(from.Stack), 6);

        slide.Settle(commit: true);
        RunOut(slide);

        Assert.Equal(0, OffsetOf(from.Stack), 6);
    }

    [Fact]
    public void Progress_does_nothing_once_the_slide_is_settling()
    {
        using var host = new CompositorTestHost();
        var from = Workspace(host);
        var to = Workspace(host);

        var slide = new SlideTransition();
        slide.BeginInteractive(from.Stack, to.Stack, Area, direction: 1);
        slide.Progress = 0.5;
        slide.Settle(commit: false);

        slide.Progress = 1;

        Assert.Equal(-400, OffsetOf(from.Stack), 6);
    }

    [Fact]
    public void A_drag_owes_no_frames()
    {
        using var host = new CompositorTestHost();
        var from = Workspace(host);
        var to = Workspace(host);

        var slide = new SlideTransition();
        slide.BeginInteractive(from.Stack, to.Stack, Area, direction: 1);
        slide.Progress = 0.5;

        Assert.False(slide.IsAnimating);
        Assert.False(slide.Step(Tick(0)));
        Assert.False(slide.Step(Tick(600)));
        Assert.Equal(-400, OffsetOf(from.Stack), 6);
        Assert.True(slide.IsActive);
    }

    [Fact]
    public void A_destroyed_tree_ends_the_slide()
    {
        using var host = new CompositorTestHost();
        var from = Workspace(host);
        var to = Workspace(host);

        var slide = new SlideTransition();
        slide.BeginInteractive(from.Stack, to.Stack, Area, direction: 1);
        slide.Progress = 0.5;
        slide.Settle(commit: true);

        to.Tree.Destroy();

        Assert.False(slide.Step(Tick(0)));
        Assert.False(slide.IsActive);
    }

    [Fact]
    public void An_interactive_slide_picks_up_a_running_one()
    {
        using var host = new CompositorTestHost();
        var from = Workspace(host);
        var to = Workspace(host);

        var slide = new SlideTransition();
        slide.Begin(from.Stack, to.Stack, Area, direction: 1);
        _ = slide.Step(Tick(0));
        _ = slide.Step(Tick(100));
        var interrupted = OffsetOf(from.Stack);

        slide.BeginInteractive(from.Stack, to.Stack, Area, direction: 1);

        Assert.Equal(interrupted, OffsetOf(from.Stack), 6);
        Assert.True(slide.IsInteractive);
    }
}
