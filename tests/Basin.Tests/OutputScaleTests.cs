using Basin.Host;
using Basin.Scene;
using Xunit;

namespace Basin.Tests;

public sealed class OutputScaleTests
{
    [Theory]
    [InlineData(3840, 2560, 596, 397, OutputClass.Desktop, 1.70)]
    [InlineData(3840, 2160, 598, 336, OutputClass.Desktop, 1.70)]
    [InlineData(3840, 2160, 708, 398, OutputClass.Desktop, 1.45)]
    [InlineData(2880, 1800, 301, 188, OutputClass.Laptop, 2.00)]
    [InlineData(3840, 2400, 344, 215, OutputClass.Laptop, 2.25)]
    [InlineData(3840, 2160, 1218, 685, OutputClass.Desktop, 2.65)]
    [InlineData(3840, 2160, 1218, 685, OutputClass.Tv, 2.65)]
    [InlineData(2560, 1440, 598, 336, OutputClass.Desktop, 1.00)]
    [InlineData(1920, 1080, 531, 299, OutputClass.Desktop, 1.00)]
    [InlineData(3440, 1440, 798, 335, OutputClass.Desktop, 1.00)]
    public void The_kwin_table_holds(int width, int height, int widthMm, int heightMm, OutputClass outputClass, double expected)
    {
        var chosen = OutputScale.Choose(new OutputMode(width, height, 60_000), (widthMm, heightMm), outputClass);
        Assert.Equal(expected, chosen, 3);
    }

    [Theory]
    [InlineData(2, 300)]
    [InlineData(300, 2)]
    [InlineData(0, 0)]
    public void A_screen_narrower_than_3mm_is_a_lie_and_stays_at_1(int widthMm, int heightMm)
    {
        Assert.Equal(1.0, OutputScale.Choose(new OutputMode(3840, 2160, 60_000), (widthMm, heightMm), OutputClass.Desktop));
    }

    [Fact]
    public void A_phone_panel_scales_as_a_handheld_but_not_as_a_laptop()
    {
        var mode = new OutputMode(1080, 2400, 60_000);
        var handheld = OutputScale.Choose(mode, (68, 151), OutputClass.Handheld);
        var laptop = OutputScale.Choose(mode, (68, 151), OutputClass.Laptop);
        Assert.True(handheld > laptop);
        Assert.True(handheld >= 2.5);
    }

    [Fact]
    public void An_ultrawide_never_takes_the_television_target()
    {
        var ultrawide = OutputScale.Choose(new OutputMode(3440, 1440, 60_000), (798, 335), OutputClass.Desktop);
        Assert.Equal(1.0, ultrawide);
    }

    [Theory]
    [InlineData(3840, 2560, 596, 397, OutputClass.Desktop)]
    [InlineData(3840, 2160, 1218, 685, OutputClass.Desktop)]
    [InlineData(2880, 1800, 301, 188, OutputClass.Laptop)]
    [InlineData(3840, 2400, 344, 215, OutputClass.Laptop)]
    public void Every_choice_survives_the_wire_snap_unchanged(int width, int height, int widthMm, int heightMm, OutputClass outputClass)
    {
        var chosen = OutputScale.Choose(new OutputMode(width, height, 60_000), (widthMm, heightMm), outputClass);
        Assert.Equal(chosen, OutputScaling.Snap(chosen));
    }

    [Fact]
    public void The_driver_keeps_a_headless_output_at_1()
    {
        Assert.SkipWhen(
            !CompositorTestHost.HasWaylandServer,
            "this host has no libwayland server, and the driver hosts a real display");
        using var host = Basin.Host.BasinHost.Create(Basin.Host.HostOptions.ForBackend("headless"));
        var scene = new Basin.Scene.Scene();
        var layout = new OutputLayout();
        using var renderer = new Basin.Render.Pixman.PixmanRenderer();
        using var driver = new OutputDriver(host, scene, layout, renderer, null)
        {
            HeadlessMode = new OutputMode(3840, 2560, 60_000),
        };
        driver.CreateInitialOutputs();
        Assert.Equal(1, driver.Views[0].Output.Scale);
        Assert.Equal(OutputClass.Desktop, driver.Views[0].Output.Class);
    }

    [Fact]
    public void A_configured_scale_beats_the_default_and_the_scales_array_beats_both()
    {
        Assert.SkipWhen(
            !CompositorTestHost.HasWaylandServer,
            "this host has no libwayland server, and the driver hosts a real display");
        using var host = Basin.Host.BasinHost.Create(Basin.Host.HostOptions.ForBackend("headless"));
        var scene = new Basin.Scene.Scene();
        var layout = new OutputLayout();
        using var renderer = new Basin.Render.Pixman.PixmanRenderer();
        using var driver = new OutputDriver(host, scene, layout, renderer, null)
        {
            HeadlessMode = new OutputMode(1280, 720, 60_000),
            ConfiguredScale = _ => 1.5,
        };
        driver.CreateInitialOutputs();
        Assert.Equal(1.5, driver.Views[0].Output.Scale);

        driver.Scales = [2];
        var pinned = driver.AddView(host.Headless!.CreateOutput(new OutputMode(1280, 720, 60_000)));
        Assert.Equal(2, pinned.Output.Scale);
    }
}
