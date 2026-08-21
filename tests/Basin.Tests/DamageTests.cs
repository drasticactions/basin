using Basin.Scene;
using Pixman;
using Xunit;

namespace Basin.Tests;

public sealed class DamageRingTests
{
    [Fact]
    public void Buffer_age_accumulates_history()
    {
        using var ring = new DamageRing(100, 100);
        using var result = new PixmanRegion32();

        ring.GetBufferDamage(1, result);
        Assert.Equal((0, 0, 100, 100), Extents(result));
        ring.Commit();

        ring.Add(new Box(10, 10, 5, 5));
        ring.Commit();
        ring.Add(new Box(50, 50, 5, 5));

        ring.GetBufferDamage(1, result);
        Assert.Equal((50, 50, 55, 55), Extents(result));

        ring.GetBufferDamage(2, result);
        Assert.Equal((10, 10, 55, 55), Extents(result));

        ring.GetBufferDamage(0, result);
        Assert.Equal((0, 0, 100, 100), Extents(result));
        ring.GetBufferDamage(9, result);
        Assert.Equal((0, 0, 100, 100), Extents(result));
    }

    [Fact]
    public void Resize_invalidates_everything()
    {
        using var ring = new DamageRing(100, 100);
        ring.Commit();
        Assert.True(ring.IsEmpty);
        ring.Resize(200, 100);
        Assert.False(ring.IsEmpty);
    }

    [Fact]
    public void Damage_is_clamped_to_the_output()
    {
        using var ring = new DamageRing(100, 100);
        ring.Commit();
        ring.Add(new Box(90, 90, 50, 50));
        using var result = new PixmanRegion32();
        ring.GetBufferDamage(1, result);
        Assert.Equal((90, 90, 100, 100), Extents(result));
    }

    private static (int, int, int, int) Extents(PixmanRegion32 region)
    {
        var extents = region.Extents;
        return (extents.X1, extents.Y1, extents.X2, extents.Y2);
    }
}

public sealed class SceneDamageTests
{
    private static (Scene.Scene Scene, List<Box> Damage) Tracked()
    {
        var scene = new Scene.Scene();
        var boxes = new List<Box>();
        scene.Damaged += (_, box) => boxes.Add(box);
        return (scene, boxes);
    }

    [Fact]
    public void Moving_a_node_damages_both_positions()
    {
        var (scene, damage) = Tracked();
        var rect = new SceneRect(scene.Root, 10, 10, RenderColor.Black);
        damage.Clear();

        rect.SetPosition(30, 40);
        Assert.Equal(2, damage.Count);
        Assert.Equal(new Box(0, 0, 10, 10), damage[0]);
        Assert.Equal(new Box(30, 40, 10, 10), damage[1]);
        scene.Root.Destroy();
    }

    [Fact]
    public void Toggling_visibility_damages()
    {
        var (scene, damage) = Tracked();
        var rect = new SceneRect(scene.Root, 10, 10, RenderColor.Black);
        damage.Clear();

        rect.Enabled = false;
        Assert.Single(damage);
        rect.Enabled = true;
        Assert.Equal(2, damage.Count);
        scene.Root.Destroy();
    }

    [Fact]
    public void Disabled_subtrees_produce_no_damage()
    {
        var (scene, damage) = Tracked();
        var tree = new SceneTree(scene.Root);
        var rect = new SceneRect(tree, 10, 10, RenderColor.Black);
        tree.Enabled = false;
        damage.Clear();

        rect.SetPosition(50, 50);
        rect.Color = new RenderColor(1, 0, 0, 1);
        Assert.Empty(damage);
        scene.Root.Destroy();
    }

    [Fact]
    public void Same_size_buffer_swap_does_not_damage()
    {
        var (scene, damage) = Tracked();
        var node = new SceneBuffer(scene.Root);
        var a = new MemoryBuffer(16, 16, DrmFormat.Xrgb8888);
        var b = new MemoryBuffer(16, 16, DrmFormat.Xrgb8888);
        node.SetBuffer(a);
        damage.Clear();

        node.SetBuffer(b);
        Assert.Empty(damage);

        using var region = new PixmanRegion32();
        region.Reset(new PixmanBox32(2, 3, 6, 8));
        node.NotifyContentChanged(region);
        Assert.Single(damage);
        Assert.Equal(new Box(2, 3, 4, 5), damage[0]);

        scene.Root.Destroy();
        a.Destroy();
        b.Destroy();
    }

