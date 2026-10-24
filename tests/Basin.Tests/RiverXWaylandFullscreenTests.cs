using System.Diagnostics;
using Basin.XWayland;
using Xunit;
using Wm = Basin.WindowManager;

namespace Basin.Tests;

public sealed class RiverXWaylandFullscreenTests
{
    [Fact]
    public void An_x_window_under_a_manager_goes_fullscreen_and_back_with_no_intermediate_size()
    {
        Assert.SkipUnless(PrivateBus.OnPath("Xwayland") && PrivateBus.OnPath("xeyes"), "Xwayland and xeyes are needed");
        using var fixture = new RiverFixture(640, 480);
        using var shell = new XwaylandShellGlobal(fixture.Host.Display, fixture.Host.Compositor);
        using var server = new XWaylandServer(fixture.Host.Display, fixture.Host.Loop);
        XWaylandWm? wm = null;
        XWaylandWindow? xwindow = null;
        server.Ready += fd =>
        {
            wm = new XWaylandWm(fd, fixture.Host.Loop, shell, fixture.Host.Seat);
            fixture.Server.Adopt(wm);
            wm.WindowMapped += mapped => xwindow ??= mapped;
        };

        using var eyes = XWaylandTestSupport.Start("xeyes", server.DisplayName, "-geometry", "150x100+0+0");
        X11ConfigureProbe? probe = null;
        try
        {
            var wantFullscreen = (bool?)null;
            fixture.OnManage = context =>
            {
                foreach (var window in context.Windows)
                {
                    if (wantFullscreen is true)
                    {
                        window.Fullscreen(context.Outputs[0]);
                        window.InformFullscreen(true);
                    }
                    else if (wantFullscreen is false)
                    {
                        window.ExitFullscreen();
                        window.InformFullscreen(false);
                    }

                    window.ProposeDimensions(200, 150);
                }

                wantFullscreen = null;
            };
            Pump(fixture, () => fixture.Windows.Count == 1 && xwindow is { Width: 200 });
            var managed = fixture.Windows[0];
            managed.FullscreenRequested += _ => wantFullscreen = true;
            managed.ExitFullscreenRequested += () => wantFullscreen = false;
            probe = new X11ConfigureProbe(server.DisplayName, xwindow!.WindowId);

            probe.RequestFullscreen(true);
            Pump(fixture, () => xwindow.IsFullscreen && xwindow.Width == 640 && probe.StateSaysFullscreen);
            probe.Drain();
            Assert.All(probe.Sizes, size => Assert.Contains(size, new[] { (200, 150), (640, 480) }));
            probe.Sizes.Clear();

            probe.RequestFullscreen(false);
            Pump(fixture, () => !xwindow.IsFullscreen && xwindow.Width == 200 && !probe.StateSaysFullscreen);
            fixture.Settle();
            probe.Drain();

            Assert.NotEmpty(probe.Sizes);
            Assert.All(probe.Sizes, size => Assert.Equal((200, 150), size));
        }
        finally
        {
            probe?.Dispose();
            fixture.OnManage = null;
            eyes.Kill();
            eyes.WaitForExit(2000);
            wm?.Dispose();
        }
    }

    [Fact]
    public void A_session_gets_the_fullscreen_request_of_a_new_x_window_in_the_sequence_that_adopts_it()
    {
        Assert.SkipUnless(PrivateBus.OnPath("Xwayland") && PrivateBus.OnPath("xprop"), "Xwayland and xprop are needed");
        using var fixture = new RiverFixture(640, 480);
        using var shell = new XwaylandShellGlobal(fixture.Host.Display, fixture.Host.Compositor);
        using var server = new XWaylandServer(fixture.Host.Display, fixture.Host.Loop);
        XWaylandWm? wm = null;
        server.Ready += fd =>
        {
            wm = new XWaylandWm(fd, fixture.Host.Loop, shell, fixture.Host.Seat);
            fixture.Server.Adopt(wm);
        };

        var session = new Wm.WmSession<Managed>(new Wm.WmFocusStack<Managed>(fixture.Client), window => new Managed(window));
        var adopting = false;
        var requested = false;
        var requestedWhileAdopting = false;
        session.Adopted += managed => managed.Window.FullscreenRequested += _ =>
        {
            requested = true;
            requestedWhileAdopting = adopting;
        };
        fixture.OnManage = context =>
        {
            adopting = true;
            session.AdoptNewWindows(context);
            adopting = false;
        };

        X11ConfigureProbe? owner = null;
        try
        {
            using (var start = XWaylandTestSupport.Start("xprop", server.DisplayName, "-root", "_NET_SUPPORTED"))
            {
                Pump(fixture, () => wm is not null && start.HasExited);
            }

            X11ConfigureProbe.MapFullscreenWindow(server.DisplayName, out owner);
            Pump(fixture, () => requested);

            Assert.True(requestedWhileAdopting);

            owner!.Dispose();
            owner = null;
            Pump(fixture, () => fixture.Windows.Count == 0);
            fixture.Settle();
        }
        finally
        {
            owner?.Dispose();
            fixture.OnManage = null;
            wm?.Dispose();
        }
    }

    private sealed class Managed(Wm.WmWindow window) : Wm.IWmManagedWindow
    {
        public Wm.WmWindow Window { get; } = window;
    }

    private static void Pump(RiverFixture fixture, Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (!condition() && clock.Elapsed < TimeSpan.FromSeconds(15))
        {
            fixture.Host.Loop.Dispatch(10);
            fixture.Settle(1);
        }

        Assert.True(condition(), "condition not reached within 15 s");
    }
}
