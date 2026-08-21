using Basin.Scene;
using Pixman;
using Xunit;

namespace Basin.Tests;

public sealed class SceneMirrorTests
{
    private static readonly RenderColor Red = new(1f, 0f, 0f, 1f);
    private static readonly RenderColor Grey = new(0.25f, 0.25f, 0.25f, 1f);

    [Fact]
    public void A_mirror_draws_the_source_at_its_own_position()
    {
        using var host = new CompositorTestHost();
        var source = new SceneTree(host.Scene.Root);
        source.SetPosition(10, 10);
        _ = new SceneRect(source, 30, 30, Red);

        var mirror = new SceneMirror(host.Scene.Root, source, 30, 30);
        mirror.SetPosition(100, 60);

        host.RenderFrame();

        Assert.Equal(0xffff0000, host.Pixel(20, 20));
        Assert.Equal(0xffff0000, host.Pixel(110, 70));
    }

    [Fact]
    public void A_mirror_clips_the_source_to_its_own_box()
    {
        using var host = new CompositorTestHost();
        var source = new SceneTree(host.Scene.Root);
        _ = new SceneRect(source, 60, 60, Red);

        var mirror = new SceneMirror(host.Scene.Root, source, 20, 20);
        mirror.SetPosition(100, 60);

        host.RenderFrame();

        Assert.Equal(0xffff0000, host.Pixel(110, 70));
        Assert.NotEqual(0xffff0000, host.Pixel(130, 70));
        Assert.NotEqual(0xffff0000, host.Pixel(110, 90));
    }

    [Fact]
    public void A_disabled_mirror_draws_nothing()
    {
        using var host = new CompositorTestHost();
        var source = new SceneTree(host.Scene.Root);
        _ = new SceneRect(source, 30, 30, Red);
        var mirror = new SceneMirror(host.Scene.Root, source, 30, 30);
        mirror.SetPosition(100, 60);

        mirror.Enabled = false;
        host.RenderFrame();

        Assert.NotEqual(0xffff0000, host.Pixel(110, 70));
    }

    [Fact]
    public void A_mirror_of_a_disabled_source_draws_nothing()
    {
        using var host = new CompositorTestHost();
        var source = new SceneTree(host.Scene.Root);
        _ = new SceneRect(source, 30, 30, Red);
        var mirror = new SceneMirror(host.Scene.Root, source, 30, 30);
        mirror.SetPosition(100, 60);

        source.Enabled = false;
        host.RenderFrame();

        Assert.NotEqual(0xffff0000, host.Pixel(110, 70));
    }

    [Fact]
    public void Damage_in_the_source_also_damages_every_mirror_of_it()
    {
        using var host = new CompositorTestHost();
        var source = new SceneTree(host.Scene.Root);
        var rect = new SceneRect(source, 30, 30, Red);
        var near = new SceneMirror(host.Scene.Root, source, 30, 30);
        near.SetPosition(60, 10);
        var far = new SceneMirror(host.Scene.Root, source, 30, 30);
        far.SetPosition(100, 60);

        var boxes = new List<Box>();
        host.Scene.Damaged += (_, box) => boxes.Add(box);
        rect.Color = Grey;

        Assert.Contains(boxes, box => box.X == 60 && box.Y == 10 && box.Width == 30 && box.Height == 30);
        Assert.Contains(boxes, box => box.X == 100 && box.Y == 60 && box.Width == 30 && box.Height == 30);
    }

    [Fact]
    public void A_destroyed_mirror_stops_taking_the_source_damage()
    {
        using var host = new CompositorTestHost();
        var source = new SceneTree(host.Scene.Root);
        var rect = new SceneRect(source, 30, 30, Red);
        var mirror = new SceneMirror(host.Scene.Root, source, 30, 30);
        mirror.SetPosition(100, 60);
        mirror.Destroy();

        var boxes = new List<Box>();
        host.Scene.Damaged += (_, box) => boxes.Add(box);
        rect.Color = Grey;

        Assert.DoesNotContain(boxes, box => box.X == 100 && box.Y == 60);
    }

    [Fact]
    public void A_source_that_contains_the_mirror_is_rejected()
    {
        using var host = new CompositorTestHost();
        var source = new SceneTree(host.Scene.Root);
        var inner = new SceneTree(source);

        Assert.Throws<InvalidOperationException>(() => new SceneMirror(inner, source, 30, 30));
        Assert.Throws<InvalidOperationException>(() => new SceneMirror(source, source, 30, 30));
    }

