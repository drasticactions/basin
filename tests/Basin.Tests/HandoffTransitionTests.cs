using Basin.Diagnostics;
using Basin.Host;
using Basin.Scene;
using Xunit;

namespace Basin.Tests;

public sealed class HandoffTransitionTests
{
    private sealed class Owner : IDisposable
    {
        public int Disposed { get; private set; }

        public BufferBase? Buffer { get; set; }

        public void Dispose()
        {
            Disposed++;
            Buffer?.Destroy();
            Buffer = null;
        }
    }

    private sealed class Rig : IDisposable
    {
        public Rig(HandoffHold hold = HandoffHold.Ready, TimeSpan? limit = null, bool fullRepaint = false)
        {
            Host = BasinHost.Create(HostOptions.ForBackend("headless"));
            Renderer = new Basin.Render.Pixman.PixmanRenderer();
            Driver = new OutputDriver(Host, Scene, new OutputLayout(), Renderer, null)
            {
                HeadlessMode = new OutputMode(64, 48, 60_000),
                HandoffHold = hold,
                HandoffHoldLimit = limit ?? TimeSpan.FromSeconds(3),
                HandoffClock = () => Now,
                FullRepaint = fullRepaint,
            };
            Driver.HandoffEnded += _ => Ended++;
            Driver.CreateInitialOutputs();
            _ = new SceneRect(Scene.Root, 64, 48, new RenderColor(1, 0, 0, 1));
            Frame = new MemoryBuffer(64, 48, DrmFormat.Xrgb8888);
        }

        public BasinHost Host { get; }

        public Scene.Scene Scene { get; } = new();

        public Basin.Render.Pixman.PixmanRenderer Renderer { get; }

        public OutputDriver Driver { get; }

        public MemoryBuffer Frame { get; }

        public Owner Owner { get; } = new();

        public long Now { get; set; } = 1_000_000_000;

        public int Ended { get; private set; }

        public OutputView View => Driver.Views[0];

        public HandoffRun? Run => View.Handoff;

        public void Pump(int millis = 60)
        {
            var deadline = Environment.TickCount64 + millis;
            while (Environment.TickCount64 < deadline)
            {
                Host.Loop.Dispatch(5);
            }
        }

        public void Dispose()
        {
            Driver.Dispose();
            Scene.Root.Destroy();
            Frame.Destroy();
            Renderer.Dispose();
            Host.Dispose();
        }
    }

    [Theory]
    [InlineData(0.0, 1f)]
    [InlineData(0.5, 0.125f)]
    [InlineData(1.0, 0f)]
    public void The_crossfade_ramps_opacity_from_one_to_zero_with_an_ease_out(double progress, float opacity)
    {
        var transition = new CrossfadeTransition(TimeSpan.FromMilliseconds(400));
        var frame = new HandoffFrame("X", 64, 48, 128, 96);
        transition.Begin(frame);
        Assert.Equal(1f, frame.Opacity);
        Assert.Equal(new Box(0, 0, 64, 48), frame.Destination);

        transition.Step(frame, progress);
        Assert.Equal(opacity, frame.Opacity, 3);

        transition.End(frame);
        Assert.Equal(0f, frame.Opacity);
    }

    [Fact]
    public void The_crossfade_takes_a_custom_easing()
    {
        var transition = new CrossfadeTransition(TimeSpan.FromMilliseconds(400), t => t);
        var frame = new HandoffFrame("X", 64, 48, 64, 48);
        transition.Step(frame, 0.25);
        Assert.Equal(0.75f, frame.Opacity, 3);
    }

    [Fact]
    public void The_oracle_steps_the_crossfade_and_ends_it()
    {
        Assert.SkipWhen(!CompositorTestHost.HasWaylandServer, "this host has no libwayland server, and the driver hosts a real display");
        using var rig = new Rig(fullRepaint: true);
        var frame = new MemoryBuffer(64, 48, DrmFormat.Xrgb8888);
        rig.Owner.Buffer = frame;
        Assert.True(rig.Driver.StartHandoff(rig.View, frame, 64, 48, rig.Owner));

        rig.Pump();
        Assert.True(rig.Host.Ready);
        Assert.Equal(rig.Now, rig.Run!.Started);

        rig.Now += 200_000_000;
        rig.Pump();
        Assert.Equal(0.125f, rig.Run!.Frame.Opacity, 3);

        rig.Now += 300_000_000;
        rig.Pump();
        Assert.False(rig.View.HandoffActive);
        Assert.Equal(1, rig.Ended);
        Assert.Equal(1, rig.Owner.Disposed);
    }

