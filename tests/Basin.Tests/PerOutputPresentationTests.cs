using Basin.Backend.Headless;
using Basin.Desktop;
using Basin.Scene;
using Wayland;
using Xunit;

namespace Basin.Tests;

public sealed class PerOutputPresentationTests
{
    private sealed class TwoOutputs : IDisposable
    {
        private readonly Swapchain _slowChain;
        private readonly Swapchain _fastChain;
        private readonly OutputState _state = new();

        public TwoOutputs(CompositorTestHost host)
        {
            Host = host;
            Fast = host.Backend.CreateOutput(new OutputMode(160, 120, 144_000), manualFrameClock: true);
            host.Layout.Add(Fast, 160, 0);
            Pump = new PresentationFeedbackPump(host.Presentation, host.Layout);
            Slow = new SceneOutput(host.Scene, host.Output) { Presented = host.Compositor.PresentedSets.For(host.Output) };
            FastScene = new SceneOutput(host.Scene, Fast)
            {
                Presented = host.Compositor.PresentedSets.For(Fast),
                Position = new Point(160, 0),
            };
            _slowChain = new Swapchain(new ShmAllocator(), 160, 120, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
            _fastChain = new Swapchain(new ShmAllocator(), 160, 120, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
        }

        public CompositorTestHost Host { get; }

        public HeadlessOutput Fast { get; }

        public PresentationFeedbackPump Pump { get; }

        public SceneOutput Slow { get; }

        public SceneOutput FastScene { get; }

        public void FrameSlow()
        {
            Host.Loop.Dispatch(0);
            Slow.Ring.AddWhole();
            _ = Slow.Commit(Host.Renderer, _slowChain, _state);
            Host.Output.StepFrame();
        }

        public void FrameFast()
        {
            Host.Loop.Dispatch(0);
            FastScene.Ring.AddWhole();
            _ = FastScene.Commit(Host.Renderer, _fastChain, _state);
            Fast.StepFrame();
        }

        public void Dispose()
        {
            Slow.Dispose();
            FastScene.Dispose();
            _slowChain.Dispose();
            _fastChain.Dispose();
            _state.Dispose();
            Pump.Dispose();
            Host.Layout.Remove(Fast);
            Fast.Destroy();
        }
    }

    private static (WlSurface Surface, Surface Server) Place(CompositorTestHost host, int x)
    {
        Surface? server = null;
        void Capture(Surface surface) => server ??= surface;
        host.Compositor.SurfaceCreated += Capture;
        var surface = host.Client.Compositor.CreateSurface();
        FifoTests.Paint(host.Client, surface, 20);
        surface.Commit();
        host.PumpUntil(() => server is { Current.Width: 20 });
        host.Compositor.SurfaceCreated -= Capture;
        host.SurfaceScenes.Single(scene => scene.Surface == server).Tree.SetPosition(x, 10);
        return (surface, server!);
    }

    private static Func<(uint Refresh, ulong Sequence)?> Feedback(CompositorTestHost host, WlSurface surface)
    {
        (uint, ulong)? result = null;
        var feedback = host.Client.Presentation!.Feedback(surface);
        feedback.Presented += (_, e) => result = (e.Refresh, ((ulong)e.SeqHi << 32) | e.SeqLo);
        FifoTests.Paint(host.Client, surface, 20);
        surface.Commit();
        host.PumpToServer();
        return () => result;
    }

    [Fact]
    public void Each_surface_is_presented_by_its_own_output_with_its_refresh_and_sequence()
    {
        using var host = new CompositorTestHost();
        using var outputs = new TwoOutputs(host);
        var left = Place(host, 10);
        var right = Place(host, 200);
        outputs.FrameSlow();
        outputs.FrameFast();
        outputs.FrameFast();
        outputs.FrameFast();

        var leftFeedback = Feedback(host, left.Surface);
        var rightFeedback = Feedback(host, right.Surface);
        outputs.FrameFast();
        host.PumpToClient();
        Assert.Null(leftFeedback());
        Assert.NotNull(rightFeedback());

        outputs.FrameSlow();
        host.PumpUntil(() => leftFeedback() is not null);

        Assert.Same(host.Output, left.Server.MainOutput);
        Assert.Same(outputs.Fast, right.Server.MainOutput);
        Assert.Equal(host.Output.CurrentMode.RefreshIntervalNanoseconds, leftFeedback()!.Value.Refresh);
        Assert.Equal(host.Output.Sequence, leftFeedback()!.Value.Sequence);
        Assert.Equal(outputs.Fast.CurrentMode.RefreshIntervalNanoseconds, rightFeedback()!.Value.Refresh);
        Assert.Equal(outputs.Fast.Sequence, rightFeedback()!.Value.Sequence);
        Assert.NotEqual(leftFeedback()!.Value.Sequence, rightFeedback()!.Value.Sequence);
    }

    [Fact]
    public void A_surface_on_two_outputs_is_presented_once_by_its_main_output()
    {
        using var host = new CompositorTestHost();
        using var outputs = new TwoOutputs(host);
        var straddling = Place(host, 150);
        outputs.FrameSlow();
        outputs.FrameFast();
        Assert.Same(host.Output, straddling.Server.MainOutput);

        var presented = 0;
        var feedback = host.Client.Presentation!.Feedback(straddling.Surface);
        feedback.Presented += (_, _) => presented++;
        FifoTests.Paint(host.Client, straddling.Surface, 20);
        straddling.Surface.Commit();
        host.PumpToServer();

        outputs.FrameFast();
        host.PumpToClient();
        Assert.Equal(0, presented);

        outputs.FrameSlow();
        host.PumpUntil(() => presented == 1);

        host.SurfaceScenes.Single(scene => scene.Surface == straddling.Server).Tree.SetPosition(200, 10);
        outputs.FrameSlow();
        outputs.FrameFast();
        Assert.Same(outputs.Fast, straddling.Server.MainOutput);
    }

    [Fact]
    public void A_surface_drawn_on_no_output_keeps_its_feedback_until_the_next_commit_discards_it()
    {
        using var host = new CompositorTestHost();
        using var outputs = new TwoOutputs(host);
        var hidden = Place(host, 1000);
        outputs.FrameSlow();
        outputs.FrameFast();

        var discarded = false;
        var presented = false;
        var feedback = host.Client.Presentation!.Feedback(hidden.Surface);
        feedback.Presented += (_, _) => presented = true;
        feedback.Discarded += (_, _) => discarded = true;
        FifoTests.Paint(host.Client, hidden.Surface, 20);
        hidden.Surface.Commit();
        host.PumpToServer();
        outputs.FrameSlow();
        outputs.FrameFast();
        host.PumpToClient();
        Assert.False(presented);
        Assert.False(discarded);

        FifoTests.Paint(host.Client, hidden.Surface, 20);
        hidden.Surface.Commit();
        host.PumpUntil(() => discarded);
        Assert.False(presented);
    }

    [Fact]
    public void A_fifo_surface_is_latched_only_by_the_output_that_shows_it()
    {
        using var host = new CompositorTestHost();
        using var outputs = new TwoOutputs(host);
        using var manager = new FifoManager(host.Display, host.Compositor, host.Layout, host.Loop);
        var placed = Place(host, 10);
        outputs.FrameSlow();
        outputs.FrameFast();

        Basin.Desktop.Protocol.WpFifoManagerV1? proxy = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "wp_fifo_manager_v1")
            {
                proxy = registry.Bind<Basin.Desktop.Protocol.WpFifoManagerV1>(e.Name, 1);
            }
        };
        host.PumpToClient();
        var fifo = proxy!.GetFifo(placed.Surface);