    [Fact]
    public void Restacking_damages_the_node()
    {
        var (scene, damage) = Tracked();
        _ = new SceneRect(scene.Root, 10, 10, RenderColor.Black);
        var top = new SceneRect(scene.Root, 10, 10, new RenderColor(1, 0, 0, 1));
        damage.Clear();

        top.LowerToBottom();
        Assert.Single(damage);
        scene.Root.Destroy();
    }
}

public sealed class CommitRefusalTests
{
    private sealed class RefusingOutput(string name) : OutputBase(name)
    {
        public bool Refuse { get; set; }

        public int AppliedFrames { get; private set; }

        protected override bool TestCommitCore(OutputState state) => true;

        protected override bool CommitCore(OutputState state)
        {
            if ((state.Fields & OutputStateFields.Buffer) == 0)
            {
                return true;
            }

            if (Refuse)
            {
                return false;
            }

            AppliedFrames++;
            return true;
        }
    }

    [Fact]
    public void Refused_frame_keeps_its_damage_for_the_retry()
    {
        using var host = new CompositorTestHost();
        var output = new RefusingOutput("refusing");
        using (var mode = new OutputState())
        {
            Assert.True(output.Commit(mode.SetEnabled(true).SetMode(new OutputMode(100, 80, 60_000))));
        }

        using var sceneOutput = new SceneOutput(host.Scene, output);
        using var swapchain = new Swapchain(new ShmAllocator(), 100, 80, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
        using var state = new OutputState();
        var options = new SceneCommitOptions { AllowDirectScanout = false };
        _ = new SceneRect(host.Scene.Root, 10, 10, RenderColor.Black);

        output.Refuse = true;
        Assert.False(sceneOutput.Commit(host.Renderer, swapchain, state, options));
        Assert.True(sceneOutput.NeedsRepaint);
        Assert.Equal(0, output.AppliedFrames);

        output.Refuse = false;
        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state, options));
        Assert.False(sceneOutput.NeedsRepaint);
        Assert.Equal(1, output.AppliedFrames);

        output.Destroy();
    }
}

public sealed class RendererTargetPreservationTests
{
    public static TheoryData<string> Renderers => new() { "pixman", "gl", "vulkan", "skia", "skia-gl", "skia-vulkan", "skia-graphite", "impeller" };

    [Theory]
    [MemberData(nameof(Renderers))]
    public void Pass_preserves_pixels_outside_its_draws(string renderer)
    {
        CompositorTestHost.SkipUnlessRunnable(renderer);
        using var host = new CompositorTestHost(renderer: renderer);
        var target = new MemoryBuffer(64, 64, DrmFormat.Xrgb8888);
        using var targetGuard = new DeferDestroy(target);

        var pass = host.Renderer.BeginBufferPass(target, new RenderPassOptions());
        pass.AddRect(new RenderColor(1f, 0f, 0f, 1f), new Box(0, 0, 64, 64));
        Assert.True(pass.Submit());

        pass = host.Renderer.BeginBufferPass(target, new RenderPassOptions());
        pass.AddRect(new RenderColor(0f, 0f, 1f, 1f), new Box(8, 8, 16, 16));
        Assert.True(pass.Submit());

        Assert.True(target.BeginDataAccess(BufferDataAccess.Read, out var view));
        try
        {
            unsafe
            {
                uint At(int x, int y) => *(uint*)(view.Data + y * view.Stride + x * 4) | 0xFF000000u;
                Assert.Equal(0xFF0000FFu, At(10, 10));
                Assert.Equal(0xFFFF0000u, At(40, 40));
                Assert.Equal(0xFFFF0000u, At(2, 2));
                Assert.Equal(0xFFFF0000u, At(60, 60));
            }
        }
        finally
        {
            target.EndDataAccess();
        }
    }

