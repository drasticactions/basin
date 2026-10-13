using Basin.Host;
using Basin.Scene;
using Xunit;

namespace Basin.Tests;

public sealed class HandoffOutroTests
{
    private sealed class SlowOutro : IHandoffOutro
    {
        public TimeSpan Duration => TimeSpan.FromSeconds(30);

        public int Steps { get; private set; }

        public void Begin(OutputView view)
        {
        }

        public void Step(OutputView view, double progress) => Steps++;

        public void End(OutputView view)
        {
        }
    }

    private sealed class GatedOutro : IHandoffOutro
    {
        public TimeSpan Duration => TimeSpan.FromMilliseconds(20);

        public bool Ready { get; set; }

        public int Steps { get; private set; }

        public void Begin(OutputView view)
        {
        }

        public void Step(OutputView view, double progress) => Steps++;

        public void End(OutputView view)
        {
        }
    }

    private static (BasinHost Host, Scene.Scene Scene, OutputDriver Driver, Basin.Render.Pixman.PixmanRenderer Renderer) Start()
    {
        var host = BasinHost.Create(HostOptions.ForBackend("headless") with { Handoff = HandoffMode.Retain });
        var scene = new Scene.Scene();
        var renderer = new Basin.Render.Pixman.PixmanRenderer();
        var driver = new OutputDriver(host, scene, new OutputLayout(), renderer, null)
        {
            HeadlessMode = new OutputMode(64, 48, 60_000),
            ContinuousRepaint = true,
        };
        driver.CreateInitialOutputs();
        return (host, scene, driver, renderer);
    }

    private static void Pump(BasinHost host, Func<bool> until, int millis = 2000)
    {
        var deadline = Environment.TickCount64 + millis;
        while (!until() && Environment.TickCount64 < deadline)
        {
            host.Loop.Dispatch(5);
        }
    }

    [Fact]
    public void The_windows_fade_fades_three_layers_and_keeps_the_two_below()
    {
        var scene = new Scene.Scene();
        var layers = new SceneLayers(scene.Root);
        layers.Top.Alpha = 0.5f;
        var outro = new WindowsFadeOutro(layers, TimeSpan.FromMilliseconds(300));
        outro.Begin(null!);
        outro.Step(null!, 0.5);
        Assert.Equal(0.125f, layers.Windows.Alpha, 3);
        Assert.Equal(0.0625f, layers.Top.Alpha, 3);
        Assert.Equal(0.125f, layers.Overlay.Alpha, 3);
        Assert.Equal(1f, layers.Background.Alpha);
        Assert.Equal(1f, layers.Bottom.Alpha);
        Assert.Equal(1f, layers.Lock.Alpha);

        outro.End(null!);
        Assert.Equal(0f, layers.Windows.Alpha);
        Assert.Equal(0f, layers.Top.Alpha);
        Assert.Equal(1f, layers.Background.Alpha);
        scene.Root.Destroy();
    }

    [Fact]
    public void Retain_and_stop_flattens_waits_for_the_flip_and_calls_done_once()
    {
        Assert.SkipWhen(!CompositorTestHost.HasWaylandServer, "this host has no libwayland server, and the driver hosts a real display");
        var (host, scene, driver, renderer) = Start();
        using (host)
        using (renderer)
        using (driver)
        {
            Pump(host, () => driver.Views[0].Rendered > 0);
            var rendered = driver.Views[0].Rendered;
            var done = 0;
            driver.RetainAndStop(null, TimeSpan.FromSeconds(5), () => done++);
            Assert.True(driver.Retaining);
            Pump(host, () => done > 0);
            Assert.Equal(1, done);
            Assert.False(driver.Retaining);
            Assert.True(driver.Views[0].Rendered > rendered);

            driver.RetainAndStop(null, TimeSpan.FromSeconds(5), () => done++);
            Pump(host, () => false, 100);
            Assert.Equal(1, done);
            scene.Root.Destroy();
        }
    }

    [Fact]
    public void Retain_and_stop_calls_done_when_the_budget_runs_out()
    {
        Assert.SkipWhen(!CompositorTestHost.HasWaylandServer, "this host has no libwayland server, and the driver hosts a real display");
        var (host, scene, driver, renderer) = Start();
        using (host)
        using (renderer)
        using (driver)
        {
            Pump(host, () => driver.Views[0].Rendered > 0);
            var outro = new SlowOutro();
            var done = 0;
            var started = Environment.TickCount64;
            driver.RetainAndStop(outro, TimeSpan.FromMilliseconds(80), () => done++);
            Pump(host, () => done > 0);
            Assert.Equal(1, done);
            Assert.True(outro.Steps > 0);
            Assert.InRange(Environment.TickCount64 - started, 60, 1500);
            Pump(host, () => false, 100);
            Assert.Equal(1, done);
            scene.Root.Destroy();
        }
    }

    [Fact]
    public void The_run_loop_runs_the_outro_on_an_orderly_stop_when_the_policy_retains()
    {
        Assert.SkipWhen(!CompositorTestHost.HasWaylandServer, "this host has no libwayland server, and the driver hosts a real display");
        var (host, scene, driver, renderer) = Start();
        using (host)
        using (renderer)
        using (driver)
        {
            var layers = new SceneLayers(scene.Root);
            var loop = new CompositorRunLoop(host, driver)
            {
                Frames = 3,
                Outro = new WindowsFadeOutro(layers, TimeSpan.FromMilliseconds(30)),
            };
            loop.Run();
            Assert.Equal(0f, layers.Windows.Alpha);
            Assert.Equal(1f, layers.Background.Alpha);

            var failing = new CompositorRunLoop(host, driver) { Frames = driver.Views[0].Rendered + 2, Stopping = HandoffStop.Failure };
            layers.Windows.Alpha = 1f;
            failing.Run();
            Assert.Equal(1f, layers.Windows.Alpha);
            scene.Root.Destroy();
        }
    }

    [Fact]
    public void An_outro_that_is_not_ready_holds_its_clock_until_it_is()
    {
        Assert.SkipWhen(!CompositorTestHost.HasWaylandServer, "this host has no libwayland server, and the driver hosts a real display");
        var (host, scene, driver, renderer) = Start();
        using (host)
        using (renderer)
        using (driver)
        {
            Pump(host, () => driver.Views[0].Rendered > 0);
            var outro = new GatedOutro();
            var done = 0;
            driver.RetainAndStop(outro, TimeSpan.FromSeconds(5), () => done++);
            Pump(host, () => false, 150);
            Assert.Equal(0, outro.Steps);
            Assert.Equal(0, done);

            outro.Ready = true;
            Pump(host, () => done > 0);
            Assert.Equal(1, done);
            Assert.True(outro.Steps > 0);
            scene.Root.Destroy();
        }
    }

    [Fact]
    public void An_outro_that_never_becomes_ready_still_ends_at_the_budget()
    {
        Assert.SkipWhen(!CompositorTestHost.HasWaylandServer, "this host has no libwayland server, and the driver hosts a real display");
        var (host, scene, driver, renderer) = Start();
        using (host)
        using (renderer)
        using (driver)
        {
            Pump(host, () => driver.Views[0].Rendered > 0);
            var outro = new GatedOutro();
            var done = 0;
            driver.RetainAndStop(outro, TimeSpan.FromMilliseconds(80), () => done++);
            Pump(host, () => done > 0);
            Assert.Equal(1, done);
            Assert.Equal(0, outro.Steps);
            scene.Root.Destroy();
        }
    }
}
