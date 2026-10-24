using Basin.Diagnostics;
using Basin.Host;
using Xunit;

namespace Basin.Tests;

public sealed class SecondaryOutputTests
{
    [Fact]
    public void Continuous_repaint_keeps_a_secondary_only_output_drawing_and_counts_its_frames()
    {
        BasinCounters.Reset();
        using var host = BasinHost.Create(HostOptions.ForBackend("headless"));
        var scene = new Scene.Scene();
        using var renderer = new Basin.Render.Pixman.PixmanRenderer();
        using var allocator = new ShmAllocator();
        var driver = new OutputDriver(host, scene, new OutputLayout(), renderer, null) { ContinuousRepaint = true };
        try
        {
            var output = host.Headless!.CreateOutput(new OutputMode(64, 48, 60_000));
            var view = driver.AddView(output, allocator, secondary: true);
            Assert.True(view.IsSecondary);

            var deadline = Environment.TickCount64 + 3000;
            while (view.Rendered < 10 && Environment.TickCount64 < deadline)
            {
                host.Loop.Dispatch(5);
            }

            Assert.True(view.Rendered >= 10, $"the secondary view drew {view.Rendered} frames");
            Assert.Equal(view.Rendered, driver.PrimaryRendered);
        }
        finally
        {
            driver.Dispose();
            scene.Root.Destroy();
        }
    }
}