    [Theory]
    [InlineData("gl")]
    [InlineData("skia-gl")]
    [InlineData("impeller")]
    [InlineData("vulkan")]
    [InlineData("skia-vulkan")]
    public void Pass_preserves_pixels_outside_its_draws_on_dmabuf_targets(string renderer)
    {
        CompositorTestHost.SkipUnlessRunnable(renderer);
        using var host = new CompositorTestHost(renderer: renderer);
        using var allocator = renderer switch
        {
            "gl" => ((Basin.Render.Gl.GlRenderer)host.Renderer).Device.CreateAllocator(),
            "skia-gl" => ((Basin.Render.Skia.SkiaGlRenderer)host.Renderer).Device.CreateAllocator(),
            "vulkan" => ((Basin.Render.Vulkan.VulkanRenderer)host.Renderer).Device.CreateAllocator(),
            "skia-vulkan" => ((Basin.Render.Skia.SkiaVulkanRenderer)host.Renderer).Device.CreateAllocator(),
            _ => ((Basin.Render.Impeller.ImpellerGlRenderer)host.Renderer).Device.CreateAllocator(),
        };
        var modifiers = allocator.Formats.ModifiersOf(DrmFormat.Xrgb8888).ToArray();
        var target = allocator.Allocate(64, 64, DrmFormat.Xrgb8888, modifiers, BufferUse.Render);
        Assert.SkipWhen(target is null, "GBM declined an Xrgb8888 render target");

        var pass = host.Renderer.BeginBufferPass(target!, new RenderPassOptions());
        pass.AddRect(new RenderColor(1f, 0f, 0f, 1f), new Box(0, 0, 64, 64));
        Assert.True(pass.Submit());

        pass = host.Renderer.BeginBufferPass(target!, new RenderPassOptions());
        pass.AddRect(new RenderColor(0f, 0f, 1f, 1f), new Box(8, 8, 16, 16));
        Assert.True(pass.Submit());

        var readback = new MemoryBuffer(64, 64, DrmFormat.Xrgb8888);
        var texture = host.Renderer.ImportTexture(target!);

        try
        {
            Assert.NotNull(texture);
            pass = host.Renderer.BeginBufferPass(readback, new RenderPassOptions());
            pass.AddTexture(texture, new TextureRenderOptions { DstBox = new Box(0, 0, 64, 64) });
            Assert.True(pass.Submit());

            Assert.True(readback.BeginDataAccess(BufferDataAccess.Read, out var view));
            try
            {
                unsafe
                {
                    uint At(int x, int y) => *(uint*)(view.Data + y * view.Stride + x * 4) | 0xFF000000u;
                    Assert.Equal(0xFF0000FFu, At(10, 10));
                    Assert.Equal(0xFFFF0000u, At(40, 40));
                    Assert.Equal(0xFFFF0000u, At(2, 2));
                    Assert.Equal(0xFFFF0000u, At(60, 60));
                }
            }
            finally
            {
                readback.EndDataAccess();
            }
        }
        finally
        {
            texture?.Dispose();
            readback.Destroy();
            (target as BufferBase)!.Destroy();
        }
    }

    private sealed class DeferDestroy(BufferBase buffer) : IDisposable
    {
        public void Dispose() => buffer.Destroy();
    }
}

public sealed class DamageOracleTests
{
    public static TheoryData<string> Renderers => new() { "pixman", "gl", "skia", "impeller" };

    [Theory]
    [MemberData(nameof(Renderers))]
    public void Damage_tracked_rendering_matches_full_repaint(string renderer)
    {
        CompositorTestHost.SkipUnlessRunnable(renderer);
        using var host = new CompositorTestHost(renderer: renderer);
        using var sceneOutput = new SceneOutput(host.Scene, host.Output);
        using var swapchain = new Swapchain(new ShmAllocator(), 160, 120, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
        using var state = new OutputState();
        var oracle = new MemoryBuffer(160, 120, DrmFormat.Xrgb8888);
        using var oracleGuard = new DeferDestroy(oracle);
        var options = new SceneCommitOptions { AllowDirectScanout = false };

        var surface = host.Client.Compositor.CreateSurface();
        var buffer = host.Client.CreateBuffer(60, 50, Fill.Gradient(60, 50));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Commit();
        host.PumpToServer();
        var node = host.SurfaceScenes[0];
        node.Tree.SetPosition(8, 6);

        void Step(string what)
        {
            var committed = sceneOutput.Commit(host.Renderer, swapchain, state, options);
            Assert.True(committed, $"{what}: expected a commit");
            host.Scene.Render(host.Renderer, oracle, RenderColor.Black);
            AssertSamePixels(oracle, state.Buffer!, what);
        }

        Step("initial map");

        Fill.Solid(60, 50, 0xFF2266AA)(buffer.Data, buffer.Stride);
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(5, 5, 20, 10);
        surface.Commit();
        host.PumpToServer();
        Step("partial damage");

        node.Tree.SetPosition(40, 30);
        Step("move");

        var cover = new SceneRect(host.Scene.Root, 80, 60, new RenderColor(0.2f, 0.8f, 0.3f, 1f));
        cover.SetPosition(30, 20);
        Step("opaque cover");

        Fill.Solid(60, 50, 0xFF993311)(buffer.Data, buffer.Stride);
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, 60, 50);
        surface.Commit();
        host.PumpToServer();
        Step("damage under cover");

        cover.Enabled = false;
        Step("uncover");

        var skippedBefore = sceneOutput.SkippedCommits;
        Assert.False(sceneOutput.Commit(host.Renderer, swapchain, state, options));
        Assert.Equal(skippedBefore + 1, sceneOutput.SkippedCommits);

        surface.Dispose();
        host.PumpToServer();
    }

