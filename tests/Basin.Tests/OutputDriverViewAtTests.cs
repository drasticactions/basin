using Basin.Host;
using Xunit;

namespace Basin.Tests;

public sealed class OutputDriverViewAtTests
{
    [Fact]
    public void ViewAt_names_the_view_whose_box_holds_the_point()
    {
        Assert.SkipWhen(!CompositorTestHost.HasWaylandServer, "this host has no libwayland server, and the driver hosts a real display");
        using var host = BasinHost.Create(HostOptions.ForBackend("headless"));
        var scene = new Scene.Scene();
        using var renderer = new Basin.Render.Pixman.PixmanRenderer();
        using var driver = new OutputDriver(host, scene, new OutputLayout(), renderer, null)
        {
            HeadlessMode = new OutputMode(320, 240, 60_000),
        };
        driver.CreateInitialOutputs();
        var first = driver.Views[0];
        var second = driver.AddView(host.Headless!.CreateOutput(new OutputMode(320, 240, 60_000)));
        Assert.NotNull(second);

        Assert.Same(first, driver.ViewAt(first.Box.X + 10, first.Box.Y + 10));
        Assert.Same(second, driver.ViewAt(second!.Box.X + 10, second.Box.Y + 10));
        Assert.Same(second, driver.ViewAt(second.Box.Right - 0.5, second.Box.Bottom - 0.5));
        Assert.Null(driver.ViewAt(-1, -1));
        Assert.Null(driver.ViewAt(Math.Max(first.Box.Right, second.Box.Right) + 1, 10));
    }
}
