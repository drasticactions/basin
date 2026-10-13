using System.Diagnostics;
using Basin.XWayland;
using Xunit;

namespace Basin.Tests;

public sealed class XWaylandPumpTests
{
    [Fact]
    public void Events_read_during_a_flush_outside_the_pump_are_still_dispatched()
    {
        var randremu = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "scripts", "wlclients", "bin", "randremu");
        Assert.SkipUnless(PrivateBus.OnPath("Xwayland") && File.Exists(randremu), "Xwayland and scripts/wlclients/bin/randremu are needed");
        using var host = new CompositorTestHost(1920, 1080);
        using var shell = new XwaylandShellGlobal(host.Display, host.Compositor);
        using var server = new XWaylandServer(host.Display, host.Loop);
        XWaylandWm? wm = null;
        XWaylandWindow? window = null;
        server.Ready += fd =>
        {
            wm = new XWaylandWm(fd, host.Loop, shell, host.Seat);
            wm.WindowMapped += mapped =>
            {
                window ??= mapped;
                mapped.SetFullscreen(true);
                mapped.Configure(0, 0, 1920, 1080);
            };
        };

        using var client = XWaylandTestSupport.Start(randremu, server.DisplayName, "1024x768", "20");
        try
        {
            var clock = Stopwatch.StartNew();
            while (window is null && clock.Elapsed < TimeSpan.FromSeconds(15))
            {
                host.Loop.Dispatch(10);
                host.Display.FlushClients();
                host.Output.StepFrame();
                host.RenderFrame();
            }

            Assert.NotNull(window);
        }
        finally
        {
            client.Kill();
            client.WaitForExit(2000);
            wm?.Dispose();
        }
    }
}
