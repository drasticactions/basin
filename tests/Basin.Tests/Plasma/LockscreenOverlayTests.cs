using Basin.Capabilities;
using Basin.Desktop;
using Basin.Plasma;
using Basin.Scene;
using Wayland;
using Xunit;

namespace Basin.Tests;

public sealed class LockscreenOverlayTests
{
    [Fact]
    public void Allow_on_an_unmapped_surface_records_it()
    {
        using var host = new CompositorTestHost();
        var allowed = new LockOverlaySurfaces();
        using var manager = new LockscreenOverlayManager(host.Display, host.Compositor, allowed);

        var surface = host.Client.Compositor.CreateSurface();
        host.PumpToServer();
        var server = host.Compositor.Surfaces.Single();

        var proxy = Bind(host);
        proxy.Allow(surface);
        host.PumpToServer();

        Assert.True(allowed.IsAllowed(server));
        Assert.True(manager.Allowed.IsAllowed(server));
    }

    [Fact]
    public void Allow_on_a_mapped_surface_raises_invalid_surface_state()
    {
        using var host = new CompositorTestHost();
        var allowed = new LockOverlaySurfaces();
        using var manager = new LockscreenOverlayManager(host.Display, host.Compositor, allowed);

        var surface = host.Client.Compositor.CreateSurface();
        var buffer = host.Client.CreateBuffer(160, 120, Fill.Solid(160, 120, 0xFF335577));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Commit();
        host.PumpToServer();
        var server = host.Compositor.Surfaces.Single();
        Assert.True(server.IsMapped);

        var proxy = Bind(host);
        proxy.Allow(surface);
        var error = ExpectError(host);

        Assert.Equal(
            (int)Basin.Plasma.Protocol.KdeLockscreenOverlayV1.Error.InvalidSurfaceState, error.ErrorCode);
        Assert.Equal("kde_lockscreen_overlay_v1", error.InterfaceName);
        Assert.False(allowed.IsAllowed(server));
        host.DisconnectClient(host.Client);
    }

    [Fact]
    public void Destroying_the_manager_leaves_allowed_surfaces_allowed()
    {
        using var host = new CompositorTestHost();
        var allowed = new LockOverlaySurfaces();
        using var manager = new LockscreenOverlayManager(host.Display, host.Compositor, allowed);

        var surface = host.Client.Compositor.CreateSurface();
        host.PumpToServer();
        var server = host.Compositor.Surfaces.Single();

        var proxy = Bind(host);
        proxy.Allow(surface);
        host.PumpToServer();
        Assert.True(allowed.IsAllowed(server));

        proxy.Destroy();
        host.PumpToServer();

        Assert.True(allowed.IsAllowed(server));
        AssertClientAlive(host);
    }

    [Fact]
    public void Destroying_the_surface_drops_the_entry()
    {
        using var host = new CompositorTestHost();
        var allowed = new LockOverlaySurfaces();
        using var manager = new LockscreenOverlayManager(host.Display, host.Compositor, allowed);

        var surface = host.Client.Compositor.CreateSurface();
        host.PumpToServer();
        var server = host.Compositor.Surfaces.Single();

        var proxy = Bind(host);
        proxy.Allow(surface);
        host.PumpToServer();
        Assert.True(allowed.IsAllowed(server));

        surface.Destroy();
        host.PumpToServer();

        Assert.False(allowed.IsAllowed(server));
    }

