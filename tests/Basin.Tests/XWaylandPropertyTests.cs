using Basin.XWayland;
using Xunit;

namespace Basin.Tests;

public sealed class XWaylandPropertyTests
{
    [Fact]
    public void A_title_set_after_map_reaches_the_window()
    {
        Assert.SkipUnless(PrivateBus.OnPath("Xwayland") && PrivateBus.OnPath("xeyes") && PrivateBus.OnPath("xprop"),
            "Xwayland, xeyes and xprop are needed");
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

        using var eyes = XWaylandTestSupport.Start("xeyes", server.DisplayName);
        try
        {
            XWaylandTestSupport.Pump(host, () => window is not null);
            XWaylandTestSupport.Run(host, server.DisplayName, "-id", $"0x{window!.WindowId:x}", "-f", "_NET_WM_NAME", "8u", "-set", "_NET_WM_NAME", "renamed");
            XWaylandTestSupport.Pump(host, () => window.Title == "renamed");
        }
        finally
        {
            eyes.Kill();
            eyes.WaitForExit(2000);
            wm?.Dispose();
        }
    }

    [Fact]
    public void The_pid_comes_from_the_x_server_and_STEAM_GAME_and_the_window_type_reach_the_window()
    {
        Assert.SkipUnless(PrivateBus.OnPath("Xwayland") && PrivateBus.OnPath("xeyes") && PrivateBus.OnPath("xprop"),
            "Xwayland, xeyes and xprop are needed");
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

        using var eyes = XWaylandTestSupport.Start("xeyes", server.DisplayName);
        try
        {
            XWaylandTestSupport.Pump(host, () => window is not null);
            Assert.Equal(eyes.Id, window!.Pid);
            Assert.True(window.PidVerified);
            Assert.Equal("xeyes", window.Process.Name);
            Assert.Equal("normal", window.WindowType);
            Assert.Null(window.SteamGame);

            var changes = 0;
            window.PropertiesChanged += () => changes++;
            XWaylandTestSupport.Run(host, server.DisplayName, "-id", $"0x{window.WindowId:x}", "-f", "STEAM_GAME", "32c", "-set", "STEAM_GAME", "570");
            XWaylandTestSupport.Pump(host, () => window.SteamGame == 570);
            Assert.True(changes > 0);

            XWaylandTestSupport.Run(host, server.DisplayName, "-id", $"0x{window.WindowId:x}", "-f", "_NET_WM_WINDOW_TYPE", "32a", "-set", "_NET_WM_WINDOW_TYPE", "_NET_WM_WINDOW_TYPE_DIALOG");
            XWaylandTestSupport.Pump(host, () => window.WindowType == "dialog");

            Assert.Equal(string.Empty, window.ThemeVariant);
            XWaylandTestSupport.Run(host, server.DisplayName, "-id", $"0x{window.WindowId:x}", "-f", "_GTK_THEME_VARIANT", "8u", "-set", "_GTK_THEME_VARIANT", "dark");
            XWaylandTestSupport.Pump(host, () => window.ThemeVariant == "dark");
        }
        finally
        {
            eyes.Kill();
            eyes.WaitForExit(2000);
            wm?.Dispose();
        }
    }
}
