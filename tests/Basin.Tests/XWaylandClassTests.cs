using System.Diagnostics;
using Basin.XWayland;
using Xunit;

namespace Basin.Tests;

public sealed class XWaylandClassTests
{
    internal static readonly string X11Focus = Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "scripts", "wlclients", "bin", "x11focus");

    internal static void SkipWithoutX11Focus(params string[] programs)
    {
        Assert.SkipUnless(PrivateBus.OnPath("Xwayland") && File.Exists(X11Focus), "Xwayland and scripts/wlclients/bin/x11focus are needed");
        foreach (var program in programs)
        {
            Assert.SkipUnless(PrivateBus.OnPath(program), $"{program} is needed");
        }
    }

    [Fact]
    public void A_window_created_override_redirect_and_cleared_before_the_map_maps_managed()
    {
        SkipWithoutX11Focus();
        using var host = new CompositorTestHost();
        using var shell = new XwaylandShellGlobal(host.Display, host.Compositor);
        using var server = new XWaylandServer(host.Display, host.Loop);
        XWaylandWm? wm = null;
        XWaylandWindow? managed = null;
        XWaylandWindow? unmanaged = null;
        server.Ready += fd =>
        {
            wm = new XWaylandWm(fd, host.Loop, shell, host.Seat);
            wm.WindowMapped += mapped => managed ??= mapped;
            wm.OverrideRedirectMapped += mapped => unmanaged ??= mapped;
        };

        using var client = XWaylandTestSupport.Start(X11Focus, server.DisplayName, "orflip", "20");
        try
        {
            XWaylandTestSupport.Pump(host, () => managed is not null);
            Assert.False(managed!.OverrideRedirect);
            Assert.Equal("x11focus", managed.Class);
            Assert.Null(unmanaged);
        }
        finally
        {
            client.Kill();
            client.WaitForExit(2000);
            wm?.Dispose();
        }
    }

    [Fact]
    public void A_state_write_keeps_the_atoms_the_client_set()
    {
        SkipWithoutX11Focus("xprop");
        using var host = new CompositorTestHost();
        using var shell = new XwaylandShellGlobal(host.Display, host.Compositor);
        using var server = new XWaylandServer(host.Display, host.Loop);
        XWaylandWm? wm = null;
        XWaylandWindow? window = null;
        server.Ready += fd =>
        {
            wm = new XWaylandWm(fd, host.Loop, shell, host.Seat);
            wm.WindowMapped += mapped => window ??= mapped;
        };

        using var client = XWaylandTestSupport.Start(X11Focus, server.DisplayName, "keepstate", "20");
        try
        {
            XWaylandTestSupport.Pump(host, () => window is not null);
            window!.SetFullscreen(true);
            var state = ReadState(host, server.DisplayName, window.WindowId);
            Assert.Contains("_NET_WM_STATE_ABOVE", state);
            Assert.Contains("_NET_WM_STATE_STICKY", state);
            Assert.Contains("_NET_WM_STATE_FULLSCREEN", state);

            window.SetFullscreen(false);
            state = ReadState(host, server.DisplayName, window.WindowId);
            Assert.Contains("_NET_WM_STATE_ABOVE", state);
            Assert.DoesNotContain("_NET_WM_STATE_FULLSCREEN", state);
        }
        finally
        {
            client.Kill();
            client.WaitForExit(2000);
            wm?.Dispose();
        }
    }

    internal static string ReadState(CompositorTestHost host, string display, uint window)
    {
        var info = new ProcessStartInfo("xprop", ["-id", $"0x{window:x}", "_NET_WM_STATE"])
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
        };
        info.Environment["DISPLAY"] = display;
        info.Environment.Remove("WAYLAND_DISPLAY");
        using var process = Process.Start(info)!;
        XWaylandTestSupport.Pump(host, () => process.HasExited);
        using var reader = process.StandardOutput;
        return reader.ReadToEnd();
    }
}
