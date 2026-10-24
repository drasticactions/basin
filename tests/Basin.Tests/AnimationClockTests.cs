using System.Text.Json;
using Basin.Capabilities;
using Basin.Capabilities.Defaults;
using Basin.Effects;
using Basin.Host;
using Basin.Ipc;
using Basin.Scene;
using Basin.Shell.Xdg;
using Basin.UI.Skia;
using SkiaSharp;
using Xunit;

namespace Basin.Tests;

public sealed class AnimationClockTests
{
    private const long Second = 1_000_000_000;

    public static TheoryData<string> Renderers => GoldenTests.Renderers;

    [Fact]
    public void A_frozen_clock_holds_still_and_advances_only_when_told()
    {
        var clock = new DefaultAnimationClock();
        var before = clock.Nanos;
        clock.Freeze();
        var frozen = clock.Nanos;
        Assert.True(frozen >= before);
        Thread.Sleep(20);
        Assert.Equal(frozen, clock.Nanos);
        Assert.Equal(frozen, clock.FromMonotonic(MonotonicClock.Nanos + Second));

        clock.Advance(Second / 2);
        Assert.Equal(frozen + (Second / 2), clock.Nanos);
        Assert.Throws<ArgumentOutOfRangeException>(() => clock.Advance(-1));
    }

    [Fact]
    public void Resume_continues_from_the_frozen_time_and_never_runs_backward()
    {
        var clock = new DefaultAnimationClock();
        clock.Freeze();
        clock.Advance(Second);
        var frozen = clock.Nanos;
        Thread.Sleep(20);

        clock.Resume();
        var resumed = clock.Nanos;
        Assert.InRange(resumed, frozen, frozen + Second);
        Thread.Sleep(5);
        Assert.True(clock.Nanos > resumed);

        var monotonic = MonotonicClock.Nanos;
        var mapped = clock.FromMonotonic(monotonic);
        Assert.Equal(monotonic - mapped, MonotonicClock.Nanos - clock.Nanos, Second / 100.0);

        clock.Freeze();
        var refrozen = clock.Nanos;
        Assert.True(refrozen >= resumed);
        clock.Resume();
        Assert.True(clock.Nanos >= refrozen);
    }

    [Fact]
    public void An_animation_that_starts_while_frozen_counts_from_the_frozen_time()
    {
        var clock = new DefaultAnimationClock();
        clock.Freeze();
        var start = clock.FromMonotonic(MonotonicClock.Nanos + (Second / 60));
        var timeline = new EffectTimeline { Easing = EasingCurve.Linear };
        timeline.Start(new FrameTick(start, Second / 60), Second);

        Thread.Sleep(20);
        Assert.Equal(0, timeline.Progress(new FrameTick(clock.FromMonotonic(MonotonicClock.Nanos), Second / 60)));

        clock.Advance(Second / 4);
        Assert.Equal(0.25, timeline.Progress(new FrameTick(clock.FromMonotonic(MonotonicClock.Nanos), Second / 60)), 6);
    }

    [Fact]
    public void A_mark_lasts_until_the_next_frame_and_a_frozen_clock_asks_for_none()
    {
        var frames = new FrameClock();
        var requested = 0;
        frames.FrameRequested += () => requested++;
        var clock = new DefaultAnimationClock(frames);

        Assert.False(clock.IsAnimating);
        Assert.True(clock.MarkAnimating());
        Assert.True(clock.IsAnimating);
        clock.BeginFrame();
        Assert.False(clock.IsAnimating);

        clock.Freeze();
        Assert.False(clock.MarkAnimating());
        Assert.False(clock.IsAnimating);
        Assert.Equal(0, requested);

        clock.Advance(Second);
        Assert.Equal(1, requested);
        clock.Resume();
        Assert.Equal(2, requested);
        Assert.True(clock.MarkAnimating());

        var monotonic = new DefaultAnimationClock();
        Assert.Equal(1234, monotonic.FromMonotonic(1234));
        Assert.True(monotonic.MarkAnimating());
        Assert.True(monotonic.IsAnimating);
        monotonic.BeginFrame();
        Assert.False(monotonic.IsAnimating);
    }

