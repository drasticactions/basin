using Basin.Capabilities.Defaults;
using Pixman;
using Xunit;

namespace Basin.Tests;

public sealed class SurfaceAppearanceTests
{
    public static TheoryData<string> Renderers => new() { "pixman", "gl", "vulkan", "skia", "skia-gl", "skia-vulkan", "skia-graphite", "impeller" };

    private static string GoldenName(string name, string renderer) =>
        renderer == "pixman" ? name : $"{name}-{renderer}";

    [Theory]
    [MemberData(nameof(Renderers))]
    public void Golden_half_opacity_surface(string renderer)
    {
        CompositorTestHost.SkipUnlessRunnable(renderer);
        using var host = new CompositorTestHost(renderer: renderer);
        var appearance = new DefaultSurfaceAppearance();
        host.Scene.Appearance = appearance;

        var surface = host.Client.Compositor.CreateSurface();
        var buffer = host.Client.CreateBuffer(100, 80, Fill.Solid(100, 80, 0xFFCC2020));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, 100, 80);
        surface.Commit();
        host.PumpToServer();

        var scene = host.SurfaceScenes[0];
        scene.Tree.SetPosition(20, 14);
        appearance.SetOpacity(scene.Surface, 0.5);
        Assert.Equal(0.5f, scene.Tree.Alpha);

        host.RenderFrame();
        Golden.AssertMatches(host, GoldenName("half-opacity-surface", renderer));
    }

    [Theory]
    [MemberData(nameof(Renderers))]
    public void Golden_visible_region_clips_the_surface(string renderer)
    {
        CompositorTestHost.SkipUnlessRunnable(renderer);
        using var host = new CompositorTestHost(renderer: renderer);
        var appearance = new DefaultSurfaceAppearance();
        host.Scene.Appearance = appearance;

        var surface = host.Client.Compositor.CreateSurface();
        var buffer = host.Client.CreateBuffer(128, 96, Fill.Gradient(128, 96));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, 128, 96);
        surface.Commit();
        host.PumpToServer();

        var scene = host.SurfaceScenes[0];
        scene.Tree.SetPosition(8, 6);
        using var visible = new PixmanRegion32(16, 12, 64, 48);
        appearance.SetVisibleRegion(scene.Surface, visible);
        Assert.Equal(new Box(16, 12, 64, 48), scene.Content.VisibleBox);

        host.RenderFrame();
        Golden.AssertMatches(host, GoldenName("visible-region-surface", renderer));
    }

    [Fact]
    public void Clearing_the_appearance_restores_the_defaults()
    {
        using var host = new CompositorTestHost();
        var appearance = new DefaultSurfaceAppearance();
        host.Scene.Appearance = appearance;

        var surface = host.Client.Compositor.CreateSurface();
        var buffer = host.Client.CreateBuffer(64, 48, Fill.Solid(64, 48, 0xFF2020CC));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Commit();
        host.PumpToServer();

        var scene = host.SurfaceScenes[0];
        using var visible = new PixmanRegion32(0, 0, 10, 10);
        appearance.SetOpacity(scene.Surface, 0.25);
        appearance.SetVisibleRegion(scene.Surface, visible);
        Assert.Equal(0.25f, scene.Tree.Alpha);
        Assert.NotNull(scene.Content.VisibleBox);

        appearance.SetOpacity(scene.Surface, 1.0);
        appearance.ClearVisibleRegion(scene.Surface);
        Assert.Equal(1f, scene.Tree.Alpha);
        Assert.Null(scene.Content.VisibleBox);
        Assert.Equal(1.0, appearance.OpacityOf(scene.Surface));
        Assert.False(appearance.TryVisibleRegion(scene.Surface, out _));
    }

    [Fact]
    public void A_scene_surface_created_after_the_value_reads_it_on_its_first_commit()
    {
        using var host = new CompositorTestHost();
        var appearance = new DefaultSurfaceAppearance();
        host.Scene.Appearance = appearance;

        var surface = host.Client.Compositor.CreateSurface();
        host.PumpToServer();
        var server = host.SurfaceScenes[0].Surface;
        appearance.SetOpacity(server, 0.75);

        var late = new Scene.SceneSurface(host.Scene.Root, server);
        Assert.Equal(0.75f, late.Tree.Alpha);
        late.Destroy();
    }
}