    [Fact]
    public void A_commit_carrying_only_frame_callbacks_still_asks_for_a_repaint()
    {
        using var host = new CompositorTestHost();
        using var sceneOutput = new SceneOutput(host.Scene, host.Output);
        var surface = host.Client.Compositor.CreateSurface();
        var buffer = host.Client.CreateBuffer(8, 8, Fill.Solid(8, 8, 0xFFFFFFFF));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, 8, 8);
        surface.Commit();
        host.PumpToServer();
        host.RenderFrame();
        host.PumpToClient();

        var pending = 0;
        sceneOutput.DamagePending += () => pending++;

        var done = 0;
        var callback = surface.Frame();
        callback.Done += (_, _) => done++;
        surface.Commit();
        host.PumpToServer();

        Assert.True(pending > 0, "a commit with only frame callbacks must ask for a repaint");

        host.RenderFrame();
        host.PumpToClient();
        Assert.Equal(1, done);

        surface.Dispose();
        host.PumpToServer();
    }

    [Fact]
    public void A_scheduled_repaint_cannot_land_between_a_move_and_its_damage()
    {
        using var host = new CompositorTestHost();
        using var sceneOutput = new SceneOutput(host.Scene, host.Output);
        using var scheduler = new OutputScheduler(host.Loop, host.Output);
        using var swapchain = new Swapchain(new ShmAllocator(), 160, 120, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
        using var state = new OutputState();
        var oracle = new MemoryBuffer(160, 120, DrmFormat.Xrgb8888);
        using var oracleGuard = new DeferDestroy(oracle);
        var options = new SceneCommitOptions { AllowDirectScanout = false };

        sceneOutput.DamagePending += scheduler.ScheduleRepaint;
        scheduler.Repaint += () =>
        {
            if (sceneOutput.Commit(host.Renderer, swapchain, state, options))
            {
                scheduler.NotifyCommitted();
            }
        };

        var node = new SceneRect(host.Scene.Root, 160, 120, new RenderColor(0.2f, 0.4f, 0.8f, 1f));
        scheduler.ScheduleRepaint();
        Settle();
        Assert.NotNull(state.Buffer);
        Assert.True(sceneOutput.Ring.IsEmpty);

        node.SetPosition(40, 30);
        node.Width = 60;
        node.Height = 50;

        using var pending = new PixmanRegion32();
        sceneOutput.Ring.GetBufferDamage(1, pending);
        var extents = pending.Extents;
        Assert.Equal((0, 0, 160, 120), (extents.X1, extents.Y1, extents.X2, extents.Y2));

        Settle();
        host.Scene.Render(host.Renderer, oracle, RenderColor.Black);
        AssertSamePixels(oracle, state.Buffer!, "restored");

        void Settle()
        {
            for (var i = 0; i < 8; i++)
            {
                host.Loop.Dispatch(20);
                host.Output.StepFrame();
            }
        }
    }

    private sealed class DeferDestroy(BufferBase buffer) : IDisposable
    {
        public void Dispose() => buffer.Destroy();
    }

    private static void AssertSamePixels(IBuffer expected, IBuffer actual, string what)
    {
        Assert.True(expected.BeginDataAccess(BufferDataAccess.Read, out var e), what);
        Assert.True(actual.BeginDataAccess(BufferDataAccess.Read, out var a), what);
        try
        {
            unsafe
            {
                for (var y = 0; y < expected.Height; y++)
                {
                    var er = new ReadOnlySpan<byte>((void*)(e.Data + y * e.Stride), expected.Width * 4);
                    var ar = new ReadOnlySpan<byte>((void*)(a.Data + y * a.Stride), expected.Width * 4);
                    if (!er.SequenceEqual(ar))
                    {
                        Assert.Fail($"{what}: row {y} differs between damage-tracked and full repaint");
                    }
                }
            }
        }
        finally
        {
            expected.EndDataAccess();
            actual.EndDataAccess();
        }
    }
}

