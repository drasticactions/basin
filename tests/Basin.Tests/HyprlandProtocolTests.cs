using System.Runtime.InteropServices;
using Basin.Capabilities;
using Basin.Capabilities.Defaults;
using Basin.Desktop;
using Basin.Hypr;
using Basin.Scene;
using Wayland;
using Xunit;

namespace Basin.Tests;

internal static class HyprlandTestSupport
{
    public static T Bind<T>(CompositorTestHost host, string wireInterface, uint version, ShmTestClient? client = null)
        where T : WlProxy, IWaylandObject<T>
    {
        T? proxy = null;
        var registry = (client ?? host.Client).Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == wireInterface)
            {
                proxy = registry.Bind<T>(e.Name, version);
            }
        };
        host.PumpToClient();
        Assert.NotNull(proxy);
        return proxy!;
    }

    public static void AssertAlive(CompositorTestHost host, ShmTestClient? client = null)
    {
        host.PumpToServer();
        host.PumpToClient();
        var sync = (client ?? host.Client).Display.Sync();
        var done = false;
        sync.Done += (_, _) => done = true;
        host.PumpUntil(() => done);
        Assert.True(done);
    }

    public static void AssertKilled(CompositorTestHost host)
    {
        host.PumpToServer();
        host.Display.FlushClients();
        Assert.ThrowsAny<Exception>(() =>
        {
            host.Client.Display.Dispatch();
            host.Client.Display.Roundtrip();
        });
    }

    public static uint Pixel(ClientShmBuffer buffer, int x, int y) =>
        (uint)Marshal.ReadInt32(buffer.Data, (y * buffer.Stride) + (x * 4));
}

public sealed class HyprlandSurfaceTests
{
    [Fact]
    public void Opacity_and_visible_region_land_on_commit_and_reset_on_the_commit_after_destroy()
    {
        using var host = new CompositorTestHost();
        var appearance = new DefaultSurfaceAppearance();
        host.Scene.Appearance = appearance;
        using var manager = new HyprlandSurfaceManager(host.Display, host.Compositor, appearance);
        var window = MappedToplevel.Map(host, host.Client);
        var scene = host.SurfaceScenes.Single(s => ReferenceEquals(s.Surface, window.ServerSurface));

        var proxy = HyprlandTestSupport.Bind<Basin.Hypr.Protocol.HyprlandSurfaceManagerV1>(host, "hyprland_surface_manager_v1", 2);
        var hypr = proxy.GetHyprlandSurface(window.Surface);
        hypr.SetOpacity(WlFixed.FromDouble(0.5));
        var region = host.Client.Compositor.CreateRegion();
        region.Add(10, 10, 20, 20);
        hypr.SetVisibleRegion(region);
        region.Destroy();
        host.PumpToServer();

        Assert.Equal(1.0, appearance.OpacityOf(window.ServerSurface));
        Assert.Equal(1f, scene.Tree.Alpha);
        Assert.Null(scene.Content.VisibleBox);

        window.Surface.Commit();
        host.PumpToServer();
        Assert.Equal(0.5, appearance.OpacityOf(window.ServerSurface), 3);
        Assert.Equal(0.5f, scene.Tree.Alpha, 3);
        Assert.Equal(new Box(10, 10, 20, 20), scene.Content.VisibleBox);
        Assert.True(appearance.TryVisibleRegion(window.ServerSurface, out _));

        hypr.Destroy();
        host.PumpToServer();
        Assert.Equal(0.5, appearance.OpacityOf(window.ServerSurface), 3);

        window.Surface.Commit();
        host.PumpToServer();
        Assert.Equal(1.0, appearance.OpacityOf(window.ServerSurface));
        Assert.Equal(1f, scene.Tree.Alpha);
        Assert.Null(scene.Content.VisibleBox);
        Assert.False(manager.IsClaimed(window.ServerSurface));
        HyprlandTestSupport.AssertAlive(host);
    }

