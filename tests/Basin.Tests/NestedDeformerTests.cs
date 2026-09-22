using Basin.Scene;
using Xunit;

namespace Basin.Tests;

public sealed class NestedDeformerTests
{
    [Fact]
    public void A_wave_under_a_squeeze_renders_the_same_through_the_oracle_and_the_output()
    {
        using var host = new CompositorTestHost();
        using var sceneOutput = new SceneOutput(host.Scene, host.Output);
        using var swapchain = new Swapchain(new ShmAllocator(), 160, 120, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
        using var state = new OutputState();
        var oracle = new MemoryBuffer(160, 120, DrmFormat.Xrgb8888);
        var options = new SceneCommitOptions { AllowDirectScanout = false };

        var outer = new SceneTransform(host.Scene.Root);
        outer.SetPosition(20, 25);
        var inner = new SceneTransform(outer);
        _ = new SceneRect(inner, 100, 46, new RenderColor(0.5f, 0.25f, 0.12f, 1f));
        var wave = new WaveDeformer { Amplitude = 6 };
        inner.Deformer = wave;
        outer.Deformer = new SqueezeDeformer { Factor = 0.5 };

        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state, options));
        host.Scene.Render(host.Renderer, oracle, RenderColor.Black);
        AssertSamePixels(oracle, state.Buffer!, "nested deformers");

        var rgba = Basin.Diagnostics.BufferCapture.ReadRgba(oracle);
        int At(int x, int y) => (rgba[((y * 160) + x) * 4] << 16)
            | (rgba[(((y * 160) + x) * 4) + 1] << 8)
            | rgba[(((y * 160) + x) * 4) + 2];
        Assert.NotEqual(0, At(45, 48));
        Assert.Equal(0, At(20 + 60, 48));

        oracle.Destroy();
    }

    [Fact]
    public void The_outer_capture_repaints_once_per_content_change()
    {
        using var host = new CompositorTestHost();
        var outer = new SceneTransform(host.Scene.Root);
        outer.SetPosition(20, 25);
        var inner = new SceneTransform(outer);
        var rect = new SceneRect(inner, 100, 46, new RenderColor(0.5f, 0.25f, 0.12f, 1f));
        inner.Deformer = new WaveDeformer { Amplitude = 6 };
        outer.Deformer = new SqueezeDeformer { Factor = 0.5 };

        host.RenderFrame();
        var (first, _, _) = outer.Capture;
        Assert.NotNull(first);
        var (innerFirst, _, _) = inner.Capture;
        Assert.NotNull(innerFirst);

        host.RenderFrame();
        host.RenderFrame();
        var (still, _, _) = outer.Capture;
        Assert.Same(first, still);

        var before = Basin.Diagnostics.BufferCapture.ReadRgba(host.Target);
        rect.Color = new RenderColor(0.1f, 0.6f, 0.2f, 1f);
        host.RenderFrame();
        var after = Basin.Diagnostics.BufferCapture.ReadRgba(host.Target);
        Assert.NotEqual(before, after);
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