    [Fact]
    public void A_ready_hold_starts_the_clock_after_the_first_frame_and_ends_once()
    {
        Assert.SkipWhen(!CompositorTestHost.HasWaylandServer, "this host has no libwayland server, and the driver hosts a real display");
        var live = BasinCounters.LiveObjects;
        HandoffRun? ended;
        using (var rig = new Rig())
        {
            var frame = new MemoryBuffer(64, 48, DrmFormat.Xrgb8888);
            rig.Owner.Buffer = frame;
            Assert.True(rig.Driver.StartHandoff(rig.View, frame, 64, 48, rig.Owner));
            Assert.True(rig.View.HandoffActive);
            Assert.True(rig.Run!.Node.Parent!.Parent!.ExcludeFromScanout);

            rig.Pump();
            Assert.Equal(rig.Now, rig.Run!.Started);

            rig.Now += 200_000_000;
            rig.Pump();
            ended = rig.Run!;
            Assert.Equal(0.125f, rig.Run!.Frame.Opacity, 3);
            Assert.Equal(0, rig.Ended);

            rig.Now += 300_000_000;
            rig.Pump();
            Assert.False(rig.View.HandoffActive);
            Assert.Equal(1, rig.Ended);
            Assert.Equal(1, rig.Owner.Disposed);

            rig.Now += 300_000_000;
            rig.Pump();
            Assert.Equal(1, rig.Ended);
            Assert.True(ended.Node.IsDestroyed);
            Assert.True(ended.Tree.IsDestroyed);
        }

        if (BasinCounters.Enabled)
        {
            Assert.True(live == BasinCounters.LiveObjects, BasinCounters.CensusReport());
        }
    }

    [Fact]
    public void A_signal_hold_waits_for_release()
    {
        Assert.SkipWhen(!CompositorTestHost.HasWaylandServer, "this host has no libwayland server, and the driver hosts a real display");
        using var rig = new Rig(HandoffHold.Signal);
        Assert.True(rig.Driver.StartHandoff(rig.View, rig.Frame, 64, 48, rig.Owner));
        rig.Pump();
        Assert.True(rig.Host.Ready);
        Assert.Equal(-1, rig.Run!.Started);
        Assert.Equal(1f, rig.Run.Frame.Opacity);

        rig.Now += 100_000_000;
        rig.Driver.ReleaseHandoff();
        rig.Pump();
        Assert.Equal(rig.Now, rig.Run!.Started);
    }

    [Fact]
    public void The_hold_limit_starts_the_clock_anyway()
    {
        Assert.SkipWhen(!CompositorTestHost.HasWaylandServer, "this host has no libwayland server, and the driver hosts a real display");
        using var rig = new Rig(HandoffHold.Signal, TimeSpan.FromMilliseconds(50));
        Assert.True(rig.Driver.StartHandoff(rig.View, rig.Frame, 64, 48, rig.Owner));
        rig.Pump(20);
        Assert.Equal(-1, rig.Run!.Started);

        rig.Now += 60_000_000;
        rig.Pump(120);
        Assert.Equal(rig.Now, rig.Run!.Started);
    }

    [Fact]
    public void No_transition_makes_every_handoff_a_cut()
    {
        Assert.SkipWhen(!CompositorTestHost.HasWaylandServer, "this host has no libwayland server, and the driver hosts a real display");
        using var rig = new Rig();
        rig.Driver.Handoff = null;
        Assert.False(rig.Driver.StartHandoff(rig.View, rig.Frame, 64, 48, rig.Owner));
        Assert.Equal(1, rig.Owner.Disposed);
        Assert.False(rig.View.HandoffActive);
    }

    [Fact]
    public void Removing_the_driver_mid_transition_releases_the_frame()
    {
        Assert.SkipWhen(!CompositorTestHost.HasWaylandServer, "this host has no libwayland server, and the driver hosts a real display");
        var rig = new Rig();
        Assert.True(rig.Driver.StartHandoff(rig.View, rig.Frame, 64, 48, rig.Owner));
        rig.Pump();
        rig.Dispose();
        Assert.Equal(1, rig.Owner.Disposed);
    }
}
