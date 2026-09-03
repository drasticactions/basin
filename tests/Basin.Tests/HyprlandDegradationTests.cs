using Basin.Capabilities;
using Basin.Capabilities.Defaults;
using Basin.Desktop;
using Basin.Hypr;
using Wayland;
using Xunit;

namespace Basin.Tests;

public sealed class HyprlandDegradationTests
{
    [Fact]
    public void Toplevel_export_without_a_capture_backend_fails_the_frame()
    {
        using var host = new CompositorTestHost();
        var model = new TestToplevelModel();
        using var manager = new HyprlandToplevelExportManager(host.Display, host.Layout, host.Buffers, capture: null, model);
        var id = model.Add("a", "b", geometry: new Box(0, 0, 60, 50));

        var proxy = HyprlandTestSupport.Bind<Basin.Hypr.Protocol.HyprlandToplevelExportManagerV1>(host, "hyprland_toplevel_export_manager_v1", 2);
        var frame = proxy.CaptureToplevel(0, (uint)id);
        var failed = false;
        frame.Failed += (_, _) => failed = true;
        host.PumpUntil(() => failed);

        Assert.True(failed);
        HyprlandTestSupport.AssertAlive(host);
    }

    [Fact]
    public void Toplevel_mapping_without_a_model_fails_the_handle()
    {
        using var host = new CompositorTestHost();
        var model = new TestToplevelModel();
        using var list = new ForeignToplevelListManager(host.Display, model);
        using var mapping = new HyprlandToplevelMappingManager(host.Display, model: null);
        _ = model.Add("a", "b");

        Basin.Desktop.Protocol.ExtForeignToplevelHandleV1? handle = null;
        var listProxy = HyprlandTestSupport.Bind<Basin.Desktop.Protocol.ExtForeignToplevelListV1>(host, "ext_foreign_toplevel_list_v1", 1);
        listProxy.Toplevel += (_, e) => handle = e.Toplevel;
        host.PumpUntil(() => handle is not null);

        var proxy = HyprlandTestSupport.Bind<Basin.Hypr.Protocol.HyprlandToplevelMappingManagerV1>(host, "hyprland_toplevel_mapping_manager_v1", 1);
        var failed = false;
        var request = proxy.GetWindowForToplevel(handle!);
        request.Failed += (_, _) => failed = true;
        host.PumpUntil(() => failed);

        Assert.True(failed);
        HyprlandTestSupport.AssertAlive(host);
    }

    [Fact]
    public void Global_shortcuts_with_the_default_registry_register_and_never_fire()
    {
        using var host = new CompositorTestHost();
        using var manager = new HyprlandGlobalShortcutsManager(host.Display, new DefaultGlobalShortcuts());

        var proxy = HyprlandTestSupport.Bind<Basin.Hypr.Protocol.HyprlandGlobalShortcutsManagerV1>(host, "hyprland_global_shortcuts_manager_v1", 1);
        var shortcut = proxy.RegisterShortcut("toggle", "org.example.app", "Toggle", "Super+T");
        var pressed = 0;
        shortcut.Pressed += (_, _) => pressed++;
        host.PumpToServer();
        host.PumpToClient();

        Assert.True(manager.IsRegistered("org.example.app", "toggle"));
        Assert.Equal(0, pressed);
        HyprlandTestSupport.AssertAlive(host);
    }

    [Fact]
    public void Lock_notify_without_a_locker_never_notifies()
    {
        using var host = new CompositorTestHost();
        using var notifier = new HyprlandLockNotifier(host.Display, NeverLocked.Instance);

        var proxy = HyprlandTestSupport.Bind<Basin.Hypr.Protocol.HyprlandLockNotifierV1>(host, "hyprland_lock_notifier_v1", 1);
        var notification = proxy.GetLockNotification();
        var events = 0;
        notification.Locked += (_, _) => events++;
        notification.Unlocked += (_, _) => events++;
        host.PumpToServer();
        host.PumpToClient();

        Assert.Equal(0, events);
        HyprlandTestSupport.AssertAlive(host);
    }