    [Fact]
    public void A_mirrored_surface_is_never_a_direct_scanout_candidate()
    {
        using var host = new CompositorTestHost();
        using var sceneOutput = new SceneOutput(host.Scene, host.Output) { ScanoutEntryThreshold = 1 };
        using var swapchain = new Swapchain(new ShmAllocator(), 160, 120, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
        using var state = new OutputState();

        var source = new SceneTree(host.Scene.Root);
        var client = DirectScanoutTests.FakeClientBuffer(160, 120);
        var node = new SceneBuffer(source) { IsOpaque = true };
        node.SetBuffer(client);

        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state));
        Assert.True(sceneOutput.IsDirectScanout);

        var mirror = new SceneMirror(host.Scene.Root, source, 160, 120);
        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state));
        Assert.False(sceneOutput.IsDirectScanout);

        mirror.Destroy();
        node.Destroy();
        client.Destroy();
    }

    [Fact]
    public void Mirrored_content_never_occludes_what_is_under_it()
    {
        using var host = new CompositorTestHost();
        using var sceneOutput = new SceneOutput(host.Scene, host.Output);
        using var swapchain = new Swapchain(new ShmAllocator(), 160, 120, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
        using var state = new OutputState();
        var oracle = new MemoryBuffer(160, 120, DrmFormat.Xrgb8888);
        var options = new SceneCommitOptions { AllowDirectScanout = false };

        var below = new SceneRect(host.Scene.Root, 160, 120, Grey);
        var source = new SceneTree(host.Scene.Root);
        _ = new SceneRect(source, 40, 40, Red);
        source.Enabled = false;

        var mirror = new SceneMirror(host.Scene.Root, source, 160, 120);

        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state, options));
        source.Enabled = true;
        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state, options));
        host.Scene.Render(host.Renderer, oracle, RenderColor.Black);

        AssertSamePixels(oracle, state.Buffer!, "mirror over an opaque rect");
        oracle.Destroy();
        GC.KeepAlive(below);
        GC.KeepAlive(mirror);
    }

    [Fact]
    public void A_mirror_draws_the_same_through_the_oracle_and_the_optimized_path()
    {
        using var host = new CompositorTestHost();
        using var sceneOutput = new SceneOutput(host.Scene, host.Output);
        using var swapchain = new Swapchain(new ShmAllocator(), 160, 120, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
        using var state = new OutputState();
        var oracle = new MemoryBuffer(160, 120, DrmFormat.Xrgb8888);
        var options = new SceneCommitOptions { AllowDirectScanout = false };

        _ = new SceneRect(host.Scene.Root, 160, 120, Grey);
        var source = new SceneTree(host.Scene.Root);
        source.SetPosition(8, 8);
        _ = new SceneRect(source, 40, 30, Red);
        _ = new SceneRect(source, 10, 10, new RenderColor(0f, 0f, 1f, 1f));

        var frame = new SceneTransform(host.Scene.Root);
        frame.SetPosition(90, 50);
        frame.Matrix = RenderTransform.Scale(0.5, 0.5);
        var mirror = new SceneMirror(frame, source, 40, 30);

        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state, options));
        host.Scene.Render(host.Renderer, oracle, RenderColor.Black);
        AssertSamePixels(oracle, state.Buffer!, "scaled mirror");

        mirror.SetPosition(6, 4);
        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state, options));
        host.Scene.Render(host.Renderer, oracle, RenderColor.Black);
        AssertSamePixels(oracle, state.Buffer!, "moved mirror");

        oracle.Destroy();
    }

    [Fact]
    public void A_mirror_of_a_mirror_stops_at_the_depth_limit()
    {
        using var host = new CompositorTestHost();
        var source = new SceneTree(host.Scene.Root);
        _ = new SceneRect(source, 20, 20, Red);

        var first = new SceneTree(host.Scene.Root);
        first.SetPosition(40, 0);
        _ = new SceneMirror(first, source, 20, 20);

        var second = new SceneTree(host.Scene.Root);
        second.SetPosition(80, 0);
        _ = new SceneMirror(second, first, 20, 20);

        host.RenderFrame();

        Assert.Equal(0xffff0000, host.Pixel(5, 5));
        Assert.Equal(0xffff0000, host.Pixel(45, 5));
        Assert.Equal(0xffff0000, host.Pixel(85, 5));
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
                    var expectedRow = new ReadOnlySpan<byte>((void*)(e.Data + (y * e.Stride)), expected.Width * 4);
                    var actualRow = new ReadOnlySpan<byte>((void*)(a.Data + (y * a.Stride)), expected.Width * 4);
                    if (!expectedRow.SequenceEqual(actualRow))
                    {
                        Assert.Fail($"{what}: row {y} differs");
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
