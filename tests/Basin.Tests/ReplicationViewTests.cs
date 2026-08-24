using Basin.Scene;
using Xunit;

namespace Basin.Tests;

public sealed class ReplicationViewTests
{
    private static uint PixelAt(IBuffer buffer, int x, int y)
    {
        Assert.True(buffer.BeginDataAccess(BufferDataAccess.Read, out var view));
        try
        {
            unsafe
            {
                return *(uint*)(view.Data + (y * view.Stride) + (x * 4));
            }
        }
        finally
        {
            buffer.EndDataAccess();
        }
    }

    [Fact]
    public void A_replication_view_scales_the_source_region_and_letterboxes_the_rest()
    {
        using var host = new CompositorTestHost();
        using var sceneOutput = new SceneOutput(host.Scene, host.Output);
        using var swapchain = new Swapchain(new ShmAllocator(), 160, 120, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
        using var state = new OutputState();
        var options = new SceneCommitOptions
        {
            AllowDirectScanout = false,
            Background = new RenderColor(0f, 0f, 0f, 1f),
        };

        var rect = new SceneRect(host.Scene.Root, 320, 240, new RenderColor(0f, 1f, 0f, 1f));
        rect.SetPosition(400, 0);

        sceneOutput.ReplicationSource = new Box(400, 0, 320, 240);
        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state, options));

        Assert.Equal(0xFF00FF00u, PixelAt(state.Buffer!, 80, 60));
        Assert.Equal(0xFF00FF00u, PixelAt(state.Buffer!, 2, 2));
        Assert.Equal(0xFF00FF00u, PixelAt(state.Buffer!, 157, 117));

        sceneOutput.ReplicationSource = new Box(400, 0, 480, 240);
        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state, options));

        Assert.Equal(0xFF00FF00u, PixelAt(state.Buffer!, 80, 60));
        Assert.Equal(0xFF00FF00u, PixelAt(state.Buffer!, 2, 25));
        Assert.Equal(0xFF000000u, PixelAt(state.Buffer!, 80, 5));
        Assert.Equal(0xFF000000u, PixelAt(state.Buffer!, 80, 115));
        Assert.Equal(0xFF000000u, PixelAt(state.Buffer!, 130, 60));

        sceneOutput.ReplicationSource = null;
        sceneOutput.Position = new Point(0, 0);
        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state, options));
        Assert.Equal(0xFF000000u, PixelAt(state.Buffer!, 80, 60));

        rect.Destroy();
    }
}
