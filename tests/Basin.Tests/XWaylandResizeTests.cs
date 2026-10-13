using System.Runtime.InteropServices;
using Basin.XWayland;
using Xcb.Native;
using Xunit;

namespace Basin.Tests;

public sealed class XWaylandResizeTests
{
    [Fact]
    public void A_configure_chained_from_a_completed_transaction_completes_on_the_matching_commit()
    {
        Assert.SkipUnless(PrivateBus.OnPath("Xwayland") && PrivateBus.OnPath("xeyes"), "Xwayland and xeyes are needed");
        using var host = new CompositorTestHost(640, 480);
        using var shell = new XwaylandShellGlobal(host.Display, host.Compositor);
        using var server = new XWaylandServer(host.Display, host.Loop);
        XWaylandWm? wm = null;
        XWaylandWindow? window = null;
        server.Ready += fd =>
        {
            wm = new XWaylandWm(fd, host.Loop, shell, host.Seat);
            wm.WindowMapped += mapped => window ??= mapped;
        };

        using var eyes = XWaylandTestSupport.Start("xeyes", server.DisplayName, "-geometry", "150x100+0+0");
        Transaction? first = null;
        Transaction? second = null;
        try
        {
            XWaylandTestSupport.Pump(host, () => window?.Surface?.Current.Buffer is not null);
            first = new Transaction(host.Loop, timeoutMs: 2000);
            first.Completed += () =>
            {
                second = new Transaction(host.Loop, timeoutMs: 2000);
                window!.Configure(second, 0, 0, 240, 160);
                second.Seal();
            };
            window!.Configure(first, 0, 0, 200, 130);
            first.Seal();

            XWaylandTestSupport.Pump(host, () => second?.IsComplete == true);
            Assert.False(first.TimedOut);
            Assert.False(second!.TimedOut);
            Assert.Equal((240, 160), (window.Surface!.Current.Width, window.Surface.Current.Height));
        }
        finally
        {
            first?.Dispose();
            second?.Dispose();
            eyes.Kill();
            eyes.WaitForExit(2000);
            wm?.Dispose();
        }
    }

    [Fact]
    public void A_configured_window_has_no_x_border()
    {
        Assert.SkipUnless(PrivateBus.OnPath("Xwayland") && PrivateBus.OnPath("xeyes"), "Xwayland and xeyes are needed");
        using var host = new CompositorTestHost(640, 480);
        using var shell = new XwaylandShellGlobal(host.Display, host.Compositor);
        using var server = new XWaylandServer(host.Display, host.Loop);
        XWaylandWm? wm = null;
        XWaylandWindow? window = null;
        server.Ready += fd =>
        {
            wm = new XWaylandWm(fd, host.Loop, shell, host.Seat);
            wm.WindowMapped += mapped => window ??= mapped;
        };

        using var eyes = XWaylandTestSupport.Start("xeyes", server.DisplayName, "-geometry", "150x100+0+0", "-bw", "3");
        try
        {
            XWaylandTestSupport.Pump(host, () => window?.Surface?.Current.Buffer is not null);
            Assert.Equal(3, BorderWidth(server.DisplayName, window!.WindowId));
            window.Configure(0, 0, 150, 100);
            XWaylandTestSupport.Pump(host, () => BorderWidth(server.DisplayName, window.WindowId) == 0);
        }
        finally
        {
            eyes.Kill();
            eyes.WaitForExit(2000);
            wm?.Dispose();
        }
    }

    private static unsafe int BorderWidth(string display, uint windowId)
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes(display + "\0");
        fixed (byte* ptr = bytes)
        {
            var conn = Libxcb.xcb_connect((sbyte*)ptr, null);
            try
            {
                Assert.Equal(0, Libxcb.xcb_connection_has_error(conn));
                var reply = Libxcb.xcb_get_geometry_reply(conn, Libxcb.xcb_get_geometry(conn, windowId), null);
                Assert.True(reply != null);
                var width = reply->border_width;
                NativeMemory.Free(reply);
                return width;
            }
            finally
            {
                Libxcb.xcb_disconnect(conn);
            }
        }
    }
}