    [Fact]
    public void A_null_region_clears_the_visible_region()
    {
        using var host = new CompositorTestHost();
        var appearance = new DefaultSurfaceAppearance();
        using var manager = new HyprlandSurfaceManager(host.Display, host.Compositor, appearance);
        var window = MappedToplevel.Map(host, host.Client);

        var proxy = HyprlandTestSupport.Bind<Basin.Hypr.Protocol.HyprlandSurfaceManagerV1>(host, "hyprland_surface_manager_v1", 2);
        var hypr = proxy.GetHyprlandSurface(window.Surface);
        var region = host.Client.Compositor.CreateRegion();
        region.Add(0, 0, 5, 5);
        hypr.SetVisibleRegion(region);
        window.Surface.Commit();
        host.PumpToServer();
        Assert.True(appearance.TryVisibleRegion(window.ServerSurface, out _));

        hypr.SetVisibleRegion(null);
        window.Surface.Commit();
        host.PumpToServer();
        Assert.False(appearance.TryVisibleRegion(window.ServerSurface, out _));
        HyprlandTestSupport.AssertAlive(host);
    }

    [Fact]
    public void A_second_hyprland_surface_for_the_same_wl_surface_is_already_constructed()
    {
        using var host = new CompositorTestHost();
        using var manager = new HyprlandSurfaceManager(host.Display, host.Compositor, new DefaultSurfaceAppearance());
        var window = MappedToplevel.Map(host, host.Client);

        var first = HyprlandTestSupport.Bind<Basin.Hypr.Protocol.HyprlandSurfaceManagerV1>(host, "hyprland_surface_manager_v1", 2);
        var second = HyprlandTestSupport.Bind<Basin.Hypr.Protocol.HyprlandSurfaceManagerV1>(host, "hyprland_surface_manager_v1", 2);
        _ = first.GetHyprlandSurface(window.Surface);
        host.PumpToServer();
        HyprlandTestSupport.AssertAlive(host);

        _ = second.GetHyprlandSurface(window.Surface);
        HyprlandTestSupport.AssertKilled(host);
    }

    [Fact]
    public void An_opacity_outside_the_range_is_out_of_range()
    {
        using var host = new CompositorTestHost();
        using var manager = new HyprlandSurfaceManager(host.Display, host.Compositor, new DefaultSurfaceAppearance());
        var window = MappedToplevel.Map(host, host.Client);

        var proxy = HyprlandTestSupport.Bind<Basin.Hypr.Protocol.HyprlandSurfaceManagerV1>(host, "hyprland_surface_manager_v1", 2);
        var hypr = proxy.GetHyprlandSurface(window.Surface);
        hypr.SetOpacity(WlFixed.FromDouble(1.5));
        HyprlandTestSupport.AssertKilled(host);
    }

    [Fact]
    public void A_request_after_the_wl_surface_is_gone_is_no_surface()
    {
        using var host = new CompositorTestHost();
        using var manager = new HyprlandSurfaceManager(host.Display, host.Compositor, new DefaultSurfaceAppearance());
        var surface = host.Client.Compositor.CreateSurface();

        var proxy = HyprlandTestSupport.Bind<Basin.Hypr.Protocol.HyprlandSurfaceManagerV1>(host, "hyprland_surface_manager_v1", 2);
        var hypr = proxy.GetHyprlandSurface(surface);
        host.PumpToServer();
        surface.Destroy();
        host.PumpToServer();

        hypr.SetOpacity(WlFixed.FromDouble(0.5));
        HyprlandTestSupport.AssertKilled(host);
    }
}

