using Basin.Scene;
using Xunit;

namespace Basin.Tests;

public sealed class SceneFilterTests
{
    public static TheoryData<string> Renderers => new() { "pixman", "gl", "vulkan", "skia", "skia-gl", "skia-vulkan", "skia-graphite" };

    private static MemoryBuffer Checker()
    {
        var buffer = new MemoryBuffer(4, 4, DrmFormat.Argb8888);
        Assert.True(buffer.BeginDataAccess(BufferDataAccess.Write, out var access));
        unsafe
        {
            for (var y = 0; y < 4; y++)
            {
                var row = (uint*)(access.Data + (y * access.Stride));
                for (var x = 0; x < 4; x++)
                {
                    row[x] = ((x + y) & 1) == 0 ? 0xFFFFFFFFu : 0xFF103080u;
                }
            }
        }

        buffer.EndDataAccess();
        return buffer;
    }

    private static void AssertSamePixels(IBuffer expected, IBuffer actual)
    {
        Assert.True(expected.BeginDataAccess(BufferDataAccess.Read, out var e));
        Assert.True(actual.BeginDataAccess(BufferDataAccess.Read, out var a));
        try
        {
            unsafe
            {
                for (var y = 0; y < expected.Height; y++)
                {
                    var expectedRow = new ReadOnlySpan<byte>((void*)(e.Data + (y * e.Stride)), expected.Width * 4);
                    var actualRow = new ReadOnlySpan<byte>((void*)(a.Data + (y * a.Stride)), expected.Width * 4);
                    Assert.True(expectedRow.SequenceEqual(actualRow), $"row {y} differs");
                }
            }
        }
        finally
        {
            actual.EndDataAccess();
            expected.EndDataAccess();
        }
    }

    [Theory]
    [MemberData(nameof(Renderers))]
    public void A_nearest_subtree_draws_the_same_through_the_oracle_and_the_output(string renderer)
    {
        CompositorTestHost.SkipUnlessRunnable(renderer);
        using var host = new CompositorTestHost(renderer: renderer);
        using var sceneOutput = new SceneOutput(host.Scene, host.Output);
        using var swapchain = new Swapchain(new ShmAllocator(), 160, 120, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
        using var state = new OutputState();
        var oracle = new MemoryBuffer(160, 120, DrmFormat.Xrgb8888);
        var checker = Checker();

        var outer = new SceneTree(host.Scene.Root) { Filter = TextureFilter.Nearest };
        var inner = new SceneTree(outer);
        var node = new SceneBuffer(inner);
        node.SetBuffer(checker);
        node.DestinationWidth = 64;
        node.DestinationHeight = 64;
        node.SetPosition(10, 10);
        var linear = new SceneTree(outer) { Filter = TextureFilter.Linear };
        var smooth = new SceneBuffer(linear);
        smooth.SetBuffer(checker);
        smooth.DestinationWidth = 64;
        smooth.DestinationHeight = 64;
        smooth.SetPosition(86, 10);

        var options = new SceneCommitOptions { AllowDirectScanout = false };
        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state, options));
        host.Scene.Render(host.Renderer, oracle, RenderColor.Black);
        AssertSamePixels(oracle, state.Buffer!);

        Assert.True(oracle.BeginDataAccess(BufferDataAccess.Read, out var pixels));
        unsafe
        {
            uint At(int x, int y) => ((uint*)(pixels.Data + (y * pixels.Stride)))[x] | 0xFF000000u;
            Assert.Equal(0xFFFFFFFFu, At(11, 11));
            Assert.Equal(0xFF103080u, At(27, 11));
            Assert.NotEqual(0xFFFFFFFFu, At(86 + 15, 10 + 8));
        }

        oracle.EndDataAccess();
        node.Destroy();
        smooth.Destroy();
        checker.Destroy();
        oracle.Destroy();
    }
}