    [Theory]
    [MemberData(nameof(Renderers))]
    public void Golden_slide_halfway_on_a_frozen_clock(string renderer)
    {
        CompositorTestHost.SkipUnlessRunnable(renderer);
        using var host = new CompositorTestHost(renderer: renderer);
        var clock = new DefaultAnimationClock();
        clock.Freeze();
        var output = host.SceneOutput;
        output.AnimationClock = clock;

        var track = new SceneRect(host.Scene.Root, 144, 4, new RenderColor(0.3f, 0.3f, 0.3f, 1f));
        track.SetPosition(8, 70);
        var slider = new SceneRect(host.Scene.Root, 24, 24, new RenderColor(0.9f, 0.55f, 0.1f, 1f));
        slider.SetPosition(8, 48);
        var timeline = new EffectTimeline { Easing = EasingCurve.Linear };
        var ticks = new List<FrameTick>();
        output.BeforeRepaint += tick =>
        {
            ticks.Add(tick);
            if (!timeline.IsStarted)
            {
                timeline.Start(tick, Second);
            }

            slider.SetPosition(8 + (int)Math.Round(timeline.Progress(tick) * 120), 48);
        };

        using var state = new OutputState();
        Commit(host, output, state);
        Assert.Equal(8, slider.X);
        Thread.Sleep(20);
        Commit(host, output, state);
        Assert.Equal(8, slider.X);

        clock.Advance(Second / 2);
        Commit(host, output, state);
        Assert.Equal(68, slider.X);
        Assert.All(ticks, tick => Assert.InRange(tick.TargetPresentNanos, ticks[0].TargetPresentNanos, ticks[0].TargetPresentNanos + (Second / 2)));
        Golden.AssertMatches(host, renderer == "pixman" ? "animation-clock-halfway" : $"animation-clock-halfway-{renderer}");

        slider.Destroy();
        track.Destroy();
    }

    [Theory]
    [MemberData(nameof(Renderers))]
    public void Golden_frame_hover_halfway_on_a_frozen_clock(string renderer)
    {
        CompositorTestHost.SkipUnlessRunnable(renderer);
        using var host = new CompositorTestHost(renderer: renderer);
        using var uiHost = new SkiaUIHost();
        var frames = new FrameClock();
        var requested = 0;
        frames.FrameRequested += () => requested++;
        var clock = new DefaultAnimationClock(frames);
        clock.Freeze();
        var chrome = new HoverBarFrameRenderer();
        var frame = new Frame(uiHost, chrome, host.Scene.Root) { Clock = frames, AnimationClock = clock };
        frame.Configure(new Box(20, 40, 120, 60), 1.0, new FrameState { Title = "t", Active = true });
        frame.Commit();
        host.RenderFrame();

        frame.PointerMotion(60, 30);
        Assert.True(frame.Animating);
        Assert.Equal(clock.Nanos, chrome.LastInteraction.HoverChangedAt);

        frames.BeginFrame(host.Output, MonotonicClock.Nanos + (Second / 60));
        Assert.Equal(0, chrome.LastProgress);
        Assert.Equal(0, requested);

        clock.Advance(HoverBarFrameRenderer.FadeNanos / 2);
        Assert.Equal(1, requested);
        frames.BeginFrame(host.Output, MonotonicClock.Nanos + (Second / 60));
        Assert.Equal(0.5, chrome.LastProgress);
        Assert.True(frame.Animating);
        Assert.Equal(1, requested);
        Assert.False(clock.IsAnimating);

        host.RenderFrame();
        Golden.AssertMatches(host, renderer == "pixman" ? "animation-clock-frame-hover" : $"animation-clock-frame-hover-{renderer}");

        clock.Resume();
        Assert.True(clock.MarkAnimating());
        frame.Dispose();
    }