public sealed class HyprlandFocusGrabTests
{
    [Fact]
    public void A_committed_whitelist_enters_the_keyboard_and_a_click_outside_clears_without_delivery()
    {
        using var host = new CompositorTestHost();
        using var manager = new HyprlandFocusGrabManager(host.Display, host.Compositor, host.Seat);
        var menu = MappedToplevel.Map(host, host.Client);
        var other = MappedToplevel.Map(host, host.Client);
        host.Seat.Keyboard.NotifyEnter(other.ServerSurface);

        var keyboard = host.Client.Seat!.GetKeyboard();
        var entered = new List<WlSurface?>();
        keyboard.Enter += (_, e) => entered.Add(e.Surface);
        var pointer = host.Client.Seat.GetPointer();
        var buttons = 0;
        pointer.Button += (_, _) => buttons++;
        host.PumpToClient();

        var proxy = HyprlandTestSupport.Bind<Basin.Hypr.Protocol.HyprlandFocusGrabManagerV1>(host, "hyprland_focus_grab_manager_v1", 1);
        var grab = proxy.CreateGrab();
        var cleared = 0;
        grab.Cleared += (_, _) => cleared++;
        grab.AddSurface(menu.Surface);
        host.PumpToServer();
        Assert.False(manager.IsGrabbing);

        grab.Commit();
        host.PumpToServer();
        Assert.True(manager.IsGrabbing);
        Assert.Same(menu.ServerSurface, host.Seat.Keyboard.Focus);
        host.PumpToClient();
        Assert.Equal(menu.Surface, entered.Last());

        host.Seat.Pointer.NotifyMotionAt(1, menu.ServerSurface, 5, 5, 5, 5);
        host.Seat.Pointer.NotifyButton(2, InputCodes.BtnLeft, true);
        host.Seat.Pointer.NotifyButton(3, InputCodes.BtnLeft, false);
        host.PumpToClient();
        Assert.Equal(2, buttons);
        Assert.Equal(0, cleared);

        host.Seat.Pointer.NotifyMotionAt(4, other.ServerSurface, 5, 5, 105, 5);
        host.Seat.Pointer.NotifyButton(5, InputCodes.BtnLeft, true);
        host.PumpToClient();
        Assert.Equal(2, buttons);
        Assert.False(manager.IsGrabbing);
        Assert.Equal(1, cleared);
        Assert.Same(other.ServerSurface, host.Seat.Keyboard.Focus);
        HyprlandTestSupport.AssertAlive(host);
    }

    [Fact]
    public void A_second_grab_clears_the_first_and_an_empty_commit_makes_a_grab_inert()
    {
        using var host = new CompositorTestHost();
        using var manager = new HyprlandFocusGrabManager(host.Display, host.Compositor, host.Seat);
        var first = MappedToplevel.Map(host, host.Client);
        var second = MappedToplevel.Map(host, host.Client);

        var proxy = HyprlandTestSupport.Bind<Basin.Hypr.Protocol.HyprlandFocusGrabManagerV1>(host, "hyprland_focus_grab_manager_v1", 1);
        var grabA = proxy.CreateGrab();
        var clearedA = 0;
        grabA.Cleared += (_, _) => clearedA++;
        grabA.AddSurface(first.Surface);
        grabA.Commit();
        host.PumpToServer();
        Assert.True(manager.IsGrabbing);

        var grabB = proxy.CreateGrab();
        var clearedB = 0;
        grabB.Cleared += (_, _) => clearedB++;
        grabB.AddSurface(second.Surface);
        grabB.Commit();
        host.PumpToServer();
        host.PumpToClient();
        Assert.Equal(1, clearedA);
        Assert.Equal(0, clearedB);
        Assert.Same(second.ServerSurface, host.Seat.Keyboard.Focus);

        grabB.RemoveSurface(second.Surface);
        grabB.Commit();
        host.PumpToServer();
        host.PumpToClient();
        Assert.False(manager.IsGrabbing);
        Assert.Equal(1, clearedB);
        HyprlandTestSupport.AssertAlive(host);
    }

    [Fact]
    public void Destroying_a_whitelisted_surface_removes_it()
    {
        using var host = new CompositorTestHost();
        using var manager = new HyprlandFocusGrabManager(host.Display, host.Compositor, host.Seat);
        var window = MappedToplevel.Map(host, host.Client);

        var proxy = HyprlandTestSupport.Bind<Basin.Hypr.Protocol.HyprlandFocusGrabManagerV1>(host, "hyprland_focus_grab_manager_v1", 1);
        var grab = proxy.CreateGrab();
        var cleared = 0;
        grab.Cleared += (_, _) => cleared++;
        grab.AddSurface(window.Surface);
        grab.Commit();
        host.PumpToServer();
        Assert.True(manager.IsGrabbing);

        window.Toplevel.Destroy();
        window.XdgSurface.Destroy();
        window.Surface.Destroy();
        host.PumpToServer();
        host.PumpToClient();
        Assert.False(manager.IsGrabbing);
        Assert.Equal(1, cleared);
        HyprlandTestSupport.AssertAlive(host);
    }
}

