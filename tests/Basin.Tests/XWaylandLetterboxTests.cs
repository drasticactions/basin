using Basin.Scene;
using Basin.XWayland;
using Xunit;

namespace Basin.Tests;

public sealed class XWaylandLetterboxTests
{
    [Fact]
    public void A_fullscreen_X11_window_is_fitted_until_it_commits_the_configured_size()
    {
        Assert.SkipUnless(PrivateBus.OnPath("Xwayland") && PrivateBus.OnPath("xeyes"), "Xwayland and xeyes are needed");
        using var host = new CompositorTestHost(320, 240);
        using var shell = new XwaylandShellGlobal(host.Display, host.Compositor);
        using var server = new XWaylandServer(host.Display, host.Loop);
        using var driver = new XWaylandSceneDriver { Letterbox = true };
        driver.ManagedParent = _ => new SceneTree(host.Scene.Root);
        XWaylandWm? wm = null;
        XWaylandWindow? window = null;
        server.Ready += fd =>
        {
            wm = new XWaylandWm(fd, host.Loop, shell, host.Seat);
            wm.WindowMapped += mapped => window ??= mapped;
            driver.Attach(wm);
        };

        using var eyes = XWaylandTestSupport.Start("xeyes", server.DisplayName, "-geometry", "100x80", "-bw", "0");
        try
        {
            XWaylandTestSupport.Pump(host, () => window?.Surface?.Current.Width == 100);
            var fit = driver.FitOf(window!);
            Assert.NotNull(fit);
            Assert.False(fit.IsActive);
            Assert.Same(fit.Root, driver.OuterOf(window!));
            Assert.NotNull(driver.ParentOf(window!));

            window!.SetFullscreen(true);
            window.Configure(0, 0, 320, 240);
            driver.Refit(window);
            Assert.True(fit.IsActive);
            Assert.Equal(new Box(0, 0, 320, 240), fit.Target);
            Assert.Equal(SceneFitMode.Fit, SceneFit.Needed((320, 240), (100, 80), SceneFitState.Fullscreen, window.Letterbox));

            XWaylandTestSupport.Pump(host, () => window.Surface!.Current.Width == 320);
            Assert.False(fit.IsActive);

            window.Letterbox = SceneFitPolicy.Off;
            window.Configure(0, 0, 300, 200);
            driver.Refit(window);
            Assert.False(fit.IsActive);
        }
        finally
        {
            eyes.Kill();
            eyes.WaitForExit(2000);
            driver.Dispose();
            wm?.Dispose();
        }
    }

    [Fact]
    public void A_compositor_that_places_managed_windows_keeps_the_surface_at_its_parents_origin()
    {
        Assert.SkipUnless(PrivateBus.OnPath("Xwayland") && PrivateBus.OnPath("xeyes"), "Xwayland and xeyes are needed");
        using var host = new CompositorTestHost(320, 240);
        using var shell = new XwaylandShellGlobal(host.Display, host.Compositor);
        using var server = new XWaylandServer(host.Display, host.Loop);
        using var driver = new XWaylandSceneDriver { PositionsManagedWindows = false };
        driver.ManagedParent = _ => new SceneTree(host.Scene.Root);
        XWaylandWm? wm = null;
        XWaylandWindow? window = null;
        server.Ready += fd =>
        {
            wm = new XWaylandWm(fd, host.Loop, shell, host.Seat);
            wm.WindowMapped += mapped => window ??= mapped;
            driver.Attach(wm);
        };

        using var eyes = XWaylandTestSupport.Start("xeyes", server.DisplayName, "-geometry", "100x80+40+30");
        try
        {
            XWaylandTestSupport.Pump(host, () => window is not null && driver.OuterOf(window) is not null);
            var outer = driver.OuterOf(window!)!;
            Assert.Equal((0, 0), (outer.X, outer.Y));
            window!.Configure(70, 50, 100, 80);
            XWaylandTestSupport.Pump(host, () => window.X11Bounds.X == 70);
            Assert.Equal((0, 0), (outer.X, outer.Y));
        }
        finally
        {
            eyes.Kill();
            eyes.WaitForExit(2000);
            driver.Dispose();
            wm?.Dispose();
        }
    }

    [Fact]
    public void A_framed_X11_window_is_centered_by_its_outer_box_and_told_its_frame_extents()
    {
        Assert.SkipUnless(PrivateBus.OnPath("Xwayland") && PrivateBus.OnPath("xeyes") && PrivateBus.OnPath("xprop"), "Xwayland, xeyes and xprop are needed");
        using var host = new CompositorTestHost(320, 240);
        using var shell = new XwaylandShellGlobal(host.Display, host.Compositor);
        using var server = new XWaylandServer(host.Display, host.Loop);
        using var driver = new XWaylandSceneDriver
        {
            CenterArea = () => new Box(0, 0, 320, 240),
            FrameInsets = _ => new Basin.Capabilities.FrameInsets(30, 4, 4, 4),
        };
        driver.ManagedParent = _ => new SceneTree(host.Scene.Root);
        XWaylandWm? wm = null;
        XWaylandWindow? window = null;
        server.Ready += fd =>
        {
            wm = new XWaylandWm(fd, host.Loop, shell, host.Seat);
            wm.WindowMapped += mapped => window ??= mapped;
            driver.Attach(wm);
        };

        using var eyes = XWaylandTestSupport.Start("xeyes", server.DisplayName, "-geometry", "100x80", "-bw", "0");
        try
        {
            XWaylandTestSupport.Pump(host, () => window is not null && driver.OuterOf(window) is not null && window.X11Bounds.X != 0);
            Assert.Equal(new Basin.Capabilities.FrameInsets(30, 4, 4, 4), window!.FrameExtents);
            Assert.Equal((110, 93), (window.X11Bounds.X, window.X11Bounds.Y));

            using var xprop = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                "xprop", ["-display", server.DisplayName, "-id", window.WindowId.ToString(System.Globalization.CultureInfo.InvariantCulture), "_NET_FRAME_EXTENTS"])
            {
                RedirectStandardOutput = true,
            })!;
            XWaylandTestSupport.Pump(host, () => xprop.HasExited);
            var output = xprop.StandardOutput.ReadToEnd();
            xprop.StandardOutput.Dispose();
            Assert.Contains("_NET_FRAME_EXTENTS(CARDINAL) = 4, 4, 30, 4", output);
        }
        finally
        {
            eyes.Kill();
            eyes.WaitForExit(2000);
            driver.Dispose();
            wm?.Dispose();
        }
    }
}