    [Fact]
    public void A_configure_the_client_did_not_answer_and_a_running_animation_keep_the_frame_unsettled()
    {
        using var host = new DamTestHost();
        var (entry, server) = MapToplevel(host, "Unanswered");
        using var settle = new FrameSettle(host.Dam.Outputs);
        PumpFor(host, 200);
        Assert.True(settle.IsSettled, settle.DescribeUnsettled());

        var settled = 0;
        settle.Settled += () => settled++;
        entry.Answers = false;
        server.SetSize(1000, 600);
        var serial = server.SendConfigure();
        host.PumpUntil(() => entry.Serial >= serial);
        PumpFor(host, 50);

        Assert.False(settle.IsSettled);
        var reason = settle.DescribeUnsettled();
        Assert.Contains("app id \"basin-settle\"", reason);
        Assert.Contains("title \"Unanswered\"", reason);
        Assert.Contains("did not answer its newest configure", reason);

        entry.Xdg.AckConfigure(entry.Serial);
        host.PumpToClient();
        Assert.False(settle.IsSettled);
        Assert.True(server.Xdg.IsConfigurePending);

        var resized = host.Client.CreateBuffer(1000, 600, Fill.Solid(1000, 600, 0xFF446688));
        entry.Surface.Attach(resized.Proxy, 0, 0);
        entry.Surface.Damage(0, 0, 1000, 600);
        var clock = host.Dam.Host.Services!.Require<IAnimationClock>();
        Assert.True(clock.MarkAnimating());
        entry.Surface.Commit();
        host.PumpToServer();
        Assert.False(server.Xdg.IsConfigurePending);
        Assert.False(settle.IsSettled);
        Assert.Contains("An animation is running.", settle.DescribeUnsettled());
        Assert.Equal(0, settled);

        host.Dam.Outputs.ScheduleAll();
        PumpFor(host, 200);
        Assert.True(settle.IsSettled, settle.DescribeUnsettled());
        Assert.Equal(1, settled);
        entry.Surface.Destroy();
        host.PumpToClient();
    }

    [Fact]
    public void A_configure_that_keeps_the_size_is_answered_by_its_acknowledgement()
    {
        using var host = new DamTestHost();
        var (entry, server) = MapToplevel(host, "Flower");
        using var settle = new FrameSettle(host.Dam.Outputs);
        PumpFor(host, 200);
        Assert.True(settle.IsSettled, settle.DescribeUnsettled());

        entry.Answers = false;
        var serial = server.SendConfigure();
        host.PumpUntil(() => entry.Serial == serial);
        Assert.False(settle.IsSettled);

        entry.Xdg.AckConfigure(serial);
        host.PumpToServer();
        Assert.True(server.Xdg.IsConfigurePending);
        Assert.True(settle.IsSettled, settle.DescribeUnsettled());
        entry.Surface.Destroy();
        host.PumpToClient();
    }

