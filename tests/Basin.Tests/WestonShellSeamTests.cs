using Basin.Shell.Weston;
using Wayland;
using Xunit;

namespace Basin.Tests;

public sealed class WestonShellSeamTests
{
    [Fact]
    public void The_shell_protocol_without_roles_refuses_to_freeze()
    {
        using var host = new CompositorTestHost();
        using var services = Registry(host).Install(new WestonDesktopShellModule());

        var error = Assert.Throws<InvalidOperationException>(() => services.Freeze());

        Assert.Contains("weston_desktop_shell", error.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(IShellRoles), error.Message, StringComparison.Ordinal);
        Assert.Contains("Without(\"weston_desktop_shell\")", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_screensaver_protocol_without_roles_refuses_to_freeze()
    {
        using var host = new CompositorTestHost();
        using var services = Registry(host).Install(new WestonScreensaverModule());

        var error = Assert.Throws<InvalidOperationException>(() => services.Freeze());

        Assert.Contains("weston_screensaver", error.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(IShellRoles), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Roles_registered_satisfy_the_obligation()
    {
        using var host = new CompositorTestHost();
        using var services = Registry(host)
            .Use<IShellRoles>(new RecordingRoles())
            .Install(WestonShellPack.Create())
            .Freeze();

        Assert.True(services.Modules.ContainsKey("weston_desktop_shell"));
        Assert.True(services.Modules.ContainsKey("weston_screensaver"));
    }

    [Fact]
    public void An_unprivileged_client_is_refused_the_shell()
    {
        using var host = new CompositorTestHost();
        var roles = new RecordingRoles();
        using var services = Registry(host)
            .Use<IShellRoles>(roles)
            .Install(new WestonDesktopShellModule(_ => false))
            .Freeze();

        var shell = Bind<Basin.Shell.Weston.Protocol.WestonDesktopShell>(host, "weston_desktop_shell", 1);
        shell.DesktopReady();
        host.PumpToServer();

        Assert.Equal(string.Empty, roles.Log);
    }

    [Fact]
    public void The_spawned_client_reaches_every_role()
    {
        using var host = new CompositorTestHost();
        var roles = new RecordingRoles();
        using var services = Registry(host)
            .Use<IShellRoles>(roles)
            .Install(WestonShellPack.Create(_ => true))
            .Freeze();

        var shell = Bind<Basin.Shell.Weston.Protocol.WestonDesktopShell>(host, "weston_desktop_shell", 1);
        var screensaver = Bind<Basin.Shell.Weston.Protocol.WestonScreensaver>(host, "weston_screensaver", 1);

        var background = host.Client.Compositor.CreateSurface();
        var panel = host.Client.Compositor.CreateSurface();
        var lockSurface = host.Client.Compositor.CreateSurface();
        var grab = host.Client.Compositor.CreateSurface();
        var saver = host.Client.Compositor.CreateSurface();
        host.PumpToServer();

        var output = host.Client.Outputs[0];
        shell.SetBackground(output, background);
        shell.SetPanel(output, panel);
        shell.SetPanelPosition((uint)ShellPanelPosition.Bottom);
        shell.SetLockSurface(lockSurface);
        shell.SetGrabSurface(grab);
        shell.DesktopReady();
        shell.Unlock();
        screensaver.SetSurface(saver, output);
        host.PumpToServer();

        Assert.Equal(
            "background|panel|position:Bottom|lock|grab|ready|unlock|screensaver",
            roles.Log);
    }

    [Fact]
    public void An_in_process_shell_reaches_the_same_seam()
    {
        var roles = new RecordingRoles();

        IShellRoles seam = roles;
        seam.SetPanelPosition(ShellPanelPosition.Left);
        seam.DesktopReady();
        seam.Unlock();

        Assert.Equal("position:Left|ready|unlock", roles.Log);
    }

    [Fact]
    public void The_compositor_reaches_the_shell_client()
    {
        using var host = new CompositorTestHost();
        var roles = new RecordingRoles();
        using var services = Registry(host)
            .Use<IShellRoles>(roles)
            .Install(new WestonDesktopShellModule(_ => true))
            .Freeze();

        var shell = Bind<Basin.Shell.Weston.Protocol.WestonDesktopShell>(host, "weston_desktop_shell", 1);
        var prepared = 0;
        uint cursor = 99;
        shell.PrepareLockSurface += (_, _) => prepared++;
        shell.GrabCursor += (_, e) => cursor = e.Cursor;
        host.PumpToServer();

        var client = services.Require<IShellClient>();
        client.PrepareLockSurface();
        client.GrabCursor(ShellGrabCursor.Move);
        host.PumpToClient();

        Assert.Equal(1, prepared);
        Assert.Equal((uint)ShellGrabCursor.Move, cursor);
    }

    private static BasinServices Registry(CompositorTestHost host) =>
        new BasinServices(host.Display, host.Loop)
            .Use(host.Layout)
            .Use(host.Compositor)
            .Use(host.Buffers)
            .Use(host.Seat);

    private static T Bind<T>(CompositorTestHost host, string wireInterface, uint version)
        where T : WlProxy, IWaylandObject<T>
    {
        T? proxy = null;
        var registry = host.Client.Display.GetRegistry();
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

    private sealed class RecordingRoles : IShellRoles
    {
        private readonly System.Text.StringBuilder _log = new();

        public string Log => _log.ToString();

        public void SetBackground(IOutput output, Surface surface) => Append("background");

        public void SetPanel(IOutput output, Surface surface) => Append("panel");

        public void SetPanelPosition(ShellPanelPosition position) => Append($"position:{position}");

        public void SetLockSurface(Surface surface) => Append("lock");

        public void Unlock() => Append("unlock");

        public void SetGrabSurface(Surface surface) => Append("grab");

        public void DesktopReady() => Append("ready");

        public void SetScreensaverSurface(IOutput output, Surface surface) => Append("screensaver");

        private void Append(string entry)
        {
            if (_log.Length > 0)
            {
                _log.Append('|');
            }

            _log.Append(entry);
        }
    }
}