    [Fact]
    public void A_surface_allowed_then_mapped_unmapped_and_remapped_stays_allowed()
    {
        using var host = new CompositorTestHost();
        var allowed = new LockOverlaySurfaces();
        using var manager = new LockscreenOverlayManager(host.Display, host.Compositor, allowed);

        var surface = host.Client.Compositor.CreateSurface();
        host.PumpToServer();
        var server = host.Compositor.Surfaces.Single();

        var proxy = Bind(host);
        proxy.Allow(surface);
        host.PumpToServer();

        var buffer = host.Client.CreateBuffer(160, 120, Fill.Solid(160, 120, 0xFF335577));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Commit();
        host.PumpToServer();
        Assert.True(server.IsMapped);
        Assert.True(allowed.IsAllowed(server));

        surface.Attach(null, 0, 0);
        surface.Commit();
        host.PumpToServer();
        Assert.False(server.IsMapped);
        Assert.True(allowed.IsAllowed(server));

        surface.Attach(buffer.Proxy, 0, 0);
        surface.Commit();
        host.PumpToServer();
        Assert.True(server.IsMapped);
        Assert.True(allowed.IsAllowed(server));
    }

    [Fact]
    public void A_consumer_registered_implementation_wins_over_the_module_default()
    {
        using var host = new CompositorTestHost();
        var strict = new RefuseEverything();
        using var services = new BasinServices(host.Display, host.Loop)
            .Use(host.Compositor)
            .Use<ILockOverlaySurfaces>(strict)
            .Install(new LockscreenOverlayModule())
            .Freeze();

        Assert.Same(strict, services.Require<ILockOverlaySurfaces>());

        var surface = host.Client.Compositor.CreateSurface();
        host.PumpToServer();
        var server = host.Compositor.Surfaces.Single();

        var proxy = Bind(host);
        proxy.Allow(surface);
        host.PumpToServer();

        Assert.False(services.Require<ILockOverlaySurfaces>().IsAllowed(server));
        var manager = services.Module<LockscreenOverlayModule>()!.Manager!;
        Assert.True(manager.Allowed.IsAllowed(server));
    }

    [Fact]
    public void A_consumer_that_honours_the_list_composites_only_the_allowed_surface_while_locked()
    {
        using var host = new CompositorTestHost();
        var allowed = new LockOverlaySurfaces();
        using var overlayManager = new LockscreenOverlayManager(host.Display, host.Compositor, allowed);
        var sessionLock = new SessionLockManager(host.Display, host.Compositor);

        var servers = new List<Surface>();
        host.Compositor.SurfaceCreated += servers.Add;

        var overlaySurface = host.Client.Compositor.CreateSurface();
        var ordinarySurface = host.Client.Compositor.CreateSurface();
        host.PumpToServer();

        var proxy = Bind(host);
        proxy.Allow(overlaySurface);
        host.PumpToServer();

        var windowTree = new SceneTree(host.Scene.Root);
        var lockTree = new SceneTree(host.Scene.Root);
        foreach (var scene in host.SurfaceScenes.ToList())
        {
            scene.Destroy();
        }

        var overlayScene = new SceneSurface(windowTree, servers[0]);
        var ordinaryScene = new SceneSurface(windowTree, servers[1]);
        ordinaryScene.Tree.SetPosition(80, 0);

        var blue = host.Client.CreateBuffer(80, 120, Fill.Solid(80, 120, 0xFF3355FF));
        overlaySurface.Attach(blue.Proxy, 0, 0);
        overlaySurface.Damage(0, 0, 80, 120);
        overlaySurface.Commit();
        var red = host.Client.CreateBuffer(80, 120, Fill.Solid(80, 120, 0xFFCC2222));
        ordinarySurface.Attach(red.Proxy, 0, 0);
        ordinarySurface.Damage(0, 0, 80, 120);
        ordinarySurface.Commit();
        host.PumpToServer();

        host.RenderFrame();
        Assert.Equal(0xFF3355FFu, host.Pixel(40, 60));
        Assert.Equal(0xFFCC2222u, host.Pixel(120, 60));

        var locker = host.ConnectClient();
        var lockProxy = BindLock(host, locker).Lock();
        var gotLocked = false;
        lockProxy.Locked += (_, _) => gotLocked = true;
        host.PumpUntil(() => gotLocked);
        Assert.True(sessionLock.IsLocked);

        windowTree.Enabled = false;
        SceneSurface? raised = null;
        foreach (var server in servers)
        {
            if (server.IsMapped && allowed.IsAllowed(server))
            {
                raised = new SceneSurface(lockTree, server);
            }
        }

        Assert.NotNull(raised);
        Assert.Same(servers[0], raised!.Surface);

        host.RenderFrame();
        Assert.Equal(0xFF3355FFu, host.Pixel(40, 60));
        Assert.Equal(0xFF000000u, host.Pixel(120, 60));

        AssertClientAlive(host);
        lockProxy.UnlockAndDestroy();
        host.PumpToServer();
        sessionLock.Dispose();
    }

