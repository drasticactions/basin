using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Basin.Avalonia;
using Basin.Hosted;
using Basin.Diagnostics;
using Xunit;

namespace Basin.Tests;

public sealed class BasinViewInputTests
{
    private sealed class Harness : IDisposable
    {
        public Harness()
        {
            CompositorTestHost.SkipWithoutWaylandClient();
            BasinCounters.Reset();
            Host = new BasinCompositorHost(new BasinCompositorOptions { AppName = "waylonia-tests" });
            View = new BasinToplevelView(Host, host => host.CreateViewOutput(320, 240, 1.0, "shell"))
            {
                InputSink = Events.Add,
            };
            Window = new Window { Width = 320, Height = 240, Content = View };
            Window.Show();
            View.Focus();
            Pump();
        }

        public BasinCompositorHost Host { get; }

        public BasinToplevelView View { get; }

        public Window Window { get; }

        public List<BasinViewInput> Events { get; } = [];

        public void Pump()
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            Host.Loop.Dispatch(0);
        }

        public void PumpUntil(Func<bool> condition, string what)
        {
            for (var i = 0; i < 50 && !condition(); i++)
            {
                Pump();
            }

            Assert.True(condition(), what);
        }

        public void Dispose()
        {
            View.ShutdownAsync();
            Pump();
            Window.Close();
            Dispatcher.UIThread.RunJobs();
            Host.Dispose();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void Host_pointer_and_keys_reach_the_sink_on_the_compositor_pass()
    {
        using var harness = new Harness();
        harness.PumpUntil(() => harness.View.Output is not null, "the view never created its output");

        harness.Window.MouseMove(new global::Avalonia.Point(30, 20));
        harness.PumpUntil(
            () => harness.Events.Any(static e => e.Kind == BasinViewInputKind.PointerMotion),
            "no motion reached the sink");
        var motion = harness.Events.Last(static e => e.Kind == BasinViewInputKind.PointerMotion);
        Assert.Equal(30, motion.X, 0.5);
        Assert.Equal(20, motion.Y, 0.5);

        harness.Window.MouseDown(new global::Avalonia.Point(30, 20), MouseButton.Left);
        harness.Window.MouseUp(new global::Avalonia.Point(30, 20), MouseButton.Left);
        harness.PumpUntil(
            () => harness.Events.Count(static e => e.Kind == BasinViewInputKind.PointerButton) == 2,
            "the press and release never reached the sink");
        var buttons = harness.Events.Where(static e => e.Kind == BasinViewInputKind.PointerButton).ToList();
        Assert.Equal((0x110u, true), (buttons[0].Code, buttons[0].Pressed));
        Assert.Equal((0x110u, false), (buttons[1].Code, buttons[1].Pressed));

        harness.Window.MouseWheel(new global::Avalonia.Point(30, 20), new Vector(0, -1));
        harness.PumpUntil(
            () => harness.Events.Any(static e => e.Kind == BasinViewInputKind.PointerAxis),
            "the wheel never reached the sink");

        harness.Window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.None);
        harness.Window.KeyReleaseQwerty(PhysicalKey.A, RawInputModifiers.None);
        harness.PumpUntil(
            () => harness.Events.Count(static e => e.Kind == BasinViewInputKind.Key) == 2,
            "the key never reached the sink");
        var keys = harness.Events.Where(static e => e.Kind == BasinViewInputKind.Key).ToList();
        Assert.Equal((30u, true), (keys[0].Code, keys[0].Pressed));
        Assert.Equal((30u, false), (keys[1].Code, keys[1].Pressed));
    }

    [AvaloniaFact]
    public void A_filtered_key_never_reaches_the_sink_and_an_injected_one_does()
    {
        using var harness = new Harness();
        harness.PumpUntil(() => harness.View.Output is not null, "the view never created its output");
        var filtered = new List<(uint Code, bool Pressed)>();
        harness.View.KeyFilter = (code, pressed) =>
        {
            filtered.Add((code, pressed));
            return code == 62;
        };

        harness.Window.KeyPressQwerty(PhysicalKey.F4, RawInputModifiers.None);
        harness.Window.KeyReleaseQwerty(PhysicalKey.F4, RawInputModifiers.None);
        harness.Window.KeyPressQwerty(PhysicalKey.B, RawInputModifiers.None);
        harness.Window.KeyReleaseQwerty(PhysicalKey.B, RawInputModifiers.None);
        harness.PumpUntil(
            () => harness.Events.Count(static e => e.Kind == BasinViewInputKind.Key) == 2,
            "the unfiltered key never reached the sink");
        Assert.Equal(4, filtered.Count);
        Assert.All(harness.Events.Where(static e => e.Kind == BasinViewInputKind.Key), e => Assert.Equal(48u, e.Code));

        harness.View.InjectKey(57, true);
        harness.View.InjectKey(57, false);
        harness.PumpUntil(
            () => harness.Events.Count(static e => e.Kind == BasinViewInputKind.Key && e.Code == 57) == 2,
            "the injected key never reached the sink");
    }

    [AvaloniaFact]
    public void A_touch_pointer_losing_capture_is_a_touch_cancel()
    {
        using var harness = new Harness();
        harness.PumpUntil(() => harness.View.Output is not null, "the view never created its output");
        var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Touch, isPrimary: true);
        harness.View.RaiseEvent(new PointerCaptureLostEventArgs(harness.View, pointer));
        harness.PumpUntil(
            () => harness.Events.Any(static e => e.Kind == BasinViewInputKind.TouchCancel),
            "the cancel never reached the sink");
        var cancel = harness.Events.Single(static e => e.Kind == BasinViewInputKind.TouchCancel);
        Assert.Equal(pointer.Id, cancel.TouchId);
        Assert.DoesNotContain(harness.Events, static e => e.Kind == BasinViewInputKind.TouchUp);

        var mouse = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);
        harness.View.RaiseEvent(new PointerCaptureLostEventArgs(harness.View, mouse));
        harness.Pump();
        Assert.Single(harness.Events, static e => e.Kind == BasinViewInputKind.TouchCancel);
    }

    [AvaloniaFact]
    public void Capture_and_activation_are_reported_to_the_compositor_side()
    {
        using var harness = new Harness();
        harness.PumpUntil(() => harness.View.Output is not null, "the view never created its output");
        var captured = new List<bool>();
        harness.View.CaptureChanged += captured.Add;
        ICaptureTarget target = harness.View;
        Assert.IsAssignableFrom<ICaptureTarget>(harness.View);

        target.CaptureInput(true);
        harness.PumpUntil(() => captured.Count == 1, "the capture never reached the compositor");
        Assert.True(harness.View.InputCaptured);
        target.CaptureInput(false);
        harness.PumpUntil(() => captured.Count == 2, "the release never reached the compositor");

        harness.View.NotifyActivated(true);
        harness.View.NotifyActivated(false);
        harness.PumpUntil(
            () => harness.Events.Any(static e => e.Kind == BasinViewInputKind.FocusOut),
            "the deactivation never reached the sink");
        Assert.Contains(harness.Events, static e => e.Kind == BasinViewInputKind.FocusIn);
    }
}
