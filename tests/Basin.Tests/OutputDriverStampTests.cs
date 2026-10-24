using Basin.Host;
using Basin.Scene;
using Xunit;

namespace Basin.Tests;

public sealed class OutputDriverStampTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_stamped_field_reaches_the_frame_commit(bool fullRepaint)
    {
        Assert.SkipWhen(!CompositorTestHost.HasWaylandServer, "this host has no libwayland server, and the driver hosts a real display");
        using var host = BasinHost.Create(HostOptions.ForBackend("headless"));
        var scene = new Scene.Scene();
        using var renderer = new Basin.Render.Pixman.PixmanRenderer();
        using var driver = new OutputDriver(host, scene, new OutputLayout(), renderer, null)
        {
            HeadlessMode = new OutputMode(64, 48, 60_000),
            FullRepaint = fullRepaint,
        };
        var stamped = 0;
        driver.StampFrame += (_, state) =>
        {
            stamped++;
            state.RequestOutFence();
        };
        driver.CreateInitialOutputs();
        var frames = 0;
        var carried = 0;
        driver.Views[0].Output.Committed += fields =>
        {
            if ((fields & OutputStateFields.Buffer) != 0)
            {
                frames++;
                carried += (fields & OutputStateFields.OutFence) != 0 ? 1 : 0;
            }
        };
        var rect = new SceneRect(scene.Root, 20, 20, new RenderColor(1, 0, 0, 1));

        var deadline = Environment.TickCount64 + 1000;
        while (frames < 3 && Environment.TickCount64 < deadline)
        {
            rect.SetPosition(frames, 0);
            host.Loop.Dispatch(5);
        }

        Assert.True(frames >= 3, $"only {frames} frames committed");
        Assert.True(stamped >= frames);
        Assert.Equal(frames, carried);
        rect.Destroy();
    }
}
