using Basin.Desktop;
using Basin.Scene;
using Xunit;

namespace Basin.Tests;

public sealed class SessionLockSceneDriverTests
{
    [Fact]
    public void A_lock_cycle_blanks_maps_and_restores()
    {
        using var host = new CompositorTestHost();
        var manager = new SessionLockManager(host.Display, host.Compositor);
        var layers = new SceneLayers(host.Scene.Root);
        var driver = new SessionLockSceneDriver(manager, host.Seat, layers.Lock, host.Layout, layers.SetLocked);
        var events = new List<string>();
        driver.Locked += () =>
        {
            events.Add("locked");
            Assert.False(layers.Windows.Enabled);
        };
        driver.Unlocked += () =>
        {
            events.Add("unlocked");
            Assert.True(layers.Windows.Enabled);
        };
        SceneSurface? lockScene = null;
        driver.LockSurfaceAdded += (_, scene) => lockScene = scene;

        var locker = host.ConnectClient();
        var managerProxy = BindLock(host, locker);
        var lockProxy = managerProxy.Lock();
        var gotLocked = false;
        lockProxy.Locked += (_, _) => gotLocked = true;
        host.PumpUntil(() => gotLocked);
        Assert.Equal(["locked"], events);
        Assert.False(layers.Background.Enabled);
        Assert.True(layers.Lock.Enabled);
        Assert.Null(host.Seat.Keyboard.Focus);

        var surface = locker.Compositor.CreateSurface();
        var lockSurfaceProxy = lockProxy.GetLockSurface(surface, locker.Outputs[0]);
        var size = (W: 0, H: 0);
        lockSurfaceProxy.Configure += (_, e) =>
        {
            size = ((int)e.Width, (int)e.Height);
            lockSurfaceProxy.AckConfigure(e.Serial);
        };
        host.PumpUntil(() => size.W != 0);

        var buffer = locker.CreateBuffer(size.W, size.H, Fill.Solid(size.W, size.H, 0xFF335577));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, size.W, size.H);
        surface.Commit();
        host.PumpUntil(() => lockScene is not null && host.Seat.Keyboard.Focus is not null);
        Assert.Same(layers.Lock, lockScene!.Tree.Parent);

        lockProxy.UnlockAndDestroy();
        host.PumpUntil(() => events.Contains("unlocked"));
        Assert.Equal(["locked", "unlocked"], events);
        Assert.True(layers.Background.Enabled);
        Assert.True(lockScene.IsDestroyed);
        host.DisconnectClient(locker);
    }

    [Fact]
    public void A_command_lock_blanks_without_a_client_and_a_protocol_lock_still_wins()
    {
        using var host = new CompositorTestHost();
        var manager = new SessionLockManager(host.Display, host.Compositor);
        var layers = new SceneLayers(host.Scene.Root);
        var driver = new SessionLockSceneDriver(manager, host.Seat, layers.Lock, host.Layout, layers.SetLocked);
        var events = new List<string>();
        driver.Locked += () => events.Add("locked");
        driver.Unlocked += () => events.Add("unlocked");

        driver.LockNow();
        Assert.False(layers.Windows.Enabled);
        Assert.Equal(["locked"], events);

        driver.UnlockNow();
        Assert.True(layers.Windows.Enabled);
        Assert.Equal(["locked", "unlocked"], events);
    }

    [Fact]
    public void Locked_is_sent_after_every_output_presents_a_frame()
    {
        using var host = new CompositorTestHost();
        var manager = new SessionLockManager(host.Display, host.Compositor, host.Layout);
        var layers = new SceneLayers(host.Scene.Root);
        _ = new SessionLockSceneDriver(manager, host.Seat, layers.Lock, host.Layout, layers.SetLocked);
        var state = new SessionLockState();
        state.Attach(manager);
        var observed = new List<string>();
        state.AddObserver(new LockRecorder(observed));

        var locker = host.ConnectClient();
        var lockProxy = BindLock(host, locker).Lock();
        var gotLocked = false;
        lockProxy.Locked += (_, _) => gotLocked = true;
        host.PumpToServer();
        host.PumpToClient();

        Assert.True(manager.IsLocked);
        Assert.False(manager.IsPresentedLocked);
        Assert.False(layers.Windows.Enabled);
        Assert.False(gotLocked);
        Assert.False(state.IsLocked);
        Assert.Empty(observed);

        host.RenderFrame();
        host.PumpToClient();
        Assert.False(gotLocked);

        host.Output.StepFrame();
        host.PumpUntil(() => gotLocked);
        Assert.True(manager.IsPresentedLocked);
        Assert.True(state.IsLocked);
        Assert.Equal(["locked"], observed);

        lockProxy.UnlockAndDestroy();
        host.PumpUntil(() => !manager.IsLocked);
        Assert.False(state.IsLocked);
        Assert.Equal(["locked", "unlocked"], observed);
    }

    private sealed class LockRecorder(List<string> events) : Basin.Capabilities.ILockStateObserver
    {
        public void SessionLocked() => events.Add("locked");

        public void SessionUnlocked() => events.Add("unlocked");
    }

    private static Basin.Desktop.Protocol.ExtSessionLockManagerV1 BindLock(CompositorTestHost host, ShmTestClient client)
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
}
