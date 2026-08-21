using Basin.Effects;
using Basin.Scene;
using Xunit;

namespace Basin.Tests;

public sealed class ProjectionTests
{
    [Fact]
    public void A_flat_card_at_rest_maps_corners_to_themselves()
    {
        var bounds = new Box(20, 30, 100, 60);
        var matrix = Projection.Card(bounds, 70, 60, 1, 0, 900);

        Assert.True(matrix.IsAffine);
        AssertMaps(matrix, 20, 30);
        AssertMaps(matrix, 120, 30);
        AssertMaps(matrix, 20, 90);
        AssertMaps(matrix, 120, 90);
    }

    [Fact]
    public void Yaw_brings_one_edge_nearer_and_stays_invertible()
    {
        var bounds = new Box(0, 0, 100, 60);
        var matrix = Projection.Card(bounds, 50, 30, 1, 0.9, 600);

        Assert.False(matrix.IsAffine);
        var (_, topLeftY) = matrix.Map(0, 0);
        var (_, bottomLeftY) = matrix.Map(0, 60);
        var (rightX, topRightY) = matrix.Map(100, 0);
        var (_, bottomRightY) = matrix.Map(100, 60);
        Assert.True(bottomRightY - topRightY > bottomLeftY - topLeftY);

        Assert.True(matrix.TryInvert(out var inverse));
        var (x, y) = inverse.Map(rightX, topRightY);
        Assert.Equal(100, x, 6);
        Assert.Equal(0, y, 6);
    }

    [Fact]
    public void An_edge_on_card_is_not_invertible()
    {
        var matrix = Projection.Card(new Box(0, 0, 100, 60), 50, 30, 1, Math.PI / 2, 600);
        Assert.False(matrix.TryInvert(out _));
    }

    private static void AssertMaps(in RenderTransform matrix, double x, double y)
    {
        var (mappedX, mappedY) = matrix.Map(x, y);
        Assert.Equal(x, mappedX, 6);
        Assert.Equal(y, mappedY, 6);
    }
}

public sealed class SwitcherEffectTests
{
    private static FrameTick Tick(long millis) => new(millis * 1_000_000, 16_666_667);

    private static (SceneTree Tree, TransformStack Stack) Window(
        CompositorTestHost host, int x, int y, RenderColor color)
    {
        var tree = new SceneTree(host.Scene.Root);
        tree.SetPosition(x, y);
        _ = new SceneRect(tree, 60, 40, color);
        return (tree, new TransformStack(tree));
    }

    [Fact]
    public void Cards_fly_to_the_deck_and_home_again()
    {
        using var host = new CompositorTestHost();
        var a = Window(host, 10, 10, new RenderColor(1f, 0f, 0f, 1f));
        var b = Window(host, 200, 60, new RenderColor(0f, 1f, 0f, 1f));
        var c = Window(host, 90, 120, new RenderColor(0f, 0f, 1f, 1f));
        var stacks = new List<TransformStack> { a.Stack, b.Stack, c.Stack };

        var switcher = new SwitcherEffect();
        switcher.Begin(stacks, new Box(0, 0, 640, 480), 1);
        Assert.True(switcher.IsActive);

        Assert.True(switcher.Step(Tick(5000)));
        Assert.True(switcher.Step(Tick(6000)));
        var selected = stacks[1].Get("switcher");
        var side = stacks[0].Get("switcher");
        Assert.NotNull(selected);
        Assert.NotNull(side);
        Assert.True(selected!.Matrix.IsAffine);
        Assert.False(side!.Matrix.IsAffine);
        Assert.True(side.Alpha < 1f);

        switcher.End();
        Assert.True(switcher.IsDismissing);
        var frame = 6000L;
        while (switcher.Step(Tick(frame += 16)) && frame < 9000)
        {
        }

        Assert.False(switcher.IsActive);
        Assert.Null(stacks[0].Get("switcher"));
        Assert.Null(stacks[1].Get("switcher"));
        Assert.Single(a.Tree.Children);
        Assert.IsType<SceneRect>(a.Tree.Children[0]);
    }

    [Fact]
    public void Selecting_retargets_the_deck_around_the_new_card()
    {
        using var host = new CompositorTestHost();
        var a = Window(host, 10, 10, new RenderColor(1f, 0f, 0f, 1f));
        var b = Window(host, 200, 60, new RenderColor(0f, 1f, 0f, 1f));
        var c = Window(host, 90, 120, new RenderColor(0f, 0f, 1f, 1f));
        var stacks = new List<TransformStack> { a.Stack, b.Stack, c.Stack };

        var switcher = new SwitcherEffect();
        switcher.Begin(stacks, new Box(0, 0, 640, 480), 0);
        Assert.True(switcher.Step(Tick(5000)));
        Assert.True(switcher.Step(Tick(6000)));
        Assert.True(stacks[0].Get("switcher")!.Matrix.IsAffine);

        switcher.Select(2);
        Assert.Equal(2, switcher.Selected);
        Assert.True(switcher.Step(Tick(6016)));
        Assert.True(switcher.Step(Tick(7000)));
        Assert.True(stacks[2].Get("switcher")!.Matrix.IsAffine);
        Assert.False(stacks[0].Get("switcher")!.Matrix.IsAffine);
        Assert.Equal(1f, stacks[2].Get("switcher")!.Alpha, 3);
        Assert.True(stacks[0].Get("switcher")!.Alpha < 1f);

        switcher.End();
        var frame = 7000L;
        while (switcher.Step(Tick(frame += 16)) && frame < 10000)
        {
        }
    }

    [Fact]
    public void The_selected_card_renders_at_the_area_center()
    {
        using var host = new CompositorTestHost();
        var a = Window(host, 10, 10, new RenderColor(1f, 0f, 0f, 1f));
        var b = Window(host, 200, 60, new RenderColor(0f, 1f, 0f, 1f));
        var c = Window(host, 90, 120, new RenderColor(0f, 0f, 1f, 1f));
        var stacks = new List<TransformStack> { a.Stack, b.Stack, c.Stack };

        var switcher = new SwitcherEffect();
        switcher.Begin(stacks, new Box(0, 0, host.Target.Width, host.Target.Height), 1);
        Assert.True(switcher.Step(Tick(5000)));
        Assert.True(switcher.Step(Tick(6000)));

        host.RenderFrame();
        var rgba = Basin.Diagnostics.BufferCapture.ReadRgba(host.Target);
        var centerX = host.Target.Width / 2;
        var centerY = host.Target.Height / 2;
        var index = ((centerY * host.Target.Width) + centerX) * 4;
        Assert.True(rgba[index + 1] > 200, "the selected green card covers the area center");
        Assert.True(rgba[index] < 60, "the red card does not cover the area center");
    }
}
