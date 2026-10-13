using Basin.Scene;
using Xunit;

namespace Basin.Tests;

public sealed class SceneFitTests
{
    [Fact]
    public void Fit_scales_uniformly_to_the_largest_size_that_fits_and_centers()
    {
        var (scale, box) = SceneFit.Place(new Box(0, 0, 1920, 1080), (1024, 768), SceneFitMode.Fit);
        Assert.Equal(1080 / 768.0, scale, 6);
        Assert.Equal(new Box(240, 0, 1440, 1080), box);
    }

    [Fact]
    public void Center_keeps_one_to_one_and_shrinks_only_what_does_not_fit()
    {
        var (scale, box) = SceneFit.Place(new Box(0, 0, 1920, 1080), (640, 480), SceneFitMode.Center);
        Assert.Equal(1.0, scale);
        Assert.Equal(new Box(640, 300, 640, 480), box);

        (scale, box) = SceneFit.Place(new Box(10, 20, 800, 600), (1600, 600), SceneFitMode.Center);
        Assert.Equal(0.5, scale);
        Assert.Equal(new Box(10, 170, 800, 300), box);
    }

    [Fact]
    public void Needed_follows_the_state_the_policy_and_convergence()
    {
        Assert.Null(SceneFit.Needed((800, 600), (800, 600), SceneFitState.Fullscreen, SceneFitPolicy.Auto));
        Assert.Null(SceneFit.Needed((800, 600), (640, 480), SceneFitState.Floating, SceneFitPolicy.Auto));
        Assert.Null(SceneFit.Needed((800, 600), (640, 480), SceneFitState.Fullscreen, SceneFitPolicy.Off));
        Assert.Equal(SceneFitMode.Fit, SceneFit.Needed((800, 600), (640, 480), SceneFitState.Fullscreen, SceneFitPolicy.Auto));
        Assert.Equal(SceneFitMode.Center, SceneFit.Needed((800, 600), (640, 480), SceneFitState.Maximized, SceneFitPolicy.Auto));
        Assert.Equal(SceneFitMode.Center, SceneFit.Needed((800, 600), (640, 480), SceneFitState.Tiled, SceneFitPolicy.Auto));
        Assert.Equal(SceneFitMode.Fit, SceneFit.Needed((800, 600), (640, 480), SceneFitState.Maximized, SceneFitPolicy.Fit));
        Assert.Equal(SceneFitMode.Center, SceneFit.Needed((800, 600), (640, 480), SceneFitState.Fullscreen, SceneFitPolicy.Center));
    }

    [Fact]
    public void Equal_sizes_leave_the_node_inert()
    {
        using var host = new CompositorTestHost();
        var fit = new SceneFit(host.Scene.Root);
        fit.Update(new Box(0, 0, 100, 80), (100, 80), SceneFitMode.Fit);
        Assert.False(fit.IsActive);
        Assert.True(((SceneTransform)fit.Content).IsInertNow);
        Assert.False(fit.Root.Children[0].Enabled);
        fit.Destroy();
    }

    [Fact]
    public void Input_maps_through_the_inverse_and_a_bar_stops_a_press()
    {
        using var host = new CompositorTestHost();
        var window = MappedToplevel.Map(host, host.Client, 40, 30);
        var below = host.SurfaceScenes[0];
        below.Tree.SetPosition(0, 0);

        var surface = host.Client.Compositor.CreateSurface();
        var buffer = host.Client.CreateBuffer(40, 30, Fill.Solid(40, 30, 0xFF00FF00));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Commit();
        host.PumpToServer();
        var top = host.SurfaceScenes[^1];

        var fit = new SceneFit(host.Scene.Root);
        top.Tree.Reparent(fit.Content);
        top.Tree.SetPosition(0, 0);
        fit.Update(new Box(0, 0, 160, 120), (40, 30), SceneFitMode.Fit);
        Assert.Equal(4.0, fit.ContentScale);

        var hit = host.Scene.SurfaceAt(80, 60);
        Assert.NotNull(hit);
        Assert.Same(top.Surface, ((SceneBuffer)hit.Value.Node).InputSurface);
        Assert.Equal((20.0, 15.0), (hit.Value.X, hit.Value.Y));
        Assert.Equal((20.0, 15.0), fit.ToContent(80, 60));

        fit.Update(new Box(0, 0, 160, 120), (20, 30), SceneFitMode.Fit);
        Assert.Equal(new Box(40, 0, 80, 120), fit.ContentBox);
        Assert.Null(host.Scene.SurfaceAt(10, 10));
        Assert.NotNull(window);
        fit.Destroy();
    }

