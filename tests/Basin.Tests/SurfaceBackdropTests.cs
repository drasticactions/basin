using Basin.Desktop;
using Basin.Scene;
using Wayland;
using Xunit;

namespace Basin.Tests;

public sealed class SurfaceBackdropTests
{
    [Fact]
    public void A_client_region_reaches_the_node_and_unsetting_it_clears_the_node()
    {
        using var host = new CompositorTestHost();
        using var manager = new BackgroundEffectManager(host.Display, host.Compositor, new BlurCapable());
        var effect = new RecordingEffect();
        host.Scene.SurfaceBackdrop = new BackgroundBlurDriver(manager, effect);
        var (surface, scene) = MapSurface(host, 40, 30);
        Assert.Null(scene.Content.BackdropEffect);

        var blur = Bind(host).GetBackgroundEffect(surface);
        SetRegion(host, blur, 5, 5, 10, 10);
        surface.Commit();
        host.PumpToServer();

        Assert.Same(effect, scene.Content.BackdropEffect);
        Assert.Same(scene.Content, scene.Content.BackdropKey);
        Assert.Equal((5, 5, 15, 15), Extents(scene.Content.BackdropRegion!));

        blur.SetBlurRegion(null);
        surface.Commit();
        host.PumpToServer();

        Assert.Null(scene.Content.BackdropEffect);
        Assert.False(scene.Content.HasActiveBackdrop);
    }

    [Fact]
    public void Destroying_the_effect_object_clears_the_node_on_the_next_commit()
    {
        using var host = new CompositorTestHost();
        using var manager = new BackgroundEffectManager(host.Display, host.Compositor, new BlurCapable());
        host.Scene.SurfaceBackdrop = new BackgroundBlurDriver(manager, new RecordingEffect());
        var (surface, scene) = MapSurface(host, 40, 30);

        var blur = Bind(host).GetBackgroundEffect(surface);
        SetRegion(host, blur, 0, 0, 40, 30);
        surface.Commit();
        host.PumpToServer();
        Assert.True(scene.Content.HasActiveBackdrop);

        blur.Destroy();
        surface.Commit();
        host.PumpToServer();
        Assert.False(scene.Content.HasActiveBackdrop);
    }

    [Fact]
    public void Destroying_the_surface_forgets_its_key()
    {
        using var host = new CompositorTestHost();
        using var manager = new BackgroundEffectManager(host.Display, host.Compositor, new BlurCapable());
        var effect = new RecordingEffect();
        host.Scene.SurfaceBackdrop = new BackgroundBlurDriver(manager, effect);
        var (surface, scene) = MapSurface(host, 40, 30);

        var blur = Bind(host).GetBackgroundEffect(surface);
        SetRegion(host, blur, 0, 0, 40, 30);
        surface.Commit();
        host.PumpToServer();

        var key = scene.Content;
        blur.Destroy();
        surface.Destroy();
        host.PumpToServer();

        Assert.True(scene.IsDestroyed);
        Assert.Contains(key, effect.Forgotten);
    }

    [Fact]
    public void A_subsurface_region_reaches_the_child_node()
    {
        using var host = new CompositorTestHost();
        using var manager = new BackgroundEffectManager(host.Display, host.Compositor, new BlurCapable());
        var effect = new RecordingEffect();
        host.Scene.SurfaceBackdrop = new BackgroundBlurDriver(manager, effect);
        Surface? childServer = null;
        var (parent, _) = MapSurface(host, 60, 50);
        host.Compositor.SurfaceCreated += s => childServer ??= s;

        var child = host.Client.Compositor.CreateSurface();
        var subsurface = host.Client.Subcompositor.GetSubsurface(child, parent);
        subsurface.SetPosition(10, 10);
        var buffer = host.Client.CreateBuffer(20, 20, Fill.Solid(20, 20, 0x80000000), WlShm.Format.Argb8888);
        child.Attach(buffer.Proxy, 0, 0);
        var blur = Bind(host).GetBackgroundEffect(child);
        SetRegion(host, blur, 2, 2, 8, 8);
        child.Commit();
        parent.Commit();
        host.PumpToServer();

        var node = FindContent(host.Scene.Root, childServer!);
        Assert.NotNull(node);
        Assert.Same(effect, node!.BackdropEffect);
        Assert.Equal((2, 2, 10, 10), Extents(node.BackdropRegion!));
    }

