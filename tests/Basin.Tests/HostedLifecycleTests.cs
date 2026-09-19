using Basin.Diagnostics;
using Basin.Hosted;
using Basin.Seat;
using Xunit;

namespace Basin.Tests;

public sealed class HostedLifecycleTests
{
    private sealed class Fixture : IDisposable
    {
        public Fixture()
        {
            BasinCounters.Reset();
            Host = new BasinCompositorHost(new BasinCompositorOptions { AppName = "basin-tests" });
        }

        public BasinCompositorHost Host { get; }

        public void Dispose()
        {
            Host.Dispose();
            LeakTracking.Expect(0, BasinCounters.LiveObjects);
        }
    }

    private sealed class RecordingSink : Capabilities.IFrameSink
    {
        public List<long> Predicted { get; } = [];

        public void BeginFrame(IOutput output, long predictedVblankNanos) => Predicted.Add(predictedVblankNanos);

        public void EndFrame(IOutput output, long presentedNanos)
        {
        }
    }

    private sealed class RecordingTouchGrab : ITouchGrab
    {
        public int Cancelled { get; private set; }

        public uint Down(Surface surface, uint timeMs, int id, double x, double y) => 0;

        public void Up(uint timeMs, int id)
        {
        }

        public void Motion(uint timeMs, int id, double x, double y)
        {
        }

        public void Frame()
        {
        }

        public void Cancel() => Cancelled++;
    }

    [Fact]
    public void Suspend_stops_frames_and_presentation_and_resume_asks_every_view_to_draw()
    {
        using var fixture = new Fixture();
        var host = fixture.Host;
        var first = host.CreateViewOutput(320, 240);
        var second = host.CreateViewOutput(200, 100);
        var renders = 0;
        first.RequestRender = () => renders++;
        second.RequestRender = () => renders++;

        host.Suspend();
        host.Suspend();
        Assert.True(host.IsSuspended);
        Assert.True(host.Session.IsSuspended);
        Assert.True(host.Wake.IsSuspended);
        Assert.False(first.IsPresenting);
        Assert.False(second.IsPresenting);
        Assert.False(host.EnterFrame(TimeSpan.FromMilliseconds(1)));
        host.InvalidateDirtyViews();
        Assert.Equal(0, renders);

        var late = host.CreateViewOutput(64, 64);
        Assert.False(late.IsPresenting);

        host.Resume();
        host.Resume();
        Assert.False(host.IsSuspended);
        Assert.False(host.Wake.IsSuspended);
        Assert.True(first.IsPresenting);
        Assert.True(late.IsPresenting);
        Assert.Equal(2, renders);
        Assert.True(host.EnterFrame(TimeSpan.FromMilliseconds(2)));
        host.ExitFrame();
    }

    [Fact]
    public void The_first_frame_after_resume_follows_the_wall_clock_rather_than_the_cadence()
    {
        using var fixture = new Fixture();
        var host = fixture.Host;
        host.CreateViewOutput(320, 240);
        var sink = new RecordingSink();
        var clock = Assert.IsType<Capabilities.Defaults.FrameClock>(host.Services.Require<Capabilities.IFrameClock>());
        clock.Add(sink);
        var refresh = 1_000_000_000L / 60;

        host.EnterFrame(TimeSpan.FromMilliseconds(1));
        host.ExitFrame();
        host.EnterFrame(TimeSpan.FromMilliseconds(2));
        host.ExitFrame();
        Assert.Equal(2, sink.Predicted.Count);
        Assert.Equal(sink.Predicted[0] + refresh, sink.Predicted[1]);

        host.Suspend();
        Thread.Sleep(60);
        host.Resume();
        var before = MonotonicClock.Nanos;
        host.EnterFrame(TimeSpan.FromMilliseconds(3));
        host.ExitFrame();
        Assert.Equal(3, sink.Predicted.Count);
        Assert.True(sink.Predicted[2] >= before + refresh, "the prediction still followed the pre-suspend cadence");
        Assert.True(sink.Predicted[2] - sink.Predicted[1] > 2 * refresh, "the gap was not reported as one long frame");
        clock.Remove(sink);
    }

    [Fact]
    public void Reconfigure_changes_mode_scale_and_transform_atomically_and_republishes_the_screen()
    {
        using var fixture = new Fixture();
        var host = fixture.Host;
        var view = host.CreateViewOutput(320, 240, 1.0, "phone");
        host.Screens.Advertise(view.Output, new HostScreenInfo("phone", "phone", 0, 0, 320, 240, 1.0, true));
        var reconfigured = 0;
        var renders = 0;
        view.Reconfigured += _ => reconfigured++;
        view.RequestRender = () => renders++;

        view.Reconfigure(240, 320, 2.0, OutputTransform.Rotate90);
        Assert.Equal((240, 320), (view.Output.CurrentMode.Width, view.Output.CurrentMode.Height));
        Assert.Equal(OutputTransform.Rotate90, view.Output.Transform);
        Assert.Equal(2.0, view.Output.Scale);
        Assert.Equal((240, 320, 2.0), (view.Target.Width, view.Target.Height, view.Target.Scale));
        Assert.Equal(1, reconfigured);
        Assert.Equal(1, renders);
        var screen = Assert.Single(host.Screens.Current, static s => s.Key == "phone");
        Assert.Equal((320, 240, 2.0), (screen.Width, screen.Height, screen.Scaling));
        Assert.Equal(OutputTransform.Rotate90, screen.Transform);

        view.Reconfigure(240, 320, 2.0, OutputTransform.Rotate90);
        Assert.Equal(1, reconfigured);

        view.Resize(320, 240, 2.0);
        Assert.Equal(OutputTransform.Rotate90, view.Output.Transform);
        Assert.Equal((320, 240), (view.Output.CurrentMode.Width, view.Output.CurrentMode.Height));
        Assert.Equal(2, reconfigured);
    }

    [Fact]
    public void A_touch_cancel_reaches_the_seat_grab()
    {
        using var fixture = new Fixture();
        var grab = new RecordingTouchGrab();
        fixture.Host.Seat.Touch.StartGrab(grab);
        fixture.Host.CancelTouch();
        fixture.Host.Seat.Touch.EndGrab(grab);
        Assert.Equal(1, grab.Cancelled);
    }

    [Fact]
    public void The_key_synthesizer_types_through_the_seat_keyboard()
    {
        using var fixture = new Fixture();
        var host = fixture.Host;
        var pressed = new List<uint>();
        host.Seat.Keyboard.ModifiersChanged += () => pressed.Add(host.Seat.Keyboard.ModifierState.Depressed);
        host.Keys.Keymap = host.Seat.Keyboard.Keymap;
        Assert.Equal(1, host.Keys.Type("A", 1));
        Assert.Empty(host.Seat.Keyboard.PressedKeys);
        Assert.Contains(1u, pressed);
        Assert.Equal(0u, host.Seat.Keyboard.ModifierState.Depressed);
    }
}