public sealed class PartialUploadTests
{
    public static TheoryData<string> Renderers => new()
    {
        "pixman", "gl", "vulkan", "skia", "skia-gl", "skia-vulkan", "skia-graphite", "impeller",
    };

    private const uint Background = 0xff204060;
    private const uint Patch = 0xffe0b020;

    [Theory]
    [MemberData(nameof(Renderers))]
    public void A_damaged_rectangle_updates_and_nothing_else_does(string renderer)
    {
        CompositorTestHost.SkipUnlessRunnable(renderer);
        using var host = new CompositorTestHost(64, 64, renderer: renderer);

        var surface = host.Client.Compositor.CreateSurface();
        var buffer = host.Client.CreateBuffer(64, 64, Fill.Solid(64, 64, Background));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, 64, 64);
        surface.Commit();
        host.PumpToServer();
        host.RenderFrame();
        Assert.Equal(Background, host.Pixel(40, 40));

        WriteRect(buffer, 24, 32, 16, 8, Patch);
        surface.Damage(24, 32, 16, 8);
        surface.Commit();
        host.PumpToServer();
        host.RenderFrame();

        Assert.Equal(Patch, host.Pixel(24, 32));
        Assert.Equal(Patch, host.Pixel(39, 39));
        Assert.Equal(Patch, host.Pixel(30, 35));
        Assert.Equal(Background, host.Pixel(23, 32));
        Assert.Equal(Background, host.Pixel(40, 39));
        Assert.Equal(Background, host.Pixel(24, 31));
        Assert.Equal(Background, host.Pixel(24, 40));
        Assert.Equal(Background, host.Pixel(0, 0));
        Assert.Equal(Background, host.Pixel(63, 63));
    }

    [Theory]
    [MemberData(nameof(Renderers))]
    public void Damage_accumulated_between_frames_all_reaches_the_screen(string renderer)
    {
        CompositorTestHost.SkipUnlessRunnable(renderer);
        using var host = new CompositorTestHost(64, 64, renderer: renderer);

        var surface = host.Client.Compositor.CreateSurface();
        var buffer = host.Client.CreateBuffer(64, 64, Fill.Solid(64, 64, Background));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, 64, 64);
        surface.Commit();
        host.PumpToServer();
        host.RenderFrame();

        WriteRect(buffer, 4, 4, 8, 8, Patch);
        surface.Damage(4, 4, 8, 8);
        surface.Commit();
        host.PumpToServer();

        WriteRect(buffer, 48, 50, 8, 8, Patch);
        surface.Damage(48, 50, 8, 8);
        surface.Commit();
        host.PumpToServer();
        host.RenderFrame();

        Assert.Equal(Patch, host.Pixel(6, 6));
        Assert.Equal(Patch, host.Pixel(50, 52));
        Assert.Equal(Background, host.Pixel(30, 30));
    }

    [Theory]
    [MemberData(nameof(Renderers))]
    public void A_double_buffering_client_sees_every_line_it_drew(string renderer)
    {
        CompositorTestHost.SkipUnlessRunnable(renderer);
        using var host = new CompositorTestHost(64, 64, renderer: renderer);

        var surface = host.Client.Compositor.CreateSurface();
        var buffers = new[]
        {
            host.Client.CreateBuffer(64, 64, Fill.Solid(64, 64, Background)),
            host.Client.CreateBuffer(64, 64, Fill.Solid(64, 64, Background)),
        };

        for (var line = 0; line < 16; line++)
        {
            var buffer = buffers[line % 2];

            for (var drawn = 0; drawn <= line; drawn++)
            {
                WriteRect(buffer, 0, drawn * 4, 64, 4, Patch);
            }

            surface.Attach(buffer.Proxy, 0, 0);
            surface.Damage(0, line * 4, 64, 4);
            surface.Commit();
            host.PumpToServer();
            host.RenderFrame();
        }

        for (var line = 0; line < 16; line++)
        {
            Assert.Equal(Patch, host.Pixel(32, line * 4 + 2));
        }
    }

    private static void WriteRect(ClientShmBuffer buffer, int x, int y, int width, int height, uint color)
    {
        unsafe
        {
            for (var row = 0; row < height; row++)
            {
                var line = (uint*)(buffer.Data + (nint)(y + row) * buffer.Stride) + x;
                for (var column = 0; column < width; column++)
                {
                    line[column] = color;
                }
            }
        }
    }
}
