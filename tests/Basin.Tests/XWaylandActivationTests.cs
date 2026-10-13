using System.Diagnostics;
using Basin.Capabilities;
using Basin.XWayland;
using Xunit;

namespace Basin.Tests;

public sealed class XWaylandActivationTests
{
    private static string RootClientList(CompositorTestHost host, string display)
    {
        var file = Path.Combine(Path.GetTempPath(), $"basin-xclients-{Guid.NewGuid():N}.txt");
        try
        {
            var info = new ProcessStartInfo("/bin/sh", ["-c", $"xprop -root _NET_CLIENT_LIST > '{file}'"]) { UseShellExecute = false };
            info.Environment["DISPLAY"] = display;
            using var process = Process.Start(info)!;
            XWaylandTestSupport.Pump(host, () => process.HasExited);
            return File.ReadAllText(file);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void The_root_client_list_names_each_managed_window_until_it_goes()
    {
        Assert.SkipUnless(PrivateBus.OnPath("Xwayland") && PrivateBus.OnPath("xeyes") && PrivateBus.OnPath("xprop"), "Xwayland, xeyes and xprop are needed");
        using var host = new CompositorTestHost(320, 240);
        using var shell = new XwaylandShellGlobal(host.Display, host.Compositor);
        using var server = new XWaylandServer(host.Display, host.Loop);
        XWaylandWm? wm = null;
        var mapped = new List<XWaylandWindow>();
        var destroyed = 0;
        server.Ready += fd =>
        {
            wm = new XWaylandWm(fd, host.Loop, shell, host.Seat);
            wm.WindowMapped += window =>
            {
                mapped.Add(window);
                window.Destroyed += () => destroyed++;
            };
        };

        using var eyes = XWaylandTestSupport.Start("xeyes", server.DisplayName, "-geometry", "100x80+10+10");
        try
        {
            XWaylandTestSupport.Pump(host, () => mapped.Count > 0);
            Assert.Contains($"0x{mapped[0].WindowId:x}", RootClientList(host, server.DisplayName), StringComparison.Ordinal);

            eyes.Kill();
            eyes.WaitForExit(2000);
            XWaylandTestSupport.Pump(host, () => destroyed > 0);
            Assert.DoesNotContain($"0x{mapped[0].WindowId:x}", RootClientList(host, server.DisplayName), StringComparison.Ordinal);
        }
        finally
        {
            if (!eyes.HasExited)
            {
                eyes.Kill();
            }

            wm?.Dispose();
        }
    }

    [Fact]
    public void An_activate_request_reaches_the_compositor_and_the_model_reports_focus_and_window_state()
    {
        Assert.SkipUnless(PrivateBus.OnPath("Xwayland") && PrivateBus.OnPath("xeyes"), "Xwayland and xeyes are needed");
        using var host = new CompositorTestHost(320, 240);
        using var shell = new XwaylandShellGlobal(host.Display, host.Compositor);
        using var server = new XWaylandServer(host.Display, host.Loop);
        XWaylandWm? wm = null;
        XWaylandToplevelSource? source = null;
        var mapped = new List<XWaylandWindow>();
        var activated = new List<XWaylandWindow>();
        server.Ready += fd =>
        {
            wm = new XWaylandWm(fd, host.Loop, shell, host.Seat);
            source = new XWaylandToplevelSource(wm);
            wm.WindowMapped += mapped.Add;
            wm.ActivationRequested += activated.Add;
        };

        using var eyes = XWaylandTestSupport.Start("xeyes", server.DisplayName, "-geometry", "100x80+10+10");
        try
        {
            XWaylandTestSupport.Pump(host, () => mapped.Count > 0);
            var id = source!.IdFor(mapped[0]);
            Assert.NotEqual(0ul, id);

            Assert.True(source.Request(id, new ToplevelRequest(ToplevelRequestKind.Activate)));
            Assert.Equal([mapped[0]], activated);

            ToplevelState State() => source.TryGet(id, out var info) ? info.State : ToplevelState.None;
            Assert.False(State().HasFlag(ToplevelState.Activated));
            host.Seat.Keyboard.NotifyEnter(mapped[0].Surface);
            Assert.True(State().HasFlag(ToplevelState.Activated));
            host.Seat.Keyboard.NotifyClearFocus();
            Assert.False(State().HasFlag(ToplevelState.Activated));

            mapped[0].SetMaximized(true);
            Assert.True(State().HasFlag(ToplevelState.Maximized));
            mapped[0].SetFullscreen(true);
            Assert.True(State().HasFlag(ToplevelState.Fullscreen));
            mapped[0].SetFullscreen(false);
            Assert.False(State().HasFlag(ToplevelState.Fullscreen));
        }
        finally
        {
            eyes.Kill();
            eyes.WaitForExit(2000);
            source?.Dispose();
            wm?.Dispose();
        }
    }
}
