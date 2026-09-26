using Basin.Effects;
using Basin.Scene;
using Xunit;

namespace Basin.Tests;

public sealed class DeformerCaptureTests
{
    private static (SceneTransform Node, SceneRect Top, CanvasWarpTransform Deformer) Captured(CompositorTestHost host)
    {
        var tree = new SceneTree(host.Scene.Root);
        tree.SetPosition(10, 10);
        var node = new SceneTransform(tree);
        _ = new SceneRect(node, 40, 30, new RenderColor(1f, 0f, 0f, 1f));
        var top = new SceneRect(node, 20, 10, new RenderColor(0f, 1f, 0f, 1f));
        var deformer = new CanvasWarpTransform { SceneX = 10, SceneY = 10, CellSize = 8 };
        node.Deformer = deformer;
        return (node, top, deformer);
    }

    [Fact]
    public void A_capture_survives_a_short_detach_and_is_reused()
    {
        using var host = new CompositorTestHost();
        var (node, _, deformer) = Captured(host);
        host.RenderFrame();
        var first = node.Capture.Buffer;
        Assert.NotNull(first);

        node.Deformer = null;
        for (var i = 0; i < 10; i++)
        {
            host.RenderFrame();
        }

        node.Deformer = deformer;
        host.RenderFrame();
        Assert.Same(first, node.Capture.Buffer);
    }

    [Fact]
    public void An_idle_capture_is_dropped_after_its_commits_run_out()
    {
        using var host = new CompositorTestHost();
        var (node, _, deformer) = Captured(host);
        host.RenderFrame();
        var first = node.Capture.Buffer;
        Assert.NotNull(first);

        node.Deformer = null;
        for (var i = 0; i < 130; i++)
        {
            host.RenderFrame();
        }

        node.Deformer = deformer;
        Assert.Null(node.Capture.Buffer);
        host.RenderFrame();
        Assert.NotNull(node.Capture.Buffer);
        Assert.NotSame(first, node.Capture.Buffer);
    }

    [Fact]
    public void Content_changed_while_detached_reaches_the_screen_after_the_reattach()
    {
        using var host = new CompositorTestHost();
        var (node, top, deformer) = Captured(host);
        host.RenderFrame();
        Assert.Equal(0xFF00FF00u, host.Pixel(15, 15));

        node.Deformer = null;
        host.RenderFrame();
        top.Color = new RenderColor(0f, 0f, 1f, 1f);
        host.RenderFrame();
        node.Deformer = deformer;
        host.RenderFrame();
        Assert.Equal(0xFF0000FFu, host.Pixel(15, 15));
    }
}