public sealed class HyprlandLockNotifyTests
{
    [Fact]
    public void Notifications_follow_the_lock_state_and_a_late_one_is_told_at_once()
    {
        using var host = new CompositorTestHost();
        var lockManager = new SessionLockManager(host.Display, host.Compositor);
        var state = new SessionLockState();
        state.Attach(lockManager);
        using var notifier = new HyprlandLockNotifier(host.Display, state);

        var proxy = HyprlandTestSupport.Bind<Basin.Hypr.Protocol.HyprlandLockNotifierV1>(host, "hyprland_lock_notifier_v1", 1);
        var early = proxy.GetLockNotification();
        var events = new List<string>();
        early.Locked += (_, _) => events.Add("locked");
        early.Unlocked += (_, _) => events.Add("unlocked");
        host.PumpToServer();

        var locker = host.ConnectClient();
        var lockProxy = HyprlandTestSupport.Bind<Basin.Desktop.Protocol.ExtSessionLockManagerV1>(host, "ext_session_lock_manager_v1", 1, locker).Lock();
        host.PumpUntil(() => events.Count == 1);
        Assert.Equal(["locked"], events);

        var late = proxy.GetLockNotification();
        var lateEvents = new List<string>();
        late.Locked += (_, _) => lateEvents.Add("locked");
        late.Unlocked += (_, _) => lateEvents.Add("unlocked");
        host.PumpUntil(() => lateEvents.Count == 1);
        Assert.Equal(["locked"], lateEvents);

        lockProxy.UnlockAndDestroy();
        host.PumpUntil(() => events.Count == 2 && lateEvents.Count == 2);
        Assert.Equal(["locked", "unlocked"], events);
        Assert.Equal(["locked", "unlocked"], lateEvents);
        HyprlandTestSupport.AssertAlive(host);
    }
}

public sealed class HyprlandToplevelMappingTests
{
    [Fact]
    public void Both_handle_kinds_resolve_to_the_same_address()
    {
        using var host = new CompositorTestHost();
        var model = new TestToplevelModel();
        using var ext = new ForeignToplevelListManager(host.Display, model);
        using var wlr = new ForeignToplevelManager(host.Display, model);
        using var mapping = new HyprlandToplevelMappingManager(host.Display, model);
        var id = model.Add("a", "b");

        Basin.Desktop.Protocol.ExtForeignToplevelHandleV1? extHandle = null;
        Basin.Desktop.Protocol.ZwlrForeignToplevelHandleV1? wlrHandle = null;
        var list = HyprlandTestSupport.Bind<Basin.Desktop.Protocol.ExtForeignToplevelListV1>(host, "ext_foreign_toplevel_list_v1", 1);
        list.Toplevel += (_, e) => extHandle = e.Toplevel;
        var manager = HyprlandTestSupport.Bind<Basin.Desktop.Protocol.ZwlrForeignToplevelManagerV1>(host, "zwlr_foreign_toplevel_manager_v1", 3);
        manager.Toplevel += (_, e) => wlrHandle = e.Toplevel;
        host.PumpUntil(() => extHandle is not null && wlrHandle is not null);

        var proxy = HyprlandTestSupport.Bind<Basin.Hypr.Protocol.HyprlandToplevelMappingManagerV1>(host, "hyprland_toplevel_mapping_manager_v1", 1);
        var addresses = new List<ulong>();
        var first = proxy.GetWindowForToplevel(extHandle!);
        first.WindowAddress += (_, e) => addresses.Add(((ulong)e.AddressHi << 32) | e.Address);
        var second = proxy.GetWindowForToplevelWlr(wlrHandle!);
        second.WindowAddress += (_, e) => addresses.Add(((ulong)e.AddressHi << 32) | e.Address);
        host.PumpUntil(() => addresses.Count == 2);

        Assert.Equal([id, id], addresses);
        HyprlandTestSupport.AssertAlive(host);
    }
}