    [Fact]
    public void The_diag_clock_methods_freeze_advance_and_resume_the_session_clock()
    {
        var frames = new FrameClock();
        var requested = 0;
        frames.FrameRequested += () => requested++;
        var clock = new DefaultAnimationClock(frames);
        using var rig = new IpcTestRig(services => services.Use<IAnimationClock>(clock).Use<IAnimationClockControl>(clock));
        var peer = rig.Connect();
        JsonElement Call(string json) => JsonDocument.Parse(peer.Call(json)).RootElement.Clone();

        var state = Call("""{"method":"diag/clock"}""").GetProperty("result");
        Assert.False(state.GetProperty("frozen").GetBoolean());

        var frozen = Call("""{"method":"diag/clock-freeze"}""").GetProperty("result");
        Assert.True(frozen.GetProperty("frozen").GetBoolean());
        var at = frozen.GetProperty("nanos").GetInt64();
        Thread.Sleep(20);
        Assert.Equal(at, Call("""{"method":"diag/clock"}""").GetProperty("result").GetProperty("nanos").GetInt64());

        var advanced = Call("""{"method":"diag/clock-advance","params":{"ms":250}}""").GetProperty("result");
        Assert.Equal(at + (Second / 4), advanced.GetProperty("nanos").GetInt64());
        Assert.Equal(1, requested);
        Assert.Equal(
            IpcErrorCodes.InvalidParams,
            Call("""{"method":"diag/clock-advance","params":{"ms":-1}}""").GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(at + (Second / 4), clock.Nanos);

        var resumed = Call("""{"method":"diag/clock-resume"}""").GetProperty("result");
        Assert.False(resumed.GetProperty("frozen").GetBoolean());
        Assert.True(resumed.GetProperty("nanos").GetInt64() >= at + (Second / 4));
        Assert.Equal(2, requested);
    }

    [Fact]
    public void A_settle_wait_that_times_out_names_the_window_that_never_answered()
    {
        using var host = new DamTestHost();
        var (entry, server) = MapToplevel(host, "Silent");
        using var settle = new FrameSettle(host.Dam.Outputs);
        PumpFor(host, 200);
        entry.Answers = false;
        var serial = server.SendConfigure();
        host.PumpUntil(() => entry.Serial == serial);

        var loop = host.Dam.Host.Loop;
        using var services = new BasinServices(loop).Freeze();
        using var ipc = new IpcServer(loop, services, new IpcSessionInfo { Compositor = "dam", Settle = settle }, null, false);
        ipc.Start();
        Assert.Equal(0, UnixSocket.Pair(out var mine, out var theirs));
        ipc.Adopt(theirs);
        using var peer = new IpcTestPeer(mine, () => loop.Dispatch(0));
        peer.Send("""{"id":1,"method":"session/settle","params":{"timeout_ms":60}}""");
        var deadline = Environment.TickCount64 + 2000;
        while (!peer.HasFrame() && Environment.TickCount64 < deadline)
        {
            loop.Dispatch(10);
        }

        var error = JsonDocument.Parse(peer.Receive()).RootElement.GetProperty("error");
        Assert.Equal(IpcErrorCodes.Failed, error.GetProperty("code").GetString());
        var message = error.GetProperty("message").GetString()!;
        Assert.StartsWith("The compositor did not settle before the timeout.", message);
        Assert.Contains("title \"Silent\"", message);
        entry.Surface.Destroy();
        host.PumpToClient();
    }

    [Fact]
    public void Frame_callbacks_and_presentation_feedback_keep_real_time_while_the_clock_is_frozen()
    {
        using var host = new DamTestHost();
        var (entry, _) = MapToplevel(host, "Timed");
        var clock = new DefaultAnimationClock();
        clock.Freeze();
        clock.Advance(3600 * Second);
        host.Dam.Outputs.AnimationClock = clock;
        var view = host.Dam.Outputs.Views[0];
        var ticks = new List<long>();
        view.Scene!.BeforeRepaint += tick => ticks.Add(tick.TargetPresentNanos);
        PumpFor(host, 100);

        uint doneMillis = 0;
        var done = false;
        var callback = entry.Surface.Frame();
        callback.Done += (_, e) =>
        {
            doneMillis = e.CallbackData;
            done = true;
        };
        ulong presentedNanos = 0;
        var presented = false;
        var feedback = host.Client.Presentation!.Feedback(entry.Surface);
        feedback.Presented += (_, e) =>
        {
            presentedNanos = ((((ulong)e.TvSecHi << 32) | e.TvSecLo) * 1_000_000_000) + e.TvNsec;
            presented = true;
        };
        var buffer = host.Client.CreateBuffer(1280, 720, Fill.Solid(1280, 720, 0xFF224466));
        entry.Surface.Attach(buffer.Proxy, 0, 0);
        entry.Surface.Damage(0, 0, 1280, 720);
        entry.Surface.Commit();
        var deadline = Environment.TickCount64 + 2000;
        while (!(done && presented) && Environment.TickCount64 < deadline)
        {
            host.PumpToClient();
            host.Dam.Host.Loop.Dispatch(5);
        }

        Assert.True(done && presented);
        Assert.InRange((long)doneMillis, (long)(uint)Environment.TickCount - 5000, (long)(uint)Environment.TickCount);
        Assert.InRange((long)presentedNanos, MonotonicClock.Nanos - (5 * Second), MonotonicClock.Nanos);
        Assert.NotEmpty(ticks);
        Assert.All(ticks, tick => Assert.Equal(clock.Nanos, tick));
        Assert.True(clock.Nanos > MonotonicClock.Nanos + (3500 * Second));
        entry.Surface.Destroy();
        host.PumpToClient();
    }

    private static void Commit(CompositorTestHost host, SceneOutput output, OutputState state)
    {
        output.Ring.AddWhole();
        state.Clear();
        _ = output.Commit(host.Renderer, host.Target, 0, state, new SceneCommitOptions
        {
            Background = RenderColor.Black,
            TargetPresentNanos = MonotonicClock.Nanos + (Second / 60),
        });
    }

    private static void PumpFor(DamTestHost host, int millis)
    {
        var deadline = Environment.TickCount64 + millis;
        while (Environment.TickCount64 < deadline)
        {
            host.PumpToClient();
            host.Dam.Host.Loop.Dispatch(5);
        }
    }

    private static (MappedClient Entry, XdgToplevelWindow Server) MapToplevel(DamTestHost host, string title)
    {
        XdgToplevelWindow? server = null;
        void Capture(XdgToplevelWindow toplevel) => server ??= toplevel;
        host.Dam.Shell.NewToplevel += Capture;
        var surface = host.Client.Compositor.CreateSurface();
        var xdg = host.Client.WmBase!.GetXdgSurface(surface);
        var toplevel = xdg.GetToplevel();
        toplevel.SetAppId("basin-settle");
        toplevel.SetTitle(title);
        var entry = new MappedClient(surface, xdg);
        xdg.Configure += (_, e) =>
        {
            entry.Serial = e.Serial;
            if (entry.Answers && entry.Mapped)
            {
                xdg.AckConfigure(e.Serial);
                surface.Commit();
            }
        };
        surface.Commit();
        host.PumpUntil(() => entry.Serial != 0);
        xdg.AckConfigure(entry.Serial);
        var buffer = host.Client.CreateBuffer(1280, 720, Fill.Solid(1280, 720, 0xFF446688));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, 1280, 720);
        surface.Commit();
        entry.Mapped = true;
        host.PumpUntil(() => server is { IsMapped: true });
        host.Dam.Shell.NewToplevel -= Capture;
        return (entry, server!);
    }

