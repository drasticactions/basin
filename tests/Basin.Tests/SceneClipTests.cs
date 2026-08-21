using Basin.Scene;
using Pixman;
using Xunit;

namespace Basin.Tests;

public class SceneClipTests
{
    private const uint Red = 0xffff0000;
    private const uint Blue = 0xff0000ff;

    [Fact]
    public void A_clip_box_hides_the_pixels_outside_it()
    {
        using var host = new CompositorTestHost(64, 64);
        var surface = MapSurface(host, 40, 40, Red);
        var tree = host.SurfaceScenes[0].Tree;
        tree.SetPosition(10, 10);

        tree.ClipBox = new Box(0, 0, 20, 20);
        host.RenderFrame();

        Assert.Equal(Red, host.Pixel(15, 15));
        Assert.NotEqual(Red, host.Pixel(35, 15));
        Assert.NotEqual(Red, host.Pixel(15, 35));
        GC.KeepAlive(surface);
    }

    [Fact]
    public void An_empty_clip_box_means_no_clipping()
    {
        using var host = new CompositorTestHost(64, 64);
        MapSurface(host, 40, 40, Red);
        var tree = host.SurfaceScenes[0].Tree;
        tree.SetPosition(10, 10);

        tree.ClipBox = new Box(0, 0, 20, 20);
        tree.ClipBox = default;
        host.RenderFrame();

        Assert.Equal(Red, host.Pixel(35, 35));
        Assert.False(tree.IsClipped);
    }

    [Fact]
    public void Clip_boxes_compose_with_an_ancestors_clip_by_intersection()
    {
        using var host = new CompositorTestHost(64, 64);
        MapSurface(host, 40, 40, Red);
        var scene = host.SurfaceScenes[0];
        var outer = new SceneTree(host.Scene.Root);
        scene.Tree.Reparent(outer);
        outer.SetPosition(10, 10);

        outer.ClipBox = new Box(0, 0, 20, 40);
        scene.Tree.ClipBox = new Box(0, 0, 40, 20);
        host.RenderFrame();

        Assert.Equal(Red, host.Pixel(15, 15));
        Assert.NotEqual(Red, host.Pixel(35, 15));
        Assert.NotEqual(Red, host.Pixel(15, 35));
    }

    [Fact]
    public void A_clip_box_that_intersects_to_nothing_draws_nothing()
    {
        using var host = new CompositorTestHost(64, 64);
        MapSurface(host, 40, 40, Red);
        var scene = host.SurfaceScenes[0];
        var outer = new SceneTree(host.Scene.Root);
        scene.Tree.Reparent(outer);

        outer.ClipBox = new Box(0, 0, 10, 10);
        scene.Tree.ClipBox = new Box(20, 20, 10, 10);
        host.RenderFrame();

        Assert.NotEqual(Red, host.Pixel(5, 5));
        Assert.NotEqual(Red, host.Pixel(25, 25));
    }

    [Fact]
    public void A_clipped_node_does_not_occlude_what_is_beneath_it_outside_the_clip()
    {
        using var host = new CompositorTestHost(64, 64);

        var lower = new SceneRect(host.Scene.Root, 64, 64, new RenderColor(0f, 0f, 1f, 1f));
        var upper = new SceneRect(host.Scene.Root, 64, 64, new RenderColor(1f, 0f, 0f, 1f));
        upper.ClipBox = new Box(0, 0, 32, 64);

        host.RenderFrame();
        Assert.Equal(Red, host.Pixel(10, 10));
        Assert.Equal(Blue, host.Pixel(50, 10));
        GC.KeepAlive(lower);
    }

    [Fact]
    public void The_damage_tracked_path_agrees_with_the_oracle_about_clip_boxes()
    {
        using var host = new CompositorTestHost(64, 64);
        MapSurface(host, 40, 40, Red);
        var lower = new SceneRect(host.Scene.Root, 64, 64, new RenderColor(0f, 0f, 1f, 1f));
        lower.LowerToBottom();
        var tree = host.SurfaceScenes[0].Tree;
        tree.SetPosition(10, 10);
        tree.ClipBox = new Box(5, 5, 20, 20);

        host.RenderFrame();
        var oracle = Snapshot(host);

        using var sceneOutput = new SceneOutput(host.Scene, host.Output);
        using var swapchain = new Swapchain(new ShmAllocator(), 64, 64, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
        using var state = new OutputState();
        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state, new SceneCommitOptions { AllowDirectScanout = false }));