public sealed class HyprlandToplevelExportTests
{
    [Fact]
    public void A_frame_announces_the_buffer_and_copies_on_damage()
    {
        using var host = new CompositorTestHost();
        var model = new TestToplevelModel();
        var capture = new ToplevelCapture(host);
        using var manager = new HyprlandToplevelExportManager(host.Display, host.Layout, host.Buffers, capture, model);
        var id = model.Add("a", "b", geometry: new Box(0, 0, 60, 50));

        var proxy = HyprlandTestSupport.Bind<Basin.Hypr.Protocol.HyprlandToplevelExportManagerV1>(host, "hyprland_toplevel_export_manager_v1", 2);
        var frame = proxy.CaptureToplevel(0, (uint)id);
        (uint Width, uint Height, uint Stride)? announced = null;
        var done = false;
        var ready = false;
        var flags = false;
        var damages = new List<(uint X, uint Y, uint W, uint H)>();
        frame.Buffer += (_, e) => announced = (e.Width, e.Height, e.Stride);
        frame.BufferDone += (_, _) => done = true;
        frame.FlagsEvent += (_, _) => flags = true;
        frame.Damage += (_, e) => damages.Add((e.X, e.Y, e.Width, e.Height));
        frame.Ready += (_, _) => ready = true;
        host.PumpUntil(() => done);
        Assert.Equal((60u, 50u, 240u), announced);

        var buffer = host.Client.CreateBuffer(60, 50, Fill.Solid(60, 50, 0));
        frame.Copy(buffer.Proxy, 0);
        host.PumpToServer();
        Assert.False(ready);
        Assert.Equal(1, manager.WaitingFrames);

        capture.Damage(host.Output, new Box(10, 10, 20, 20));
        host.PumpUntil(() => ready);
        Assert.True(flags);
        Assert.Single(damages);
        Assert.Equal([id], capture.Captured);
        HyprlandTestSupport.AssertAlive(host);
    }

    [Fact]
    public void Ignore_damage_copies_at_once_and_a_second_copy_is_already_used()
    {
        using var host = new CompositorTestHost();
        var model = new TestToplevelModel();
        var capture = new ToplevelCapture(host);
        using var manager = new HyprlandToplevelExportManager(host.Display, host.Layout, host.Buffers, capture, model);
        var id = model.Add("a", "b", geometry: new Box(0, 0, 60, 50));

        var proxy = HyprlandTestSupport.Bind<Basin.Hypr.Protocol.HyprlandToplevelExportManagerV1>(host, "hyprland_toplevel_export_manager_v1", 2);
        var frame = proxy.CaptureToplevel(0, (uint)id);
        var done = false;
        var ready = false;
        frame.BufferDone += (_, _) => done = true;
        frame.Ready += (_, _) => ready = true;
        host.PumpUntil(() => done);

        var buffer = host.Client.CreateBuffer(60, 50, Fill.Solid(60, 50, 0));
        frame.Copy(buffer.Proxy, 1);
        host.PumpUntil(() => ready);

        frame.Copy(buffer.Proxy, 1);
        HyprlandTestSupport.AssertKilled(host);
    }

    [Fact]
    public void A_buffer_of_the_wrong_size_is_invalid_buffer()
    {
        using var host = new CompositorTestHost();
        var model = new TestToplevelModel();
        var capture = new ToplevelCapture(host);
        using var manager = new HyprlandToplevelExportManager(host.Display, host.Layout, host.Buffers, capture, model);
        var id = model.Add("a", "b", geometry: new Box(0, 0, 60, 50));

        var proxy = HyprlandTestSupport.Bind<Basin.Hypr.Protocol.HyprlandToplevelExportManagerV1>(host, "hyprland_toplevel_export_manager_v1", 2);
        var frame = proxy.CaptureToplevel(0, (uint)id);
        var done = false;
        frame.BufferDone += (_, _) => done = true;
        host.PumpUntil(() => done);

        var buffer = host.Client.CreateBuffer(30, 30, Fill.Solid(30, 30, 0));
        frame.Copy(buffer.Proxy, 1);
        HyprlandTestSupport.AssertKilled(host);
    }

