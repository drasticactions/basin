using System.Runtime.InteropServices;
using Basin.Scene;
using Pixman;
using Xunit;

namespace Basin.Tests;

public sealed class OutputSchedulerTests
{
    [Fact]
    public void Damage_while_idle_repaints_immediately()
    {
        using var host = new CompositorTestHost();
        using var scheduler = new OutputScheduler(host.Loop, host.Output);
        var repaints = 0;
        scheduler.Repaint += () => repaints++;

        scheduler.ScheduleRepaint();
        scheduler.ScheduleRepaint();
        host.Loop.Dispatch(0);
        Assert.Equal(1, repaints);

        host.Loop.Dispatch(0);
        Assert.Equal(1, repaints);
    }

    [Fact]
    public void Repaint_during_flight_fires_at_the_deadline_after_the_flip()
    {
        using var host = new CompositorTestHost();
        using var scheduler = new OutputScheduler(host.Loop, host.Output);
        var repaints = 0;
        scheduler.Repaint += () => repaints++;

        scheduler.ScheduleRepaint();
        host.Loop.Dispatch(0);
        Assert.Equal(1, repaints);
        scheduler.NotifyCommitted();

        scheduler.ScheduleRepaint();
        host.Loop.Dispatch(0);
        Assert.Equal(1, repaints);

        host.Output.StepFrame();
        Assert.Equal(1, repaints);

        DispatchUntil(host, () => repaints == 2);
        Assert.Equal(2, repaints);
    }

    [Fact]
    public void A_repaint_never_fires_inside_the_call_that_asked_for_one()
    {
        using var host = new CompositorTestHost();
        using var scheduler = new OutputScheduler(host.Loop, host.Output);
        var repaints = 0;
        var asking = false;
        scheduler.Repaint += () =>
        {
            Assert.False(asking, "a repaint ran inside ScheduleRepaint, mid-mutation");
            repaints++;
        };

        asking = true;
        scheduler.ScheduleRepaint();
        asking = false;
        Assert.Equal(0, repaints);
        host.Loop.Dispatch(0);
        Assert.Equal(1, repaints);

        scheduler.NotifyCommitted();
        host.Output.StepFrame();
        asking = true;
        scheduler.ScheduleRepaint();
        asking = false;
        Assert.Equal(1, repaints);
        DispatchUntil(host, () => repaints == 2);
        Assert.Equal(2, repaints);
    }

    private static void DispatchUntil(CompositorTestHost host, Func<bool> done)
    {
        var deadline = Environment.TickCount64 + 2_000;
        while (!done() && Environment.TickCount64 < deadline)
        {
            host.Loop.Dispatch(5);
        }
    }

    [Fact]
    public void Vblank_prediction_starts_unknown_and_advances_across_flips()
    {
        using var host = new CompositorTestHost();
        using var scheduler = new OutputScheduler(host.Loop, host.Output);

        Assert.Equal(0, scheduler.PredictedVblankNanos);

        host.Output.StepFrame();
        var first = scheduler.PredictedVblankNanos;
        Assert.True(first > MonotonicClock.Nanos);

        host.Output.StepFrame();
        Assert.True(scheduler.PredictedVblankNanos >= first);

        var interval = 1_000_000_000_000L / host.Output.CurrentMode.RefreshMilliHz;
        Assert.True(scheduler.PredictedVblankNanos - MonotonicClock.Nanos <= interval);
    }

    [Fact]
    public void Outputs_at_different_rates_pace_independently()
    {
        using var host = new CompositorTestHost();
        var fast = host.Backend.CreateOutput(new OutputMode(160, 120, 144_000), manualFrameClock: false);
        var slow = host.Backend.CreateOutput(new OutputMode(160, 120, 60_000), manualFrameClock: false);

        using var fastScheduler = new OutputScheduler(host.Loop, fast);
        using var slowScheduler = new OutputScheduler(host.Loop, slow);
        var fastRepaints = 0;
        var slowRepaints = 0;

        fastScheduler.Repaint += () =>
        {
            fastRepaints++;
            fastScheduler.NotifyCommitted();
            fastScheduler.ScheduleRepaint();
        };
        slowScheduler.Repaint += () =>
        {
            slowRepaints++;
            slowScheduler.NotifyCommitted();
            slowScheduler.ScheduleRepaint();
        };

        using var fastState = new OutputState();
        using var slowState = new OutputState();
        fast.Commit(fastState.SetEnabled(true).SetMode(new OutputMode(160, 120, 144_000)));
        slow.Commit(slowState.SetEnabled(true).SetMode(new OutputMode(160, 120, 60_000)));

        fastScheduler.ScheduleRepaint();
        slowScheduler.ScheduleRepaint();

        var deadline = Environment.TickCount64 + 500;
        while (Environment.TickCount64 < deadline)
        {
            host.Loop.Dispatch(5);
        }

        Assert.True(fastRepaints > slowRepaints * 3 / 2,
            $"fast output should repaint faster: {fastRepaints} vs {slowRepaints}");
        fast.Destroy();
        slow.Destroy();
    }

    [Fact]
    public void An_idle_gap_between_flips_is_not_a_missed_vblank()
    {
        using var host = new CompositorTestHost();
        using var scheduler = new OutputScheduler(host.Loop, host.Output);
        scheduler.Repaint += () => { };

        scheduler.ScheduleRepaint();
        host.Loop.Dispatch(0);
        scheduler.NotifyCommitted();
        host.Output.StepFrame();

        System.Threading.Thread.Sleep(40);

        scheduler.ScheduleRepaint();
        host.Loop.Dispatch(0);
        scheduler.NotifyCommitted();
        host.Output.StepFrame();

        Assert.Equal(0, scheduler.MissedVblanks);
    }