    private sealed class HoverBarFrameRenderer : IFrameRenderer
    {
        public const long FadeNanos = 400_000_000;
        private const int Title = 20;
        private const int Side = 4;
        private int _width;
        private int _height;

        public FrameInteraction LastInteraction { get; private set; }

        public double LastProgress { get; private set; }

        public FrameInsets Measure(in FrameState state, double scale) => new(Title, Side, Side, Side);

        public bool Animating(in FrameState state, in FrameInteraction interaction) => Progress(interaction) < 1;

        public void Draw(IUISurface surface, in Box clientBox, in FrameState state, in FrameInteraction interaction)
        {
            LastInteraction = interaction;
            LastProgress = Progress(interaction);
            _width = surface.Size.Width;
            _height = surface.Size.Height;
            var canvas = ((ISkiaUISurface)surface).BeginDraw();
            try
            {
                canvas.Clear(new SKColor(0x28, 0x46, 0x78));
                using var bar = new SKPaint { Color = new SKColor(0xF0, 0xA0, 0x20) };
                canvas.DrawRect(0, 0, (float)Math.Round(_width * LastProgress), Title, bar);
            }
            finally
            {
                ((ISkiaUISurface)surface).EndDraw();
            }
        }

        public FramePart PartAt(double x, double y, in FrameState state, double scale) =>
            x < 0 || y < 0 || x >= _width || y >= _height ? FramePart.None
            : y < Title ? FramePart.Title
            : FramePart.Border;

        public Box PartBounds(FramePart part) => part == FramePart.Title ? new Box(0, 0, _width, Title) : default;

        private static double Progress(in FrameInteraction interaction) =>
            interaction.Hot != FramePart.Title || interaction.HoverChangedAt == 0
                ? 0
                : Math.Clamp((interaction.Now - interaction.HoverChangedAt) / (double)FadeNanos, 0, 1);
    }

    private sealed class MappedClient(Wayland.WlSurface surface, Basin.Shell.Xdg.Protocol.XdgSurface xdg)
    {
        public Wayland.WlSurface Surface => surface;

        public Basin.Shell.Xdg.Protocol.XdgSurface Xdg => xdg;

        public uint Serial { get; set; }

        public bool Mapped { get; set; }

        public bool Answers { get; set; } = true;
    }
}