    [Fact]
    public void Surface_properties_without_a_scene_are_accepted_and_kept()
    {
        using var host = new CompositorTestHost();
        var appearance = new DefaultSurfaceAppearance();
        using var manager = new HyprlandSurfaceManager(host.Display, host.Compositor, appearance);
        var surface = host.Client.Compositor.CreateSurface();

        var proxy = HyprlandTestSupport.Bind<Basin.Hypr.Protocol.HyprlandSurfaceManagerV1>(host, "hyprland_surface_manager_v1", 2);
        var hypr = proxy.GetHyprlandSurface(surface);
        hypr.SetOpacity(WlFixed.FromDouble(0.25));
        surface.Commit();
        host.PumpToServer();

        var server = host.Compositor.Surfaces.Single();
        Assert.Equal(0.25, appearance.OpacityOf(server), 3);
        HyprlandTestSupport.AssertAlive(host);
    }

    [Fact]
    public void Focus_grab_refuses_to_freeze_without_a_seat()
    {
        using var host = new CompositorTestHost();
        using var services = new BasinServices(host.Display, host.Loop)
            .Use(host.Layout)
            .Use(host.Compositor)
            .Use(host.Buffers)
            .Install(new HyprlandFocusGrabModule());

        var error = Assert.Throws<InvalidOperationException>(() => services.Freeze());

        Assert.Contains("hyprland_focus_grab_manager_v1", error.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(Basin.Seat.Seat), error.Message, StringComparison.Ordinal);
        Assert.Contains("Without(\"hyprland_focus_grab_manager_v1\")", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Ctm_control_refuses_to_freeze_without_a_driver()
    {
        using var host = new CompositorTestHost();
        using var services = new BasinServices(host.Display, host.Loop)
            .Use(host.Layout)
            .Use(host.Compositor)
            .Use(host.Buffers)
            .Use(host.Seat)
            .Install(new HyprlandCtmModule());

        var error = Assert.Throws<InvalidOperationException>(() => services.Freeze());

        Assert.Contains("hyprland_ctm_control_manager_v1", error.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(ICtmControl), error.Message, StringComparison.Ordinal);
        Assert.Contains("Without(\"hyprland_ctm_control_manager_v1\")", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_pack_installs_every_global_once_the_driver_is_registered()
    {
        using var host = new CompositorTestHost();
        using var services = new BasinServices(host.Display, host.Loop)
            .Use(host.Layout)
            .Use(host.Compositor)
            .Use(host.Buffers)
            .Use(host.Seat)
            .Use<ICtmControl>(new NullCtm())
            .Install(HyprPack.Default)
            .Freeze();

        foreach (var module in HyprPack.Default)
        {
            Assert.True(services.Modules.ContainsKey(module.WireInterface), module.WireInterface);
        }

        Assert.NotNull(services.Find<ISurfaceAppearance>());
        Assert.NotNull(services.Find<IGlobalShortcuts>());
        Assert.NotNull(services.Find<ILockState>());
        Assert.Contains(typeof(IScreenCapture), services.UnresolvedCapabilities);
    }

    [Fact]
    public void Subtracting_the_ctm_global_removes_the_obligation()
    {
        using var host = new CompositorTestHost();
        using var services = new BasinServices(host.Display, host.Loop)
            .Use(host.Layout)
            .Use(host.Compositor)
            .Use(host.Buffers)
            .Use(host.Seat)
            .Without("hyprland_ctm_control_manager_v1")
            .Install(HyprPack.Default)
            .Freeze();

        Assert.False(services.Modules.ContainsKey("hyprland_ctm_control_manager_v1"));
        Assert.True(services.Modules.ContainsKey("hyprland_surface_manager_v1"));
    }

    [Fact]
    public void The_privileged_set_hides_the_three_privileged_globals()
    {
        Assert.True(PrivilegedProtocols.Contains("hyprland_ctm_control_manager_v1"));
        Assert.True(PrivilegedProtocols.Contains("hyprland_toplevel_export_manager_v1"));
        Assert.True(PrivilegedProtocols.Contains("hyprland_input_capture_manager_v1"));
        Assert.False(PrivilegedProtocols.Contains("hyprland_global_shortcuts_manager_v1"));
    }

    private sealed class NullCtm : ICtmControl
    {
        public bool SupportsCtm(IOutput output) => false;

        public bool SetCtm(IOutput output, ReadOnlySpan<double> rowMajor3x3) => false;

        public bool ResetCtm(IOutput output) => false;
    }
}