    [Fact]
    public void A_cycle_lost_while_a_repaint_was_pending_is_a_missed_vblank()
    {
        using var host = new CompositorTestHost();
        using var scheduler = new OutputScheduler(host.Loop, host.Output);
        scheduler.Repaint += () => { };

        scheduler.ScheduleRepaint();
        host.Loop.Dispatch(0);
        scheduler.NotifyCommitted();
        scheduler.ScheduleRepaint();
        host.Output.StepFrame();

        System.Threading.Thread.Sleep(40);

        scheduler.NotifyCommitted();
        host.Output.StepFrame();

        Assert.True(scheduler.MissedVblanks > 0, "a lost cycle under a pending repaint must still count");
    }
}

public sealed class OutputLayerTests
{
    [Fact]
    public void Backends_without_plane_support_reject_every_layer()
    {
        using var host = new CompositorTestHost();
        using var state = new OutputState();
        var layer = new OutputLayer { DstBox = new Box(0, 0, 32, 32), Accepted = true };

        state.SetLayers([layer]);
        Assert.True(host.Output.TestCommit(state));
        Assert.False(layer.Accepted);

        layer.Accepted = true;
        Assert.True(host.Output.Commit(state));
        Assert.False(layer.Accepted);

        state.Clear();
        Assert.Null(state.Layers);
        Assert.Equal(OutputStateFields.None, state.Fields);
    }
}

public sealed class PlaneOffloadTests
{
    private sealed class PlaneOutput() : OutputBase("plane-test")
    {
        public Func<OutputLayer, int, bool>? Accept { get; set; }

        public Func<DrmFormat, ulong, bool, bool>? Scannable { get; set; }

        public override bool CanScanout(DrmFormat format, ulong modifier, bool overlay) =>
            Scannable?.Invoke(format, modifier, overlay) ?? true;

        public IReadOnlyList<OutputLayer>? LastCommittedLayers { get; private set; }

        protected override bool SupportsLayers => true;

        protected override bool TestCommitCore(OutputState state)
        {
            Judge(state);
            return true;
        }

        protected override bool CommitCore(OutputState state)
        {
            Judge(state);
            LastCommittedLayers = (state.Fields & OutputStateFields.Layers) != 0 ? state.Layers : null;
            return true;
        }

        private void Judge(OutputState state)
        {
            if ((state.Fields & OutputStateFields.Layers) == 0 || state.Layers is null)
            {
                return;
            }

            for (var i = 0; i < state.Layers.Count; i++)
            {
                state.Layers[i].Accepted = Accept?.Invoke(state.Layers[i], i) ?? false;
            }
        }
    }

    private static PlaneOutput LitPlaneOutput()
    {
        var output = new PlaneOutput();
        using var state = new OutputState();
        Assert.True(output.Commit(state.SetEnabled(true).SetMode(new OutputMode(160, 120, 60_000))));
        return output;
    }

    [Fact]
    public void Accepted_overlay_is_offloaded_and_skipped_by_the_compositor()
    {
        using var host = new CompositorTestHost();
        var output = LitPlaneOutput();
        using var sceneOutput = new SceneOutput(host.Scene, output) { OffloadEntryThreshold = 1 };
        using var swapchain = new Swapchain(new ShmAllocator(), 160, 120, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
        using var state = new OutputState();

        _ = new SceneRect(host.Scene.Root, 160, 120, new RenderColor(1, 0, 0, 1));
        var client = DirectScanoutTests.FakeClientBuffer(40, 40);
        var node = new SceneBuffer(host.Scene.Root);
        node.SetBuffer(client);
        node.SetPosition(10, 10);

        output.Accept = (_, _) => true;
        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state));
        Assert.Equal(1, sceneOutput.OffloadedLayers);
        Assert.Equal(1, sceneOutput.OffloadCommits);
        var committed = Assert.IsAssignableFrom<IReadOnlyList<OutputLayer>>(output.LastCommittedLayers);
        var layer = Assert.Single(committed);
        Assert.Same(client, layer.Buffer);
        Assert.Equal(new Box(10, 10, 40, 40), layer.DstBox);

