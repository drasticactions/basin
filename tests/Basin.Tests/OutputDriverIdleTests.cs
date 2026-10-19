using Basin.Capabilities;
using Basin.Host;
using Basin.Scene;
using Xunit;

namespace Basin.Tests;

public sealed class OutputDriverIdleTests
{
    private static void Pump(BasinHost host, int millis)
    {
        var deadline = Environment.TickCount64 + millis;
        while (Environment.TickCount64 < deadline)
        {
            host.Loop.Dispatch(5);
        }
    }

    private static (BasinHost Host, Scene.Scene Scene, OutputDriver Driver, Basin.Render.Pixman.PixmanRenderer Renderer) Start()
    {
        var host = BasinHost.Create(HostOptions.ForBackend("headless"));
        var scene = new Scene.Scene();
        var renderer = new Basin.Render.Pixman.PixmanRenderer();
        var driver = new OutputDriver(host, scene, new OutputLayout(), renderer, null)
        {
            HeadlessMode = new OutputMode(320, 240, 60_000),
        };
        driver.CreateInitialOutputs();
        return (host, scene, driver, renderer);
    }

    [Fact]
    public void A_presented_frame_does_not_ask_for_another_repaint()
    {
        Assert.SkipWhen(!CompositorTestHost.HasWaylandServer, "this host has no libwayland server, and the driver hosts a real display");
        var (host, scene, driver, renderer) = Start();
        using (host)
        using (renderer)
        using (driver)
        {
            var rect = new SceneRect(scene.Root, 40, 40, new RenderColor(1, 0, 0, 1));
            var attempts = 0;
            driver.BeforeRepaint += _ => attempts++;
            Pump(host, 200);
            var settled = driver.Views[0].Rendered;
            var tried = attempts;
            Pump(host, 300);

            Assert.Equal(settled, driver.Views[0].Rendered);
            Assert.Equal(tried, attempts);
            Assert.True(driver.Views[0].Scheduler!.IsIdle);

            rect.Color = new RenderColor(0, 1, 0, 1);
            Pump(host, 200);
            Assert.Equal(settled + 1, driver.Views[0].Rendered);
            Assert.Equal(tried + 1, attempts);
            rect.Destroy();
        }
    }

    [Fact]
    public void Damage_made_in_before_repaint_lands_in_that_frame_and_asks_for_no_other()
    {
        Assert.SkipWhen(!CompositorTestHost.HasWaylandServer, "this host has no libwayland server, and the driver hosts a real display");
        var (host, scene, driver, renderer) = Start();
        using (host)
        using (renderer)
        using (driver)
        {
            var rect = new SceneRect(scene.Root, 40, 40, new RenderColor(1, 0, 0, 1));
            var flip = false;
            driver.BeforeRepaint += _ =>
            {
                flip = !flip;
                rect.Color = flip ? new RenderColor(0, 0, 1, 1) : new RenderColor(1, 0, 0, 1);
            };
            Pump(host, 200);
            var settled = driver.Views[0].Rendered;
            Pump(host, 300);

            Assert.Equal(settled, driver.Views[0].Rendered);
            driver.ScheduleAll();
            Pump(host, 200);
            Assert.Equal(settled + 1, driver.Views[0].Rendered);
            rect.Destroy();
        }
    }

    [Fact]
    public void Frame_settle_answers_when_the_outputs_go_idle()
    {
        Assert.SkipWhen(!CompositorTestHost.HasWaylandServer, "this host has no libwayland server, and the driver hosts a real display");
        var (host, scene, driver, renderer) = Start();
        using (host)
        using (renderer)
        using (driver)
        using (var settle = new FrameSettle(driver))
        {
            var rect = new SceneRect(scene.Root, 40, 40, new RenderColor(1, 0, 0, 1));
            Pump(host, 200);
            Assert.True(settle.IsSettled);

            var settled = 0;
            settle.Settled += () => settled++;
            rect.Color = new RenderColor(0, 1, 0, 1);
            Assert.False(settle.IsSettled);
            Pump(host, 200);

            Assert.True(settle.IsSettled);
            Assert.Equal(1, settled);
            Assert.Equal(driver.Views[0].Rendered, settle.Presented);
            rect.Destroy();
        }
    }

    [Fact]
    public void A_toolkit_that_always_ticks_settles_after_two_quiet_pumps()
    {
        Assert.SkipWhen(!CompositorTestHost.HasWaylandServer, "this host has no libwayland server, and the driver hosts a real display");
        var (host, scene, driver, renderer) = Start();
        using (host)
        using (renderer)
        using (driver)
        using (var ui = new TickingUIHost { Due = 16 })
        using (var uiDriver = new UIDriver(ui, host.Loop))
        using (var settle = new FrameSettle(driver, uiDriver))
        {
            uiDriver.Start();
            var rect = new SceneRect(scene.Root, 40, 40, new RenderColor(1, 0, 0, 1));
            Pump(host, 200);
            Assert.False(settle.IsSettled);

            var pumpsAtSettle = -1;
            var start = ui.Pumps;
            settle.Settled += () =>
            {
                if (pumpsAtSettle < 0)
                {
                    pumpsAtSettle = ui.Pumps - start;
                }
            };
            Pump(host, 300);

            Assert.InRange(pumpsAtSettle, 2, 4);
            ui.Due = null;
            uiDriver.Schedule();
            Assert.True(settle.IsSettled);
            rect.Destroy();
        }
    }

    [Fact]
    public void A_second_driver_added_later_holds_the_settle_until_it_is_removed()
    {
        Assert.SkipWhen(!CompositorTestHost.HasWaylandServer, "this host has no libwayland server, and the driver hosts a real display");
        var (host, scene, driver, renderer) = Start();
        using (host)
        using (renderer)
        using (driver)
        using (var first = new TickingUIHost())
        using (var firstDriver = new UIDriver(first, host.Loop))
        using (var second = new TickingUIHost { Due = 0 })
        using (var secondDriver = new UIDriver(second, host.Loop))
        using (var settle = new FrameSettle(driver, firstDriver))
        {
            var rect = new SceneRect(scene.Root, 40, 40, new RenderColor(1, 0, 0, 1));
            Pump(host, 200);
            Assert.True(settle.IsSettled);

            settle.Add(secondDriver);
            settle.Add(secondDriver);
            Assert.False(settle.IsSettled);

            var settled = 0;
            settle.Settled += () => settled++;
            settle.Remove(secondDriver);
            Assert.True(settle.IsSettled);
            Assert.Equal(1, settled);
            rect.Destroy();
        }
    }

    private sealed class TickingUIHost : IUIHost
    {
        private readonly FalsifierUIHost _inner = new();

        public int Pumps { get; private set; }

        public long? Due { get; set; }

        public UITargetKind Produces => UITargetKind.Memory;

        public long? NextDueMillis => Due;

        public event Action? WakeupRequested;

        public IUISurface? CreateSurface(in UISurfaceOptions options) => _inner.CreateSurface(options);

        public void Pump() => Pumps++;

        public void RequestWakeup() => WakeupRequested?.Invoke();

        public void Dispose() => _inner.Dispose();
    }
}