    [Theory]
    [InlineData("pixman")]
    [InlineData("gl")]
    [InlineData("vulkan")]
    [InlineData("skia")]
    [InlineData("skia-gl")]
    [InlineData("skia-vulkan")]
    [InlineData("skia-graphite")]
    public void Golden_letterboxed_fullscreen(string renderer)
    {
        CompositorTestHost.SkipUnlessRunnable(renderer);
        using var host = new CompositorTestHost(renderer: renderer);
        _ = new SceneRect(host.Scene.Root, 160, 120, new RenderColor(0.9f, 0.6f, 0.1f, 1f));
        var surface = host.Client.Compositor.CreateSurface();
        var buffer = host.Client.CreateBuffer(64, 32, Fill.Gradient(64, 32));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, 64, 32);
        surface.Commit();
        host.PumpToServer();
        var scene = host.SurfaceScenes[0];
        var fit = new SceneFit(host.Scene.Root);
        scene.Tree.Reparent(fit.Content);
        scene.Tree.SetPosition(0, 0);
        fit.Update(new Box(0, 0, 160, 120), (64, 32), SceneFitMode.Fit);

        host.RenderFrame();
        Assert.Equal(0xFF000000u, host.Pixel(2, 2));
        Assert.Equal(0xFF000000u, host.Pixel(2, 117));
        Golden.AssertMatches(host, renderer == "pixman" ? "letterboxed-fullscreen" : $"letterboxed-fullscreen-{renderer}");
        fit.Destroy();
    }

    [Theory]
    [InlineData("pixman")]
    [InlineData("gl")]
    [InlineData("vulkan")]
    [InlineData("skia")]
    [InlineData("skia-gl")]
    [InlineData("skia-vulkan")]
    [InlineData("skia-graphite")]
    public void Golden_emulated_mode(string renderer)
    {
        CompositorTestHost.SkipUnlessRunnable(renderer);
        using var host = new CompositorTestHost(renderer: renderer);
        var surface = host.Client.Compositor.CreateSurface();
        var viewport = host.Client.Viewporter!.GetViewport(surface);
        viewport.SetDestination(160, 120);
        var buffer = host.Client.CreateBuffer(64, 48, (data, stride) =>
        {
            unsafe
            {
                for (var y = 0; y < 48; y++)
                {
                    var row = (uint*)(data + y * stride);
                    for (var x = 0; x < 64; x++)
                    {
                        row[x] = (((x / 8) + (y / 8)) & 1) == 0 ? 0xFFE0E0E0u : 0xFF2050C0u;
                    }
                }
            }
        });
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, 64, 48);
        surface.Commit();
        host.PumpToServer();
        var scene = host.SurfaceScenes[0];
        var fit = new SceneFit(host.Scene.Root);
        scene.Tree.Reparent(fit.Content);
        scene.Tree.SetPosition(0, 0);

        fit.Update(new Box(0, 0, 160, 120), (160, 120), (1280, 720), SceneFitMode.Fit);
        Assert.Equal(new Box(0, 15, 160, 90), fit.ContentBox);
        Assert.Equal((1.0, 0.75), (fit.ContentScale, fit.ContentScaleY));
        Assert.Equal((80.0, 60.0), fit.ToContent(80, 60));

        host.RenderFrame();
        Assert.Equal(0xFF000000u, host.Pixel(80, 5));
        Assert.Equal(0xFF000000u, host.Pixel(80, 114));
        Golden.AssertMatches(host, renderer == "pixman" ? "emulated-mode" : $"emulated-mode-{renderer}");
        fit.Destroy();
    }
}