    [Fact]
    public void With_no_consumer_reading_the_list_nothing_is_composited_while_locked()
    {
        using var host = new CompositorTestHost();
        var allowed = new LockOverlaySurfaces();
        using var overlayManager = new LockscreenOverlayManager(host.Display, host.Compositor, allowed);
        var sessionLock = new SessionLockManager(host.Display, host.Compositor);

        var surface = host.Client.Compositor.CreateSurface();
        host.PumpToServer();

        var proxy = Bind(host);
        proxy.Allow(surface);
        host.PumpToServer();

        var windowTree = new SceneTree(host.Scene.Root);
        foreach (var scene in host.SurfaceScenes.ToList())
        {
            scene.Destroy();
        }

        _ = new SceneSurface(windowTree, host.Compositor.Surfaces.Single());
        var buffer = host.Client.CreateBuffer(160, 120, Fill.Solid(160, 120, 0xFF3355FF));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, 160, 120);
        surface.Commit();
        host.PumpToServer();

        var locker = host.ConnectClient();
        var lockProxy = BindLock(host, locker).Lock();
        var gotLocked = false;
        lockProxy.Locked += (_, _) => gotLocked = true;
        host.PumpUntil(() => gotLocked);
        Assert.True(sessionLock.IsLocked);

        windowTree.Enabled = false;
        host.RenderFrame();
        Assert.Equal(0xFF000000u, host.Pixel(80, 60));

        AssertClientAlive(host);
        lockProxy.UnlockAndDestroy();
        host.PumpToServer();
        sessionLock.Dispose();
    }

    private sealed class RefuseEverything : ILockOverlaySurfaces
    {
        public bool IsAllowed(Surface surface) => false;
    }

    private static Basin.Plasma.Protocol.KdeLockscreenOverlayV1 Bind(
        CompositorTestHost host, ShmTestClient? client = null)
    {
        client ??= host.Client;
        Basin.Plasma.Protocol.KdeLockscreenOverlayV1? proxy = null;
        var registry = client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "kde_lockscreen_overlay_v1")
            {
                proxy = registry.Bind<Basin.Plasma.Protocol.KdeLockscreenOverlayV1>(e.Name, 1);
            }
        };
        host.PumpToClient();
        Assert.NotNull(proxy);
        host.PumpToServer();
        return proxy!;
    }

    private static Basin.Desktop.Protocol.ExtSessionLockManagerV1 BindLock(
        CompositorTestHost host, ShmTestClient client)
    {
        Basin.Desktop.Protocol.ExtSessionLockManagerV1? manager = null;
        var registry = client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "ext_session_lock_manager_v1")
            {
                manager = registry.Bind<Basin.Desktop.Protocol.ExtSessionLockManagerV1>(e.Name, 1);
            }
        };
        host.PumpToClient();
        Assert.NotNull(manager);
        return manager!;
    }

    private static WaylandProtocolException ExpectError(CompositorTestHost host)
    {
        for (var i = 0; i < 20; i++)
        {
            try
            {
                host.PumpToClient();
                host.PumpToServer();
            }
            catch (WaylandProtocolException error)
            {
                return error;
            }
        }

        throw new TimeoutException("no protocol error arrived while pumping");
    }

    private static void AssertClientAlive(CompositorTestHost host)
    {
        var sync = host.Client.Display.Sync();
        var done = false;
        sync.Done += (_, _) => done = true;
        host.PumpUntil(() => done);
        Assert.True(done);
    }
}