    [Fact]
    public void A_viewport_scaled_region_is_in_surface_coordinates()
    {
        using var host = new CompositorTestHost();
        using var manager = new BackgroundEffectManager(host.Display, host.Compositor, new BlurCapable());
        host.Scene.SurfaceBackdrop = new BackgroundBlurDriver(manager, new RecordingEffect());
        var surface = host.Client.Compositor.CreateSurface();
        host.PumpToServer();
        var scene = host.SurfaceScenes[^1];
        var viewport = host.Client.Viewporter!.GetViewport(surface);
        viewport.SetDestination(80, 60);
        var buffer = host.Client.CreateBuffer(20, 15, Fill.Solid(20, 15, 0x80000000), WlShm.Format.Argb8888);
        surface.Attach(buffer.Proxy, 0, 0);
        var blur = Bind(host).GetBackgroundEffect(surface);
        SetRegion(host, blur, 0, 0, 1000, 1000);
        surface.Commit();
        host.PumpToServer();

        Assert.Equal((0, 0, 80, 60), Extents(scene.Content.BackdropRegion!));
    }

    [Fact]
    public void Removing_the_seam_clears_what_it_set_and_nothing_else()
    {
        using var host = new CompositorTestHost();
        using var manager = new BackgroundEffectManager(host.Display, host.Compositor, new BlurCapable());
        var effect = new RecordingEffect();
        host.Scene.SurfaceBackdrop = new BackgroundBlurDriver(manager, effect);
        var (surface, scene) = MapSurface(host, 40, 30);
        var (_, other) = MapSurface(host, 40, 30);
        using var own = new Pixman.PixmanRegion32(0, 0, 4, 4);
        var consumer = new RecordingEffect();
        other.Content.SetBackdropEffect(consumer, own);

        var blur = Bind(host).GetBackgroundEffect(surface);
        SetRegion(host, blur, 0, 0, 40, 30);
        surface.Commit();
        host.PumpToServer();
        Assert.True(scene.Content.HasActiveBackdrop);

        host.Scene.SurfaceBackdrop = null;

        Assert.False(scene.Content.HasActiveBackdrop);
        Assert.Contains(scene.Content, effect.Forgotten);
        Assert.Same(consumer, other.Content.BackdropEffect);
    }

    private static (WlSurface Surface, SceneSurface Scene) MapSurface(CompositorTestHost host, int width, int height)
    {
        var surface = host.Client.Compositor.CreateSurface();
        host.PumpToServer();
        var scene = host.SurfaceScenes[^1];
        var buffer = host.Client.CreateBuffer(width, height, Fill.Solid(width, height, 0x80000000), WlShm.Format.Argb8888);
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Commit();
        host.PumpToServer();
        return (surface, scene);
    }

    private static void SetRegion(CompositorTestHost host, Basin.Desktop.Protocol.ExtBackgroundEffectSurfaceV1 blur, int x, int y, int width, int height)
    {
        var region = host.Client.Compositor.CreateRegion();
        region.Add(x, y, width, height);
        blur.SetBlurRegion(region);
        region.Destroy();
    }

    private static SceneBuffer? FindContent(SceneTree tree, Surface surface)
    {
        foreach (var child in tree.Children)
        {
            if (child is SceneBuffer buffer && ReferenceEquals(buffer.InputSurface, surface))
            {
                return buffer;
            }

            if (child is SceneTree subtree && FindContent(subtree, surface) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static Basin.Desktop.Protocol.ExtBackgroundEffectManagerV1 Bind(CompositorTestHost host)
    {
        Basin.Desktop.Protocol.ExtBackgroundEffectManagerV1? proxy = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "ext_background_effect_manager_v1")
            {
                proxy = registry.Bind<Basin.Desktop.Protocol.ExtBackgroundEffectManagerV1>(e.Name, 1);
            }
        };
        host.PumpToClient();
        Assert.NotNull(proxy);
        return proxy!;
    }

    private static (int, int, int, int) Extents(Pixman.PixmanRegion32 region)
    {
        var extents = region.Extents;
        return (extents.X1, extents.Y1, extents.X2, extents.Y2);
    }

    private sealed class RecordingEffect : IBackdropEffect
    {
        public List<object> Forgotten { get; } = [];

        public bool ForgetSurface(object key)
        {
            Forgotten.Add(key);
            return true;
        }
    }

    private sealed class BlurCapable : Basin.Capabilities.IBackgroundEffects
    {
        public Basin.Capabilities.BackgroundEffects Supported => Basin.Capabilities.BackgroundEffects.Blur;
    }
}