    [Fact]
    public void A_32_bit_handle_resolves_an_aggregate_id_by_its_low_word()
    {
        var xdg = new TestToplevelModel();
        var x11 = new TestToplevelModel();
        var aggregate = new AggregateToplevelModel();
        aggregate.Add(new SourceOf(xdg));
        aggregate.Add(new SourceOf(x11));
        _ = xdg.Add("a", "b");
        _ = x11.Add("c", "d");
        var only = x11.Add("e", "f");
        var infos = new ToplevelInfo[4];
        Assert.Equal(3, aggregate.Enumerate(infos));
        var ids = infos.Take(3).Select(i => i.Id).ToArray();
        Assert.All(ids, id => Assert.True(id > uint.MaxValue));

        Assert.Equal(ids[2], HyprlandToplevelExportManager.ResolveHandle(aggregate, (uint)only));
        Assert.Equal(0ul, HyprlandToplevelExportManager.ResolveHandle(aggregate, 1));
        Assert.Equal(0ul, HyprlandToplevelExportManager.ResolveHandle(aggregate, 7));
    }

    private sealed class SourceOf(TestToplevelModel model) : IToplevelSource
    {
        public int Enumerate(Span<ToplevelInfo> toplevels) => model.Enumerate(toplevels);

        public bool TryGet(ulong localId, out ToplevelInfo info) => model.TryGet(localId, out info);

        public bool Request(ulong localId, in ToplevelRequest request) => model.Request(localId, in request);

        public void AddObserver(IToplevelObserver observer) => model.AddObserver(observer);

        public void RemoveObserver(IToplevelObserver observer) => model.RemoveObserver(observer);
    }

    [Fact]
    public void An_unknown_handle_fails_the_frame()
    {
        using var host = new CompositorTestHost();
        var model = new TestToplevelModel();
        var capture = new ToplevelCapture(host);
        using var manager = new HyprlandToplevelExportManager(host.Display, host.Layout, host.Buffers, capture, model);

        var proxy = HyprlandTestSupport.Bind<Basin.Hypr.Protocol.HyprlandToplevelExportManagerV1>(host, "hyprland_toplevel_export_manager_v1", 2);
        var frame = proxy.CaptureToplevel(0, 12345);
        var failed = false;
        frame.Failed += (_, _) => failed = true;
        host.PumpUntil(() => failed);
        Assert.True(failed);
        HyprlandTestSupport.AssertAlive(host);
    }

    [Fact]
    public void The_client_content_is_captured_without_decorations_and_with_the_popup_clipped()
    {
        using var host = new CompositorTestHost();
        var model = new TestToplevelModel();
        var pack = new SceneCapturePack(host.Scene, host.Layout);
        pack.Capture.Renderer = host.Renderer;
        pack.Capture.Toplevels = model;
        using var manager = new HyprlandToplevelExportManager(host.Display, host.Layout, host.Buffers, pack.Capture, model);

        var window = MappedToplevel.Map(host, host.Client, 60, 50, 0xFF0000FF);
        var scene = host.SurfaceScenes.Single(s => ReferenceEquals(s.Surface, window.ServerSurface));
        var windowTree = new SceneTree(host.Scene.Root);
        windowTree.SetPosition(20, 10);
        _ = new SceneRect(windowTree, 100, 80, new RenderColor(1f, 0f, 0f, 1f));
        scene.Tree.Reparent(windowTree);
        scene.Tree.SetPosition(5, 5);
        var popups = new SceneTree(host.Scene.Root);
        var popup = new SceneRect(popups, 30, 30, new RenderColor(0f, 1f, 0f, 1f));
        popup.SetPosition(70, 40);

        var id = model.Add("a", "b", window.ServerSurface, new Box(20, 10, 100, 80));
        pack.Index.Set(id, new ToplevelCaptureTrees(windowTree, popups, scene.Tree));

        var proxy = HyprlandTestSupport.Bind<Basin.Hypr.Protocol.HyprlandToplevelExportManagerV1>(host, "hyprland_toplevel_export_manager_v1", 2);
        var frame = proxy.CaptureToplevel(0, (uint)id);
        (uint Width, uint Height)? announced = null;
        var done = false;
        var ready = false;
        frame.Buffer += (_, e) => announced = (e.Width, e.Height);
        frame.BufferDone += (_, _) => done = true;
        frame.Ready += (_, _) => ready = true;
        host.PumpUntil(() => done);
        Assert.Equal((60u, 50u), announced);

        var buffer = host.Client.CreateBuffer(60, 50, Fill.Solid(60, 50, 0));
        frame.Copy(buffer.Proxy, 1);
        host.PumpUntil(() => ready);

        Assert.Equal(0xFF0000FFu, HyprlandTestSupport.Pixel(buffer, 2, 2) | 0xFF000000u);
        Assert.Equal(0xFF0000FFu, HyprlandTestSupport.Pixel(buffer, 40, 20) | 0xFF000000u);
        Assert.Equal(0xFF00FF00u, HyprlandTestSupport.Pixel(buffer, 50, 40) | 0xFF000000u);
        Assert.Equal(0xFF00FF00u, HyprlandTestSupport.Pixel(buffer, 59, 49) | 0xFF000000u);
        HyprlandTestSupport.AssertAlive(host);
    }
}

