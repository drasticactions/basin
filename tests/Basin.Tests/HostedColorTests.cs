using Basin.Capabilities;
using Basin.Color;
using Basin.Desktop;
using Basin.Diagnostics;
using Basin.Hosted;
using Xunit;

namespace Basin.Tests;

public sealed class HostedColorTests
{
    [Fact]
    public void Color_management_is_off_by_default_and_on_when_asked()
    {
        CompositorTestHost.SkipWithoutWaylandServer();
        using (var plain = new BasinCompositorHost(new BasinCompositorOptions
        {
            AppName = "basin-tests",
            ConfigureServices = HostedDrivers.Without,
        }))
        {
            Assert.Null(plain.Color);
            Assert.Null(plain.Services.Find<ColorManager>());
        }

        using var host = new BasinCompositorHost(new BasinCompositorOptions
        {
            AppName = "basin-tests",
            ColorManagement = true,
            ConfigureServices = HostedDrivers.Without,
        });
        Assert.NotNull(host.Color);
        Assert.NotNull(host.Services.Find<ColorManager>());
        Assert.NotNull(host.Color!.Outputs);
    }

    [Fact]
    public void A_seeded_HDR_state_republishes_the_advertised_output_as_HDR10()
    {
        CompositorTestHost.SkipWithoutWaylandServer();
        BasinCounters.Reset();
        using var host = new BasinCompositorHost(new BasinCompositorOptions
        {
            AppName = "basin-tests",
            ColorManagement = true,
            ConfigureServices = HostedDrivers.Without,
        });
        var view = host.CreateViewOutput(800, 600, 1.0, "sdl");
        host.Screens.Advertise(view.Output, new HostScreenInfo("sdl", "sdl", 0, 0, 800, 600, 1.0, true));
        var color = host.Services.Require<ColorManager>();
        var global = host.Screens.GlobalFor("sdl")!;
        Assert.True(color.TryGetOutputDescription(global, out var sdr));
        Assert.NotEqual(ColorTransferFunction.St2084Pq, sdr.TransferNamed);

        host.Color!.Configuration.Seed(view.Output, new OutputColorState
        {
            HighDynamicRange = true,
            SdrBrightnessNits = 240,
            BrightnessOverrides = new OutputBrightnessOverrides(960, -1, -1),
        });
        host.Color.Outputs!.Refresh();
        Assert.True(color.TryGetOutputDescription(global, out var hdr));
        Assert.Equal(ColorTransferFunction.St2084Pq, hdr.TransferNamed);
        Assert.Equal(ColorPrimaries.Bt2020, hdr.PrimariesNamed);
        Assert.Equal((0u, 960u, 240u), hdr.Luminances);
        Assert.Same(hdr, view.SceneOutput.ColorDescription);

        host.Screens.Withdraw("sdl");
        Assert.False(color.TryGetOutputDescription(global, out _));
        view.Dispose();
    }
}