        var tracked = Snapshot(sceneOutput.LastTarget!);
        Assert.Equal(oracle, tracked);
    }

    [Fact]
    public void Moving_a_clip_box_repaints_what_it_stops_covering()
    {
        using var host = new CompositorTestHost(64, 64);
        MapSurface(host, 64, 64, Red);
        var tree = host.SurfaceScenes[0].Tree;
        tree.ClipBox = new Box(0, 0, 64, 64);

        using var sceneOutput = new SceneOutput(host.Scene, host.Output);
        using var swapchain = new Swapchain(new ShmAllocator(), 64, 64, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
        using var state = new OutputState();
        sceneOutput.Commit(host.Renderer, swapchain, state, new SceneCommitOptions { AllowDirectScanout = false });
        Assert.False(sceneOutput.NeedsRepaint);

        tree.ClipBox = new Box(0, 0, 16, 16);
        Assert.True(sceneOutput.NeedsRepaint);

        sceneOutput.Commit(host.Renderer, swapchain, state, new SceneCommitOptions { AllowDirectScanout = false });
        Assert.NotEqual(Red, ReadPixel(sceneOutput.LastTarget!, 40, 40));
    }

    [Fact]
    public void Clipped_away_pixels_do_not_take_input()
    {
        using var host = new CompositorTestHost(64, 64);
        MapSurface(host, 40, 40, Red);
        var tree = host.SurfaceScenes[0].Tree;
        tree.SetPosition(10, 10);

        Assert.NotNull(host.Scene.SurfaceAt(35, 35));

        tree.ClipBox = new Box(0, 0, 20, 20);
        Assert.NotNull(host.Scene.SurfaceAt(15, 15));
        Assert.Null(host.Scene.SurfaceAt(35, 35));
    }

    [Fact]
    public void An_input_disabled_surface_still_renders_but_lets_input_fall_through()
    {
        using var host = new CompositorTestHost(64, 64);
        MapSurface(host, 40, 40, Red);

        var second = host.Client.Compositor.CreateSurface();
        var buffer = host.Client.CreateBuffer(40, 40, Fill.Solid(40, 40, Blue));
        second.Attach(buffer.Proxy, 0, 0);
        second.Damage(0, 0, 40, 40);
        second.Commit();
        host.PumpToServer();

        var overlay = host.SurfaceScenes[1];
        overlay.Tree.SetPosition(10, 10);
        Assert.Same(overlay.Surface, host.Scene.SurfaceAt(15, 15)?.Surface);

        overlay.InputEnabled = false;
        Assert.Same(host.SurfaceScenes[0].Surface, host.Scene.SurfaceAt(15, 15)?.Surface);

        host.RenderFrame();
        Assert.Equal(Blue, host.Pixel(15, 15));
    }

    private static Surface MapSurface(CompositorTestHost host, int width, int height, uint argb)
    {
        var surface = host.Client.Compositor.CreateSurface();
        var buffer = host.Client.CreateBuffer(width, height, Fill.Solid(width, height, argb));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, width, height);
        surface.Commit();
        host.PumpToServer();
        return host.SurfaceScenes[0].Surface;
    }

    private static uint[] Snapshot(CompositorTestHost host)
    {
        var pixels = new uint[host.Target.Width * host.Target.Height];
        for (var y = 0; y < host.Target.Height; y++)
        {
            for (var x = 0; x < host.Target.Width; x++)
            {
                pixels[(y * host.Target.Width) + x] = host.Pixel(x, y);
            }
        }

        return pixels;
    }

    private static uint[] Snapshot(IBuffer buffer)
    {
        var pixels = new uint[buffer.Width * buffer.Height];
        for (var y = 0; y < buffer.Height; y++)
        {
            for (var x = 0; x < buffer.Width; x++)
            {
                pixels[(y * buffer.Width) + x] = ReadPixel(buffer, x, y);
            }
        }

        return pixels;
    }

    private static uint ReadPixel(IBuffer buffer, int x, int y)
    {
        Assert.True(buffer.BeginDataAccess(BufferDataAccess.Read, out var view));
        try
        {
            unsafe
            {
                var row = (byte*)view.Data + (y * view.Stride);
                return ((uint*)row)[x] | 0xff000000u;
            }
        }
        finally
        {
            buffer.EndDataAccess();
        }
    }
}