public sealed class HyprlandCtmControlTests
{
    private sealed class RecordingCtm : ICtmControl
    {
        public List<(string Output, double[]? Matrix)> Applied { get; } = [];

        public bool SupportsCtm(IOutput output) => true;

        public bool SetCtm(IOutput output, ReadOnlySpan<double> rowMajor3x3)
        {
            Applied.Add((output.Name, rowMajor3x3.ToArray()));
            return true;
        }

        public bool ResetCtm(IOutput output)
        {
            Applied.Add((output.Name, null));
            return true;
        }
    }

    private static void SetMatrix(Basin.Hypr.Protocol.HyprlandCtmControlManagerV1 proxy, WlOutput output, double diagonal) =>
        proxy.SetCtmForOutput(
            output,
            WlFixed.FromDouble(diagonal), WlFixed.FromDouble(0), WlFixed.FromDouble(0),
            WlFixed.FromDouble(0), WlFixed.FromDouble(diagonal), WlFixed.FromDouble(0),
            WlFixed.FromDouble(0), WlFixed.FromDouble(0), WlFixed.FromDouble(diagonal));

    [Fact]
    public void Commit_applies_the_named_output_and_destroy_resets_it()
    {
        using var host = new CompositorTestHost();
        var ctm = new RecordingCtm();
        using var manager = new HyprlandCtmControlManager(host.Display, host.Layout, ctm);

        var proxy = HyprlandTestSupport.Bind<Basin.Hypr.Protocol.HyprlandCtmControlManagerV1>(host, "hyprland_ctm_control_manager_v1", 2);
        SetMatrix(proxy, host.Client.Outputs[0], 0.5);
        host.PumpToServer();
        Assert.Empty(ctm.Applied);

        proxy.Commit();
        host.PumpToServer();
        Assert.Single(ctm.Applied);
        Assert.Equal(0.5, ctm.Applied[0].Matrix![0]);

        proxy.Destroy();
        host.PumpToServer();
        Assert.Equal(2, ctm.Applied.Count);
        Assert.Null(ctm.Applied[1].Matrix);
        Assert.False(manager.HasOwner);
        HyprlandTestSupport.AssertAlive(host);
    }

    [Fact]
    public void An_output_the_client_never_named_is_reset_on_commit()
    {
        using var host = new CompositorTestHost();
        var ctm = new RecordingCtm();
        using var manager = new HyprlandCtmControlManager(host.Display, host.Layout, ctm);

        var proxy = HyprlandTestSupport.Bind<Basin.Hypr.Protocol.HyprlandCtmControlManagerV1>(host, "hyprland_ctm_control_manager_v1", 2);
        proxy.Commit();
        host.PumpToServer();
        Assert.Single(ctm.Applied);
        Assert.Null(ctm.Applied[0].Matrix);
        HyprlandTestSupport.AssertAlive(host);
    }