        node.Destroy();
        client.Destroy();
        output.Destroy();
    }

    [Fact]
    public void Layer_demoted_by_the_real_commit_is_recomposited()
    {
        using var host = new CompositorTestHost();
        var output = LitPlaneOutput();
        using var sceneOutput = new SceneOutput(host.Scene, output) { OffloadEntryThreshold = 1 };
        using var swapchain = new Swapchain(new ShmAllocator(), 160, 120, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
        using var state = new OutputState();

        _ = new SceneRect(host.Scene.Root, 160, 120, new RenderColor(1, 0, 0, 1));
        var client = DirectScanoutTests.FakeClientBuffer(40, 40);
        var node = new SceneBuffer(host.Scene.Root);
        node.SetBuffer(client);
        node.SetPosition(10, 10);

        var judgeCalls = 0;
        output.Accept = (_, _) => judgeCalls++ == 0;
        var pending = false;
        sceneOutput.DamagePending += () => pending = true;

        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state));
        Assert.Equal(0, sceneOutput.OffloadedLayers);
        Assert.Equal(0, sceneOutput.OffloadCommits);
        Assert.True(pending);
        Assert.True(sceneOutput.NeedsRepaint);

        output.Accept = (_, _) => false;
        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state));
        Assert.Equal(0, sceneOutput.OffloadedLayers);
        Assert.Empty(output.LastCommittedLayers!);
        Assert.False(sceneOutput.NeedsRepaint);

        node.Destroy();
        client.Destroy();
        output.Destroy();
    }

    [Fact]
    public void Implicit_modifier_dmabufs_are_never_offered_as_layers()
    {
        using var host = new CompositorTestHost();
        var output = LitPlaneOutput();
        using var sceneOutput = new SceneOutput(host.Scene, output) { OffloadEntryThreshold = 1 };
        using var swapchain = new Swapchain(new ShmAllocator(), 160, 120, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
        using var state = new OutputState();

        _ = new SceneRect(host.Scene.Root, 160, 120, new RenderColor(1, 0, 0, 1));
        var client = DirectScanoutTests.FakeClientBuffer(40, 40, DrmFormatSet.ModifierInvalid);
        var node = new SceneBuffer(host.Scene.Root);
        node.SetBuffer(client);
        node.SetPosition(10, 10);

        var announcedAny = false;
        sceneOutput.OffloadCandidatesChanged += candidates => announcedAny |= candidates.Count > 0;
        output.Accept = (_, _) => true;
        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state));
        Assert.Equal(0, sceneOutput.OffloadedLayers);
        Assert.False(announcedAny);
        Assert.True(output.LastCommittedLayers is null or { Count: 0 });

        node.Destroy();
        client.Destroy();
        output.Destroy();
    }

    [Fact]
    public void Rejected_layer_demotes_accepted_layers_it_overlaps()
    {
        using var host = new CompositorTestHost();
        var output = LitPlaneOutput();
        using var sceneOutput = new SceneOutput(host.Scene, output) { OffloadEntryThreshold = 1 };
        using var swapchain = new Swapchain(new ShmAllocator(), 160, 120, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
        using var state = new OutputState();

        var bottomBuffer = DirectScanoutTests.FakeClientBuffer(40, 40);
        var bottom = new SceneBuffer(host.Scene.Root);
        bottom.SetBuffer(bottomBuffer);
        bottom.SetPosition(10, 10);

        var topBuffer = DirectScanoutTests.FakeClientBuffer(40, 40);
        var top = new SceneBuffer(host.Scene.Root);
        top.SetBuffer(topBuffer);
        top.SetPosition(30, 30);

        output.Accept = (_, index) => index == 0;
        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state));
        Assert.Equal(0, sceneOutput.OffloadedLayers);
        Assert.Empty(output.LastCommittedLayers!);

        top.Destroy();
        bottom.Destroy();
        topBuffer.Destroy();
        bottomBuffer.Destroy();
        output.Destroy();
    }

    [Fact]
    public void Losing_the_plane_repaints_the_vacated_region()
    {
        using var host = new CompositorTestHost();
        var output = LitPlaneOutput();
        using var sceneOutput = new SceneOutput(host.Scene, output) { OffloadEntryThreshold = 1 };
        using var swapchain = new Swapchain(new ShmAllocator(), 160, 120, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
        using var state = new OutputState();

        var client = DirectScanoutTests.FakeClientBuffer(40, 40);
        var node = new SceneBuffer(host.Scene.Root);
        node.SetBuffer(client);

        output.Accept = (_, _) => true;
        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state));
        Assert.Equal(1, sceneOutput.OffloadedLayers);

        Assert.False(sceneOutput.Commit(host.Renderer, swapchain, state));

        output.Accept = (_, _) => false;
        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state));
        Assert.Equal(0, sceneOutput.OffloadedLayers);
        Assert.Empty(output.LastCommittedLayers!);

        node.Destroy();
        client.Destroy();
        output.Destroy();
    }

    [Fact]
    public void Content_hanging_off_the_output_edge_is_cropped_onto_the_plane()
    {
        using var host = new CompositorTestHost();
        var output = LitPlaneOutput();
        using var sceneOutput = new SceneOutput(host.Scene, output) { OffloadEntryThreshold = 1 };
        using var swapchain = new Swapchain(new ShmAllocator(), 160, 120, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
        using var state = new OutputState();

        var client = DirectScanoutTests.FakeClientBuffer(40, 40);
        var node = new SceneBuffer(host.Scene.Root);
        node.SetBuffer(client);
        node.SetPosition(140, 100);

        output.Accept = (_, _) => true;
        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state));
        Assert.Equal(1, sceneOutput.OffloadedLayers);
        var layer = Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<OutputLayer>>(output.LastCommittedLayers));
        Assert.Equal(new Box(140, 100, 20, 20), layer.DstBox);
        Assert.Equal(new Box(0, 0, 20, 20), layer.SrcBox);

        node.SetPosition(10, 10);
        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state));
        Assert.Equal(1, sceneOutput.OffloadedLayers);
        layer = Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<OutputLayer>>(output.LastCommittedLayers));
        Assert.Equal(new Box(10, 10, 40, 40), layer.DstBox);
        Assert.True(layer.SrcBox.IsEmpty);

        node.Destroy();
        client.Destroy();
        output.Destroy();
    }

    [Fact]
    public void Content_cropped_off_the_top_left_samples_from_inside_the_buffer()
    {
        using var host = new CompositorTestHost();
        var output = LitPlaneOutput();
        using var sceneOutput = new SceneOutput(host.Scene, output) { OffloadEntryThreshold = 1 };
        using var swapchain = new Swapchain(new ShmAllocator(), 160, 120, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
        using var state = new OutputState();

        var client = DirectScanoutTests.FakeClientBuffer(40, 40);
        var node = new SceneBuffer(host.Scene.Root);
        node.SetBuffer(client);
        node.SetPosition(-15, -10);

        output.Accept = (_, _) => true;
        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state));
        var layer = Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<OutputLayer>>(output.LastCommittedLayers));
        Assert.Equal(new Box(0, 0, 25, 30), layer.DstBox);
        Assert.Equal(new Box(15, 10, 25, 30), layer.SrcBox);

        node.Destroy();
        client.Destroy();
        output.Destroy();
    }

    [Fact]
    public void A_crop_at_fractional_scale_keeps_its_fractional_source()
    {
        using var host = new CompositorTestHost();
        var output = new PlaneOutput();
        using (var lit = new OutputState())
        {
            Assert.True(output.Commit(lit.SetEnabled(true).SetMode(new OutputMode(160, 120, 60_000)).SetScale(1.25)));
        }

        using var sceneOutput = new SceneOutput(host.Scene, output) { OffloadEntryThreshold = 1 };
        using var swapchain = new Swapchain(new ShmAllocator(), 160, 120, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
        using var state = new OutputState();

        var client = DirectScanoutTests.FakeClientBuffer(40, 40);
        var node = new SceneBuffer(host.Scene.Root);
        node.SetBuffer(client);
        node.SetPosition(-15, -10);

        output.Accept = (_, _) => true;
        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state));
        var layer = Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<OutputLayer>>(output.LastCommittedLayers));
        Assert.Equal(new Box(0, 0, 31, 38), layer.DstBox);
        Assert.Equal(19 * 40.0 / 50, layer.SrcBox.X, 12);
        Assert.Equal(12 * 40.0 / 50, layer.SrcBox.Y, 12);
        Assert.Equal(31 * 40.0 / 50, layer.SrcBox.Width, 12);
        Assert.Equal(38 * 40.0 / 50, layer.SrcBox.Height, 12);

        node.Destroy();
        client.Destroy();
        output.Destroy();
    }

    [Fact]
    public void An_overlap_from_above_takes_only_the_part_it_covers()
    {
        using var host = new CompositorTestHost();
        var output = LitPlaneOutput();
        using var sceneOutput = new SceneOutput(host.Scene, output) { OffloadEntryThreshold = 1 };
        using var swapchain = new Swapchain(new ShmAllocator(), 160, 120, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
        using var state = new OutputState();

        var client = DirectScanoutTests.FakeClientBuffer(60, 40);
        var node = new SceneBuffer(host.Scene.Root);
        node.SetBuffer(client);
        node.SetPosition(10, 10);

        var cover = new SceneRect(host.Scene.Root, 40, 40, new RenderColor(0, 1, 0, 1));
        cover.SetPosition(50, 10);

        output.Accept = (_, _) => true;
        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state));
        Assert.Equal(1, sceneOutput.OffloadedLayers);
        var layer = Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<OutputLayer>>(output.LastCommittedLayers));
        Assert.Equal(new Box(10, 10, 40, 40), layer.DstBox);
        Assert.Equal(new Box(0, 0, 40, 40), layer.SrcBox);

        cover.Destroy();
        node.Destroy();
        client.Destroy();
        output.Destroy();
    }

    [Fact]
    public void An_overlap_that_leaves_no_rectangle_declines()
    {
        using var host = new CompositorTestHost();
        var output = LitPlaneOutput();
        using var sceneOutput = new SceneOutput(host.Scene, output) { OffloadEntryThreshold = 1 };
        using var swapchain = new Swapchain(new ShmAllocator(), 160, 120, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
        using var state = new OutputState();

        var client = DirectScanoutTests.FakeClientBuffer(60, 60);
        var node = new SceneBuffer(host.Scene.Root);
        node.SetBuffer(client);
        node.SetPosition(10, 10);

        var cover = new SceneRect(host.Scene.Root, 20, 20, new RenderColor(0, 1, 0, 1));
        cover.SetPosition(30, 30);

        output.Accept = (_, _) => true;
        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state));
        Assert.Equal(0, sceneOutput.OffloadedLayers);
        Assert.True(output.LastCommittedLayers is null or { Count: 0 });

        cover.Destroy();
        node.Destroy();
        client.Destroy();
        output.Destroy();
    }

    [Fact]
    public void A_composited_surface_in_a_higher_tree_demotes_the_window_under_it()
    {
        using var host = new CompositorTestHost();
        var output = LitPlaneOutput();
        using var sceneOutput = new SceneOutput(host.Scene, output) { OffloadEntryThreshold = 1 };
        using var swapchain = new Swapchain(new ShmAllocator(), 160, 120, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
        using var state = new OutputState();

        var windowTree = new SceneTree(host.Scene.Root);
        var aboveLayers = new SceneTree(host.Scene.Root);

        var client = DirectScanoutTests.FakeClientBuffer(160, 120);
        var window = new SceneBuffer(windowTree);
        window.SetBuffer(client);

        var overlay = new SceneRect(aboveLayers, 160, 120, new RenderColor(0, 1, 0, 1));

        output.Accept = (_, _) => true;
        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state));
        Assert.Equal(0, sceneOutput.OffloadedLayers);
        Assert.True(output.LastCommittedLayers is null or { Count: 0 });

        overlay.Destroy();
        window.Destroy();
        client.Destroy();
        aboveLayers.Destroy();
        windowTree.Destroy();
        output.Destroy();
    }

    [Fact]
    public void A_plane_that_changes_shape_repaints_the_strip_it_gave_up()
    {
        using var host = new CompositorTestHost();
        var output = LitPlaneOutput();
        using var sceneOutput = new SceneOutput(host.Scene, output) { OffloadEntryThreshold = 1 };
        using var swapchain = new Swapchain(new ShmAllocator(), 160, 120, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
        using var state = new OutputState();

        var client = DirectScanoutTests.FakeClientBuffer(60, 40);
        var node = new SceneBuffer(host.Scene.Root);
        node.SetBuffer(client);
        node.SetPosition(10, 10);
        var cover = new SceneRect(host.Scene.Root, 40, 40, new RenderColor(0, 1, 0, 1));
        cover.SetPosition(50, 10);

        output.Accept = (_, _) => true;
        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state));
        Assert.Equal(new Box(10, 10, 40, 40), Assert.Single(output.LastCommittedLayers!).DstBox);

        cover.SetPosition(60, 10);
        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state));
        Assert.Equal(new Box(10, 10, 50, 40), Assert.Single(output.LastCommittedLayers!).DstBox);
        var damage = state.Damage!;
        Assert.Equal(PixmanRegionOverlap.In, damage.Contains(new PixmanBox32(50, 10, 60, 50)));

        cover.Destroy();
        node.Destroy();
        client.Destroy();
        output.Destroy();
    }

    [Theory]
    [InlineData("pixman")]
    [InlineData("gl")]
    public void A_partial_plane_leaves_everything_beneath_it_composited(string renderer)
    {
        CompositorTestHost.SkipUnlessRunnable(renderer);
        using var host = new CompositorTestHost(renderer: renderer);
        var output = LitPlaneOutput();
        using var sceneOutput = new SceneOutput(host.Scene, output) { OffloadEntryThreshold = 1 };
        using var swapchain = new Swapchain(new ShmAllocator(), 160, 120, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
        using var state = new OutputState();
        var oracle = new MemoryBuffer(160, 120, DrmFormat.Xrgb8888);

        var under = new SceneRect(host.Scene.Root, 160, 120, new RenderColor(0.1f, 0.2f, 0.9f, 1f));
        var client = DirectScanoutTests.FakeClientBuffer(60, 40);
        var node = new SceneBuffer(host.Scene.Root);
        node.SetBuffer(client);
        node.SetPosition(10, 10);

        var cover = new SceneRect(host.Scene.Root, 40, 40, new RenderColor(0, 1, 0, 0.5f));
        cover.SetPosition(50, 10);

        try
        {
            if (renderer is "gl")
            {
                using var imported = host.Renderer.ImportTexture(client);
                Assert.SkipWhen(imported is not null, "the renderer imports the memfd stand-in, so the oracle can draw the client");
            }

            output.Accept = (_, _) => true;
            Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state));
            var layer = Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<OutputLayer>>(output.LastCommittedLayers));
            Assert.Equal(new Box(10, 10, 40, 40), layer.DstBox);

            host.Scene.Render(host.Renderer, oracle, RenderColor.Black);
            AssertSameEverywhere(oracle, state.Buffer!);
        }
        finally
        {
            cover.Destroy();
            node.Destroy();
            under.Destroy();
            client.Destroy();
            oracle.Destroy();
            output.Destroy();
        }
    }

    private static unsafe void AssertSameEverywhere(IBuffer oracle, IBuffer primary)
    {
        Assert.True(oracle.BeginDataAccess(BufferDataAccess.Read, out var o));
        Assert.True(primary.BeginDataAccess(BufferDataAccess.Read, out var p));
        try
        {
            for (var y = 0; y < oracle.Height; y++)
            {
                for (var x = 0; x < oracle.Width; x++)
                {
                    var expected = *(uint*)(o.Data + (y * o.Stride) + (x * 4)) & 0xFFFFFFu;
                    var actual = *(uint*)(p.Data + (y * p.Stride) + (x * 4)) & 0xFFFFFFu;
                    if (expected != actual)
                    {
                        Assert.Fail($"({x},{y}): full repaint {expected:X6}, composited {actual:X6}");
                    }
                }
            }
        }
        finally
        {
            oracle.EndDataAccess();
            primary.EndDataAccess();
        }
    }

    [Theory]
    [InlineData("shm", PlaneDeclineReason.NoDmabuf)]
    [InlineData("implicit", PlaneDeclineReason.ImplicitModifier)]
    [InlineData("unscannable", PlaneDeclineReason.UnscannableLayout)]
    [InlineData("clipped", PlaneDeclineReason.Clipped)]
    [InlineData("covered", PlaneDeclineReason.CoveredFromAbove)]
    [InlineData("refused", PlaneDeclineReason.BackendRefused)]
    public void Each_rule_reports_the_node_it_turned_away(string setup, PlaneDeclineReason expected)
    {
        using var host = new CompositorTestHost();
        var output = LitPlaneOutput();
        using var sceneOutput = new SceneOutput(host.Scene, output) { OffloadEntryThreshold = 1 };
        using var swapchain = new Swapchain(new ShmAllocator(), 160, 120, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
        using var state = new OutputState();

        var modifier = setup == "implicit" ? DrmFormatSet.ModifierInvalid : DrmFormatSet.ModifierLinear;
        var client = DirectScanoutTests.FakeClientBuffer(40, 40, modifier);
        var shm = new MemoryBuffer(40, 40, DrmFormat.Xrgb8888);
        var clipTree = new SceneTree(host.Scene.Root);
        var node = new SceneBuffer(clipTree);
        node.SetPosition(10, 10);
        SceneRect? cover = null;
        try
        {
            if (setup == "shm")
            {
                node.SetBuffer(shm);
            }
            else
            {
                node.SetBuffer(client);
                if (setup == "clipped")
                {
                    clipTree.ClipBox = new Box(0, 0, 30, 30);
                }
                else if (setup == "covered")
                {
                    cover = new SceneRect(host.Scene.Root, 20, 20, new RenderColor(0, 1, 0, 1));
                    cover.SetPosition(20, 20);
                }
            }

            output.Accept = (_, _) => setup != "refused";
            if (setup == "unscannable")
            {
                output.Scannable = (_, _, _) => false;
            }

            RunAndAssert();
        }
        finally
        {
            cover?.Destroy();
            node.SetBuffer(null);
            node.Destroy();
            clipTree.Destroy();
            shm.Destroy();
            client.Destroy();
            output.Destroy();
        }

        return;

        void RunAndAssert()
        {
            _ = sceneOutput.Commit(host.Renderer, swapchain, state);
            Assert.True(sceneOutput.DeclinedFor(expected) > 0, $"expected a {expected} decline");
            var index = -1;
            for (var i = 0; i < sceneOutput.DeclinedCandidates.Count; i++)
            {
                if (ReferenceEquals(sceneOutput.DeclinedCandidates[i], node))
                {
                    index = i;
                }
            }

            Assert.True(index >= 0, "the declined node must be reported, not only counted");
            Assert.Equal(expected, sceneOutput.DeclineReasons[index]);
        }
    }

    [Fact]
    public void Content_that_never_could_scan_out_is_counted_but_not_listed()
    {
        using var host = new CompositorTestHost();
        var output = LitPlaneOutput();
        using var sceneOutput = new SceneOutput(host.Scene, output) { OffloadEntryThreshold = 1 };
        using var swapchain = new Swapchain(new ShmAllocator(), 160, 120, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
        using var state = new OutputState();

        var rect = new SceneRect(host.Scene.Root, 40, 40, new RenderColor(1, 0, 0, 1));
        rect.SetPosition(10, 10);
        _ = sceneOutput.Commit(host.Renderer, swapchain, state);

        Assert.True(sceneOutput.DeclinedFor(PlaneDeclineReason.NotABuffer) > 0);
        Assert.Empty(sceneOutput.DeclinedCandidates);

        rect.Destroy();
        output.Destroy();
    }

    [Fact]
    public void New_content_on_a_plane_flips_it_without_repainting_anything()
    {
        using var host = new CompositorTestHost();
        var output = LitPlaneOutput();
        using var sceneOutput = new SceneOutput(host.Scene, output) { OffloadEntryThreshold = 1 };
        using var swapchain = new Swapchain(new ShmAllocator(), 160, 120, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
        using var state = new OutputState();

        var under = new SceneRect(host.Scene.Root, 160, 120, new RenderColor(0.1f, 0.2f, 0.9f, 1f));
        var client = DirectScanoutTests.FakeClientBuffer(40, 40);
        var node = new SceneBuffer(host.Scene.Root);
        node.SetBuffer(client);
        node.SetPosition(10, 10);

        output.Accept = (_, _) => true;
        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state));
        Assert.Equal(1, sceneOutput.OffloadedLayers);
        var composedBefore = sceneOutput.ComposedCommits;

        node.NotifyContentChanged();
        Assert.True(sceneOutput.Ring.IsEmpty, "a plane's own content must not damage the composited buffer");
        Assert.True(sceneOutput.NeedsRepaint, "the plane still needs the new buffer");

        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state));
        Assert.Equal(1, sceneOutput.PlaneOnlyCommits);
        Assert.Equal(composedBefore, sceneOutput.ComposedCommits);
        Assert.False(sceneOutput.NeedsRepaint);

        under.SetPosition(1, 0);
        Assert.False(sceneOutput.Ring.IsEmpty);
        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state));
        Assert.Equal(composedBefore + 1, sceneOutput.ComposedCommits);
        Assert.Equal(1, sceneOutput.PlaneOnlyCommits);

        node.Destroy();
        under.Destroy();
        client.Destroy();
        output.Destroy();
    }

    [Fact]
    public void A_partly_planed_node_still_repaints_the_part_it_composites()
    {
        using var host = new CompositorTestHost();
        var output = LitPlaneOutput();
        using var sceneOutput = new SceneOutput(host.Scene, output) { OffloadEntryThreshold = 1 };
        using var swapchain = new Swapchain(new ShmAllocator(), 160, 120, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
        using var state = new OutputState();

        var client = DirectScanoutTests.FakeClientBuffer(60, 40);
        var node = new SceneBuffer(host.Scene.Root);
        node.SetBuffer(client);
        node.SetPosition(10, 10);
        var cover = new SceneRect(host.Scene.Root, 40, 40, new RenderColor(0, 1, 0, 0.5f));
        cover.SetPosition(50, 10);

        output.Accept = (_, _) => true;
        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state));
        Assert.Equal(new Box(10, 10, 40, 40), Assert.Single(output.LastCommittedLayers!).DstBox);

        var composedBefore = sceneOutput.ComposedCommits;
        node.NotifyContentChanged();
        Assert.False(sceneOutput.Ring.IsEmpty);
        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state));
        Assert.Equal(composedBefore + 1, sceneOutput.ComposedCommits);
        Assert.Equal(0, sceneOutput.PlaneOnlyCommits);

        cover.Destroy();
        node.Destroy();
        client.Destroy();
        output.Destroy();
    }

    [Fact]
    public void A_client_cycling_its_buffers_imports_each_one_once()
    {
        using var host = new CompositorTestHost();
        var counting = new CountingRenderer(host.Renderer);
        using var sceneOutput = new SceneOutput(host.Scene, host.Output);
        using var swapchain = new Swapchain(new ShmAllocator(), 160, 120, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
        using var state = new OutputState();

        var node = new SceneBuffer(host.Scene.Root);
        var pool = new[]
        {
            new MemoryBuffer(40, 40, DrmFormat.Xrgb8888),
            new MemoryBuffer(40, 40, DrmFormat.Xrgb8888),
        };

        for (var frame = 0; frame < 10; frame++)
        {
            node.SetBuffer(pool[frame % pool.Length]);
            node.NotifyContentChanged();
            _ = sceneOutput.Commit(counting, swapchain, state);
        }

        Assert.Equal(pool.Length, counting.Imports);

        node.SetBuffer(null);
        node.Destroy();
        foreach (var buffer in pool)
        {
            buffer.Destroy();
        }
    }

    [Fact]
    public void A_destroyed_buffer_takes_its_texture_with_it()
    {
        using var host = new CompositorTestHost();
        var counting = new CountingRenderer(host.Renderer);
        using var sceneOutput = new SceneOutput(host.Scene, host.Output);
        using var swapchain = new Swapchain(new ShmAllocator(), 160, 120, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
        using var state = new OutputState();

        var buffer = new MemoryBuffer(40, 40, DrmFormat.Xrgb8888);
        var node = new SceneBuffer(host.Scene.Root);
        node.SetBuffer(buffer);
        node.SetPosition(10, 10);

        _ = sceneOutput.Commit(counting, swapchain, state);
        Assert.Equal(1, counting.Imports);

        buffer.Destroy();
        node.NotifyContentChanged();
        _ = sceneOutput.Commit(counting, swapchain, state);
        Assert.Equal(2, counting.Imports);

        node.SetBuffer(null);
        node.Destroy();
    }

    private sealed class CountingRenderer(IRenderer inner) : IRenderer
    {
        public int Imports { get; private set; }

        public ITexture? ImportTexture(IBuffer buffer)
        {
            Imports++;
            return inner.ImportTexture(buffer);
        }

        public IRenderPass BeginBufferPass(IBuffer target, in RenderPassOptions options) =>
            inner.BeginBufferPass(target, options);

        public void Dispose()
        {
        }
    }

    [Fact]
    public void An_offloaded_node_hands_its_fence_to_the_plane()
    {
        using var host = new CompositorTestHost();
        var output = LitPlaneOutput();
        using var sceneOutput = new SceneOutput(host.Scene, output) { OffloadEntryThreshold = 1 };
        using var swapchain = new Swapchain(new ShmAllocator(), 160, 120, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
        using var state = new OutputState();

        var client = DirectScanoutTests.FakeClientBuffer(40, 40);
        var node = new SceneBuffer(host.Scene.Root);
        node.SetBuffer(client);
        node.SetPosition(10, 10);

        var fence = memfd_create("layer-fence-test", 0);
        Assert.True(fence >= 0);
        node.AcquireFenceFd = fence;

        output.Accept = (_, _) => true;
        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state));
        var layer = Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<OutputLayer>>(output.LastCommittedLayers));
        Assert.Equal(fence, layer.InFenceFd);

        node.AcquireFenceFd = -1;
        node.SetPosition(11, 10);
        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state));
        layer = Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<OutputLayer>>(output.LastCommittedLayers));
        Assert.Equal(-1, layer.InFenceFd);

        close(fence);
        node.Destroy();
        client.Destroy();
        output.Destroy();
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int memfd_create(string name, uint flags);

    [DllImport("libc")]
    private static extern int close(int fd);

    [Fact]
    public void A_rectangle_that_keeps_changing_never_reaches_a_plane()
    {
        using var host = new CompositorTestHost();
        var output = LitPlaneOutput();
        using var sceneOutput = new SceneOutput(host.Scene, output);
        using var swapchain = new Swapchain(new ShmAllocator(), 160, 120, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
        using var state = new OutputState();

        var client = DirectScanoutTests.FakeClientBuffer(40, 40);
        var node = new SceneBuffer(host.Scene.Root);
        node.SetBuffer(client);
        output.Accept = (_, _) => true;

        for (var frame = 0; frame < 8; frame++)
        {
            node.SetPosition(10 + frame, 10);
            _ = sceneOutput.Commit(host.Renderer, swapchain, state);
            Assert.Equal(0, sceneOutput.OffloadedLayers);
        }

        Assert.True(sceneOutput.DeclinedFor(PlaneDeclineReason.Settling) > 0);

        for (var frame = 0; frame < sceneOutput.OffloadEntryThreshold; frame++)
        {
            node.NotifyContentChanged();
            _ = sceneOutput.Commit(host.Renderer, swapchain, state);
        }

        Assert.Equal(1, sceneOutput.OffloadedLayers);

        node.Destroy();
        client.Destroy();
        output.Destroy();
    }

    [Fact]
    public void Software_cursor_disables_offload()
    {
        using var host = new CompositorTestHost();
        var output = LitPlaneOutput();
        using var sceneOutput = new SceneOutput(host.Scene, output) { OffloadEntryThreshold = 1 };
        using var swapchain = new Swapchain(new ShmAllocator(), 160, 120, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
        using var state = new OutputState();
        var cursorImage = new MemoryBuffer(16, 16, DrmFormat.Argb8888);

        var client = DirectScanoutTests.FakeClientBuffer(40, 40);
        var node = new SceneBuffer(host.Scene.Root);
        node.SetBuffer(client);

        output.Accept = (_, _) => true;
        sceneOutput.SetSoftwareCursor(cursorImage, 0, 0);
        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state));
        Assert.Equal(0, sceneOutput.OffloadedLayers);

        sceneOutput.SetSoftwareCursor(null, 0, 0);
        node.Destroy();
        client.Destroy();
        cursorImage.Destroy();
        output.Destroy();
    }
}

