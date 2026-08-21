using Basin.Scene;
using Xunit;

namespace Basin.Tests;

public sealed class TransformStackTests
{
    [Fact]
    public void Chain_orders_by_z_regardless_of_insertion_order()
    {
        var scene = new Basin.Scene.Scene();
        var window = new SceneTree(scene.Root);
        var content = new SceneRect(window, 40, 30, new RenderColor(1f, 1f, 1f, 1f));

        var stack = new TransformStack(window);
        var effect = stack.Add(TransformStack.ZOrder.Effect, "wobbly");
        var scale = stack.Add(TransformStack.ZOrder.Transform2D, "scale");
        var blur = stack.Add(TransformStack.ZOrder.Backdrop, "blur");

        Assert.Same(window, blur.Parent);
        Assert.Same(blur, effect.Parent);
        Assert.Same(effect, scale.Parent);
        Assert.Same(scale, content.Parent);
    }

    [Fact]
    public void Remove_heals_the_chain_and_preserves_order()
    {
        var scene = new Basin.Scene.Scene();
        var window = new SceneTree(scene.Root);
        var first = new SceneRect(window, 10, 10, new RenderColor(1f, 0f, 0f, 1f));
        var second = new SceneRect(window, 10, 10, new RenderColor(0f, 1f, 0f, 1f));

        var stack = new TransformStack(window);
        var inner = stack.Add(1, "inner");
        var outer = stack.Add(500, "outer");
        Assert.Equal([first, second], inner.Children);

        Assert.True(stack.Remove("inner"));
        Assert.True(inner.IsDestroyed);
        Assert.Equal([first, second], outer.Children);
        Assert.Same(outer, first.Parent);

        Assert.True(stack.Remove("outer"));
        Assert.Equal([first, second], window.Children);
    }

    [Fact]
    public void Duplicate_names_throw_and_lookups_answer()
    {
        var scene = new Basin.Scene.Scene();
        var window = new SceneTree(scene.Root);
        _ = new SceneRect(window, 10, 10, new RenderColor(1f, 1f, 1f, 1f));

        var stack = new TransformStack(window);
        var node = stack.Add(500, "wobbly");
        Assert.Same(node, stack.Get("wobbly"));
        Assert.Null(stack.Get("missing"));
        Assert.Throws<InvalidOperationException>(() => stack.Add(300, "wobbly"));
        Assert.False(stack.Remove("missing"));
    }

    [Fact]
    public void Nodes_added_to_the_root_after_a_transformer_sit_outside_it()
    {
        var scene = new Basin.Scene.Scene();
        var window = new SceneTree(scene.Root);
        var content = new SceneRect(window, 40, 30, new RenderColor(1f, 1f, 1f, 1f));

        var stack = new TransformStack(window);
        var node = stack.Add(TransformStack.ZOrder.Transform2D, "open-close");
        var late = new SceneRect(window, 40, 10, new RenderColor(0f, 0f, 1f, 1f));

        Assert.Same(node, content.Parent);
        Assert.Same(window, late.Parent);
    }

    [Fact]
    public void Externally_destroyed_nodes_are_pruned()
    {
        var scene = new Basin.Scene.Scene();
        var window = new SceneTree(scene.Root);
        _ = new SceneRect(window, 10, 10, new RenderColor(1f, 1f, 1f, 1f));

        var stack = new TransformStack(window);
        var node = stack.Add(500, "wobbly");
        node.Destroy();
        Assert.Null(stack.Get("wobbly"));
        _ = stack.Add(500, "wobbly");
    }

    [Fact]
    public void Stacked_transforms_compose_for_rendering_and_hit_testing()
    {
        using var host = new CompositorTestHost();
        var window = new SceneTree(host.Scene.Root);
        window.SetPosition(50, 40);
        var content = new SceneRect(window, 40, 30, new RenderColor(1f, 0f, 0f, 1f));

        var stack = new TransformStack(window);
        stack.Add(TransformStack.ZOrder.Transform2D, "scale").Matrix = RenderTransform.Scale(2, 2);
        stack.Add(TransformStack.ZOrder.Effect, "shift").Matrix = RenderTransform.Translation(10, 0);

        var hit = host.Scene.NodeAt(50 + 10 + 40, 40 + 20);
        Assert.NotNull(hit);
        Assert.Same(content, hit!.Value.Node);
        Assert.Equal(20, hit.Value.X, 6);
        Assert.Equal(10, hit.Value.Y, 6);

        host.RenderFrame();
        var rgba = Basin.Diagnostics.BufferCapture.ReadRgba(host.Target);
        int Red(int x, int y) => rgba[((y * host.Target.Width) + x) * 4];
        Assert.True(Red(70, 55) > 200);
        Assert.True(Red(52, 42) < 60);
    }
}