        FifoTests.Paint(host.Client, placed.Surface, 30);
        fifo.SetBarrier();
        placed.Surface.Commit();
        host.PumpUntil(() => placed.Server.Current.Width == 30);

        FifoTests.Paint(host.Client, placed.Surface, 40);
        fifo.WaitBarrier();
        placed.Surface.Commit();
        host.PumpToServer();
        Assert.True(placed.Server.HasParkedCommits);

        outputs.FrameFast();
        outputs.FrameFast();
        outputs.FrameFast();
        Assert.Equal(30, placed.Server.Current.Width);

        outputs.FrameSlow();
        Assert.Equal(40, placed.Server.Current.Width);
        Assert.False(placed.Server.HasParkedCommits);
    }

    private sealed class TearingOutput() : OutputBase("tearing-test")
    {
        public bool Async { get; set; }

        public override bool LastFlipWasAsync => Async;

        protected override bool SupportsAdaptiveSync => true;

        protected override bool TestCommitCore(OutputState state) => true;

        protected override bool CommitCore(OutputState state) => true;
    }

    [Fact]
    public void An_async_flip_drops_vsync_and_a_variable_refresh_gives_a_version_one_client_zero()
    {
        using var host = new CompositorTestHost();
        var output = new TearingOutput();
        using (var state = new OutputState())
        {
            output.Commit(state.SetEnabled(true).SetMode(new OutputMode(160, 120, 144_000)).SetAdaptiveSync(true));
        }

        Assert.True(output.AdaptiveSync);
        output.Async = true;
        var placed = Place(host, 10);
        uint? refresh = null;
        uint? flags = null;
        var feedback = host.Client.Presentation!.Feedback(placed.Surface);
        feedback.Presented += (_, e) => (refresh, flags) = (e.Refresh, (uint)e.Flags);
        FifoTests.Paint(host.Client, placed.Surface, 20);
        placed.Surface.Commit();
        host.PumpToServer();

        host.Presentation.Presented(placed.Server, output, 1_000_000, 6_944_444, 7, PresentedFlags.Vsync | PresentedFlags.HwClock);
        host.PumpUntil(() => refresh is not null);

        Assert.Equal(0u, refresh);
        Assert.Equal((uint)PresentedFlags.HwClock, flags);
        output.Destroy();
    }
}