public sealed class DirectScanoutTests
{
    [DllImport("libc", SetLastError = true)]
    private static extern int memfd_create(string name, uint flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int ftruncate(int fd, long length);

    internal static DmabufBuffer FakeClientBuffer(int width, int height, ulong modifier = DrmFormatSet.ModifierLinear)
    {
        var fd = memfd_create("scanout-test", 1);
        Assert.True(fd >= 0);
        Assert.Equal(0, ftruncate(fd, width * height * 4));
        var attributes = new DmabufAttributes
        {
            Width = width,
            Height = height,
            Format = DrmFormat.Xrgb8888,
            Modifier = modifier,
            PlaneCount = 1,
        };
        attributes.Fds[0] = fd;
        attributes.Strides[0] = (uint)width * 4;

        return new DmabufBuffer(attributes);
    }

    [Fact]
    public void Fullscreen_dmabuf_enters_scanout_with_hysteresis_and_leaves_cleanly()
    {
        using var host = new CompositorTestHost();
        using var sceneOutput = new SceneOutput(host.Scene, host.Output);
        using var swapchain = new Swapchain(new ShmAllocator(), 160, 120, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
        using var state = new OutputState();

        var client = FakeClientBuffer(160, 120);
        using var clientGuard = new DeferDestroy(client);
        var node = new SceneBuffer(host.Scene.Root) { IsOpaque = true };
        node.SetBuffer(client);

        Surface? announced = null;
        var announcements = 0;
        sceneOutput.ScanoutCandidateChanged += surface =>
        {
            announced = surface;
            announcements++;
        };

        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state));
        Assert.False(sceneOutput.IsDirectScanout);
        Assert.Equal(1, announcements);

        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state));
        Assert.True(sceneOutput.IsDirectScanout);
        Assert.Same(client, state.Buffer);
        Assert.Equal(1, sceneOutput.ScanoutCommits);

        var overlay = new SceneRect(host.Scene.Root, 10, 10, new RenderColor(1, 0, 0, 1));
        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state));
        Assert.False(sceneOutput.IsDirectScanout);
        Assert.NotSame(client, state.Buffer);
        Assert.Null(announced);

        overlay.Destroy();
        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state));
        Assert.False(sceneOutput.IsDirectScanout);
        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state));
        Assert.True(sceneOutput.IsDirectScanout);

        node.Destroy();
    }

    [Fact]
    public void Undersized_or_cropped_buffers_never_scan_out()
    {
        using var host = new CompositorTestHost();
        using var sceneOutput = new SceneOutput(host.Scene, host.Output) { ScanoutEntryThreshold = 1 };
        using var swapchain = new Swapchain(new ShmAllocator(), 160, 120, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
        using var state = new OutputState();

        var small = FakeClientBuffer(80, 60);
        using var smallGuard = new DeferDestroy(small);
        var node = new SceneBuffer(host.Scene.Root) { IsOpaque = true };
        node.SetBuffer(small);
        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state));
        Assert.False(sceneOutput.IsDirectScanout);

        var full = FakeClientBuffer(160, 120);
        using var fullGuard = new DeferDestroy(full);
        node.SetBuffer(full);
        node.SourceBox = new Box(1, 1, 100, 100);
        node.DestinationWidth = 160;
        node.DestinationHeight = 120;
        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state));
        Assert.False(sceneOutput.IsDirectScanout);

        node.Destroy();
    }

    [Fact]
    public void A_client_fence_an_output_cannot_wait_on_keeps_the_buffer_off_the_plane()
    {
        using var host = new CompositorTestHost();
        Assert.False(host.Output.SupportsInFence);
        using var sceneOutput = new SceneOutput(host.Scene, host.Output) { ScanoutEntryThreshold = 1 };
        using var swapchain = new Swapchain(new ShmAllocator(), 160, 120, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
        using var state = new OutputState();

        var client = FakeClientBuffer(160, 120);
        using var clientGuard = new DeferDestroy(client);
        var node = new SceneBuffer(host.Scene.Root) { IsOpaque = true };
        node.SetBuffer(client);

        var fence = memfd_create("scanout-fence-test", 0);
        Assert.True(fence >= 0);
        try
        {
            node.AcquireFenceFd = fence;
            node.NotifyContentChanged();
            Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state));
            Assert.False(sceneOutput.IsDirectScanout);
            Assert.NotSame(client, state.Buffer);

            node.AcquireFenceFd = -1;
            node.NotifyContentChanged();
            Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state));
            Assert.True(sceneOutput.IsDirectScanout);
            Assert.Same(client, state.Buffer);
        }
        finally
        {
            node.AcquireFenceFd = -1;
            close(fence);
        }

        node.Destroy();
    }

    [Fact]
    public void An_output_that_can_wait_scans_the_fenced_buffer_out_and_gets_the_fence()
    {
        using var host = new CompositorTestHost();
        var output = new WaitingOutput();
        using var lit = new OutputState();
        Assert.True(output.Commit(lit.SetEnabled(true).SetMode(new OutputMode(160, 120, 60_000))));
        using var sceneOutput = new SceneOutput(host.Scene, output) { ScanoutEntryThreshold = 1 };
        using var swapchain = new Swapchain(new ShmAllocator(), 160, 120, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
        using var state = new OutputState();

        var client = FakeClientBuffer(160, 120);
        using var clientGuard = new DeferDestroy(client);
        var node = new SceneBuffer(host.Scene.Root) { IsOpaque = true };
        node.SetBuffer(client);

        var fence = memfd_create("waiting-fence-test", 0);
        Assert.True(fence >= 0);
        try
        {
            node.AcquireFenceFd = fence;
            node.NotifyContentChanged();
            Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state));
            Assert.True(sceneOutput.IsDirectScanout);
            Assert.Same(client, state.Buffer);
            Assert.Equal(fence, output.LastInFenceFd);
        }
        finally
        {
            node.AcquireFenceFd = -1;
            close(fence);
        }

        node.Destroy();
        output.Destroy();
    }

    private sealed class WaitingOutput() : OutputBase("waiting-test")
    {
        public int LastInFenceFd { get; private set; } = -1;

        public override bool SupportsInFence => true;

        protected override bool TestCommitCore(OutputState state) => true;

        protected override bool CommitCore(OutputState state)
        {
            LastInFenceFd = (state.Fields & OutputStateFields.InFence) != 0 ? state.InFenceFd : -1;
            return true;
        }
    }

    [Fact]
    public void An_output_that_cannot_wait_refuses_a_commit_carrying_a_fence()
    {
        using var host = new CompositorTestHost();
        using var state = new OutputState();

        var fence = memfd_create("commit-fence-test", 0);
        Assert.True(fence >= 0);
        Assert.False(host.Output.TestCommit(state.SetInFence(fence)));

        state.Clear();
        Assert.True(host.Output.TestCommit(state));

        close(fence);
    }

    [DllImport("libc")]
    private static extern int close(int fd);

    private sealed class DeferDestroy(BufferBase buffer) : IDisposable
    {
        public void Dispose() => buffer.Destroy();
    }
}
