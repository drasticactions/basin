using Basin.Scene;
using Xunit;

namespace Basin.Tests;

public sealed class TransformOutputTests
{
    public static TheoryData<string> Renderers => new() { "pixman", "gl" };

    [Theory]
    [MemberData(nameof(Renderers))]
    public void Transformed_scene_matches_the_oracle(string renderer)
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

        var transform = new SceneTransform(host.Scene.Root);
        transform.SetPosition(30, 20);
        _ = new SceneRect(transform, 72, 62, new RenderColor(0.2f, 0.3f, 0.6f, 1f));
        var content = host.SurfaceScenes[0];
        content.Tree.Reparent(transform);
        content.Tree.SetPosition(6, 6);

        void Step(string what)
        {
            var committed = sceneOutput.Commit(host.Renderer, swapchain, state, options);
            Assert.True(committed, $"{what}: expected a commit");
            host.Scene.Render(host.Renderer, oracle, RenderColor.Black);
            AssertSamePixels(oracle, state.Buffer!, what);
        }

        Step("identity transform");

        transform.Matrix = RenderTransform.RotationAbout(Math.PI / 8, 36, 31);
        Step("rotation applied");

        Fill.Solid(60, 50, 0xFF2266AA)(buffer.Data, buffer.Stride);
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(5, 5, 20, 10);
        surface.Commit();
        host.PumpToServer();
        Step("client damage under rotation");

        transform.Alpha = 0.6f;
        Step("group alpha");

        var behind = new SceneRect(host.Scene.Root, 40, 40, new RenderColor(0.7f, 0.2f, 0.2f, 1f));
        behind.SetPosition(10, 60);
        behind.LowerToBottom();
        Step("content behind a translucent transform");

        transform.Matrix = RenderTransform.Identity;
        transform.Alpha = 1f;
        Step("back to identity");

        surface.Dispose();
        host.PumpToServer();
    }

    [Fact]
    public void Transformed_fullscreen_buffer_never_scans_out()
    {
        using var host = new CompositorTestHost();
        using var sceneOutput = new SceneOutput(host.Scene, host.Output) { ScanoutEntryThreshold = 1 };
        using var swapchain = new Swapchain(new ShmAllocator(), 160, 120, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
        using var state = new OutputState();

        var client = DirectScanoutTests.FakeClientBuffer(160, 120);
        using var clientGuard = new DeferDestroy(client);
        var transform = new SceneTransform(host.Scene.Root);
        var node = new SceneBuffer(transform) { IsOpaque = true };
        node.SetBuffer(client);

        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state));
        Assert.True(sceneOutput.IsDirectScanout);

        transform.Matrix = RenderTransform.RotationAbout(0.1, 80, 60);
        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state));
        Assert.False(sceneOutput.IsDirectScanout);

        transform.Matrix = RenderTransform.Identity;
        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state));
        Assert.True(sceneOutput.IsDirectScanout);

        node.Destroy();
    }

    [Fact]
    public void Transformed_candidate_is_declined_for_planes()
    {
        using var host = new CompositorTestHost();
        using var sceneOutput = new SceneOutput(host.Scene, host.Output) { OffloadEntryThreshold = 1 };
        using var swapchain = new Swapchain(new ShmAllocator(), 160, 120, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
        using var state = new OutputState();
        var options = new SceneCommitOptions { AllowDirectScanout = false, AllowPlaneOffload = true };

        var client = DirectScanoutTests.FakeClientBuffer(40, 40);
        using var clientGuard = new DeferDestroy(client);
        var transform = new SceneTransform(host.Scene.Root);
        transform.SetPosition(20, 20);
        var node = new SceneBuffer(transform);
        node.SetBuffer(client);
        transform.Matrix = RenderTransform.RotationAbout(0.3, 20, 20);

        _ = sceneOutput.Commit(host.Renderer, swapchain, state, options);
        Assert.True(sceneOutput.DeclinedFor(PlaneDeclineReason.Transformed) > 0);

        node.Destroy();
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

    private sealed class DeferDestroy(BufferBase buffer) : IDisposable
    {
        public void Dispose() => buffer.Destroy();
    }
}
