using Basin.Diagnostics;
using Basin.Renderers;
using Xunit;

namespace Basin.Tests;

public sealed class CaptureTargetClearTests
{
    public static TheoryData<string> Rows => ["pixman", "gl", "vulkan", "skia", "skia-gl", "skia-vulkan", "skia-graphite"];

    [Theory]
    [MemberData(nameof(Rows))]
    public void A_target_cleared_on_the_cpu_starts_the_next_pass_clear(string row)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        var stack = RendererCatalog.Create(row, CompositorTestHost.RenderNodePath);
        var target = new MemoryBuffer(32, 32, DrmFormat.Argb8888);
        try
        {
            for (var round = 0; round < 3; round++)
            {
                Clear(target);
                var pass = stack.Renderer.BeginBufferPass(target, default);
                pass.AddRect(new RenderColor(0f, 0f, 0.25f, 0.25f), new Box(0, 0, 32, 32));
                Assert.True(pass.Submit());
                var rgba = BufferCapture.ReadRgba(target);
                Assert.True(rgba[3] <= 66, $"round {round}: alpha {rgba[3]} after a cleared target and one quarter-alpha rect on {row}");
            }
        }
        finally
        {
            target.Destroy();
            stack.DeviceAllocator?.Dispose();
            stack.Renderer.Dispose();
        }
    }

    private static unsafe void Clear(MemoryBuffer target)
    {
        Assert.True(target.BeginDataAccess(BufferDataAccess.Write, out var view));
        for (var y = 0; y < target.Height; y++)
        {
            new Span<byte>((void*)(view.Data + (y * view.Stride)), target.Width * 4).Clear();
        }

        target.EndDataAccess();
    }
}