    [Fact]
    public void A_second_manager_is_blocked_and_ignored()
    {
        using var host = new CompositorTestHost();
        var ctm = new RecordingCtm();
        using var manager = new HyprlandCtmControlManager(host.Display, host.Layout, ctm);

        var first = HyprlandTestSupport.Bind<Basin.Hypr.Protocol.HyprlandCtmControlManagerV1>(host, "hyprland_ctm_control_manager_v1", 2);
        host.PumpToServer();
        var second = HyprlandTestSupport.Bind<Basin.Hypr.Protocol.HyprlandCtmControlManagerV1>(host, "hyprland_ctm_control_manager_v1", 2);
        var blocked = false;
        second.Blocked += (_, _) => blocked = true;
        host.PumpUntil(() => blocked);

        SetMatrix(second, host.Client.Outputs[0], 0.25);
        second.Commit();
        host.PumpToServer();
        Assert.Empty(ctm.Applied);

        SetMatrix(first, host.Client.Outputs[0], 0.75);
        first.Commit();
        host.PumpToServer();
        Assert.Single(ctm.Applied);
        HyprlandTestSupport.AssertAlive(host);
    }

    [Fact]
    public void A_negative_component_is_invalid_matrix()
    {
        using var host = new CompositorTestHost();
        using var manager = new HyprlandCtmControlManager(host.Display, host.Layout, new RecordingCtm());

        var proxy = HyprlandTestSupport.Bind<Basin.Hypr.Protocol.HyprlandCtmControlManagerV1>(host, "hyprland_ctm_control_manager_v1", 2);
        SetMatrix(proxy, host.Client.Outputs[0], -1.0);
        HyprlandTestSupport.AssertKilled(host);
    }
}

public sealed class HyprlandGlobalShortcutsTests
{
    [Fact]
    public void A_registered_shortcut_fires_when_the_compositor_triggers_it()
    {
        using var host = new CompositorTestHost();
        var registry = new DefaultGlobalShortcuts();
        using var manager = new HyprlandGlobalShortcutsManager(host.Display, registry);

        var proxy = HyprlandTestSupport.Bind<Basin.Hypr.Protocol.HyprlandGlobalShortcutsManagerV1>(host, "hyprland_global_shortcuts_manager_v1", 1);
        var shortcut = proxy.RegisterShortcut("toggle", "org.example.app", "Toggle", "Super+T");
        var pressed = 0;
        var released = 0;
        shortcut.Pressed += (_, _) => pressed++;
        shortcut.Released += (_, _) => released++;
        host.PumpToServer();
        Assert.Equal(1, registry.Count);
        Assert.True(manager.IsRegistered("org.example.app", "toggle"));

        Assert.True(manager.Trigger("org.example.app", "toggle", pressed: true));
        Assert.True(manager.Trigger("org.example.app", "toggle", pressed: false));
        Assert.False(manager.Trigger("org.example.app", "missing", pressed: true));
        host.PumpUntil(() => released == 1);
        Assert.Equal(1, pressed);

        shortcut.Destroy();
        host.PumpToServer();
        Assert.Equal(0, registry.Count);
        HyprlandTestSupport.AssertAlive(host);
    }

    [Fact]
    public void A_duplicate_across_clients_is_already_taken()
    {
        using var host = new CompositorTestHost();
        using var manager = new HyprlandGlobalShortcutsManager(host.Display, new DefaultGlobalShortcuts());
        var other = host.ConnectClient();

        var first = HyprlandTestSupport.Bind<Basin.Hypr.Protocol.HyprlandGlobalShortcutsManagerV1>(host, "hyprland_global_shortcuts_manager_v1", 1, other);
        _ = first.RegisterShortcut("toggle", "org.example.app", "Toggle", "Super+T");
        host.PumpToServer();
        HyprlandTestSupport.AssertAlive(host, other);

        var second = HyprlandTestSupport.Bind<Basin.Hypr.Protocol.HyprlandGlobalShortcutsManagerV1>(host, "hyprland_global_shortcuts_manager_v1", 1);
        _ = second.RegisterShortcut("toggle", "org.example.app", "Toggle", "Super+T");
        HyprlandTestSupport.AssertKilled(host);
    }
}
