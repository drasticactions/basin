using Basin.Scene;
using Xunit;

namespace Basin.Tests;

public sealed class PostStageTests
{
    private sealed class PassThrough(RenderColor marker, Box markerBox) : IPostStage
    {
        public void Render(IRenderPass pass, ITexture frame, in PostContext context)
        {
            pass.AddTexture(frame, new TextureRenderOptions { DstBox = new Box(0, 0, context.Width, context.Height) });
            pass.AddRect(marker, markerBox);
        }
    }

    private sealed class Magnifier : IPostStage
    {
        public void Render(IRenderPass pass, ITexture frame, in PostContext context)
        {
            pass.AddTexture(frame, new TextureRenderOptions
            {
                DstBox = new Box(0, 0, context.Width, context.Height),
                Transform = RenderTransform.Scale(2, 2),
            });
        }
    }

    [Fact]
    public void Stages_chain_through_ping_pong_buffers()
    {
        using var host = new CompositorTestHost();
        using var sceneOutput = new SceneOutput(host.Scene, host.Output);
        using var swapchain = new Swapchain(new ShmAllocator(), 160, 120, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
        using var state = new OutputState();
        var options = new SceneCommitOptions { AllowDirectScanout = false };

        _ = new SceneRect(host.Scene.Root, 160, 120, new RenderColor(0f, 0f, 0.5f, 1f));
        sceneOutput.AddPostStage(new PassThrough(new RenderColor(1f, 0f, 0f, 1f), new Box(10, 10, 8, 8)));
        sceneOutput.AddPostStage(new PassThrough(new RenderColor(0f, 1f, 0f, 1f), new Box(30, 10, 8, 8)));

        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state, options));
        var rgba = Basin.Diagnostics.BufferCapture.ReadRgba((MemoryBuffer)state.Buffer!);
        int Channel(int x, int y, int c) => rgba[(((y * 160) + x) * 4) + c];
        Assert.True(Channel(12, 12, 0) > 200, "first stage's marker survives the second stage");
        Assert.True(Channel(32, 12, 1) > 200, "second stage's marker lands");
        Assert.True(Channel(80, 80, 2) > 100, "scene content passes through both stages");
    }

    [Fact]
    public void Magnifier_scales_the_composited_frame()
    {
        using var host = new CompositorTestHost();
        using var sceneOutput = new SceneOutput(host.Scene, host.Output);
        using var swapchain = new Swapchain(new ShmAllocator(), 160, 120, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
        using var state = new OutputState();
        var options = new SceneCommitOptions { AllowDirectScanout = false };

        var rect = new SceneRect(host.Scene.Root, 10, 10, new RenderColor(1f, 0f, 0f, 1f));
        rect.SetPosition(20, 20);
        sceneOutput.AddPostStage(new Magnifier());

        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state, options));
        var rgba = Basin.Diagnostics.BufferCapture.ReadRgba((MemoryBuffer)state.Buffer!);
        int Red(int x, int y) => rgba[((y * 160) + x) * 4];
        Assert.True(Red(45, 45) > 200, "magnified rect covers 40..60");
        Assert.True(Red(22, 22) < 60, "the unmagnified position reads background");
    }

    [Fact]
    public void Post_stages_suppress_direct_scanout_and_release_it_on_removal()
    {
        using var host = new CompositorTestHost();
        using var sceneOutput = new SceneOutput(host.Scene, host.Output) { ScanoutEntryThreshold = 1 };
        using var swapchain = new Swapchain(new ShmAllocator(), 160, 120, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
        using var state = new OutputState();

        var client = DirectScanoutTests.FakeClientBuffer(160, 120);
        var node = new SceneBuffer(host.Scene.Root) { IsOpaque = true };
        node.SetBuffer(client);

        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state));
        Assert.True(sceneOutput.IsDirectScanout);

        var stage = new PassThrough(new RenderColor(1f, 1f, 1f, 1f), new Box(0, 0, 4, 4));
        sceneOutput.AddPostStage(stage);
        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state));
        Assert.False(sceneOutput.IsDirectScanout);

        Assert.True(sceneOutput.RemovePostStage(stage));
        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state));
        Assert.True(sceneOutput.IsDirectScanout);

        node.Destroy();
        client.Destroy();
    }

    [Fact]
    public void Software_cursor_composites_after_the_last_stage()
    {
        using var host = new CompositorTestHost();
        using var sceneOutput = new SceneOutput(host.Scene, host.Output);
        using var swapchain = new Swapchain(new ShmAllocator(), 160, 120, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
        using var state = new OutputState();
        var options = new SceneCommitOptions { AllowDirectScanout = false };

        _ = new SceneRect(host.Scene.Root, 160, 120, new RenderColor(0f, 0f, 0.5f, 1f));
        var cursorImage = new MemoryBuffer(8, 8, DrmFormat.Argb8888);
        Assert.True(cursorImage.BeginDataAccess(BufferDataAccess.Write, out var view));
        Fill.Solid(8, 8, 0xFFFFFFFF)(view.Data, view.Stride);
        cursorImage.EndDataAccess();
        sceneOutput.SetSoftwareCursor(cursorImage, 0, 0);
        sceneOutput.MoveSoftwareCursor(100, 60);

        sceneOutput.AddPostStage(new Magnifier());
        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state, options));
        var rgba = Basin.Diagnostics.BufferCapture.ReadRgba((MemoryBuffer)state.Buffer!);
        int At(int x, int y) => rgba[((y * 160) + x) * 4];
        Assert.True(At(103, 63) > 200, "cursor draws at its own position, unmagnified");

        sceneOutput.SetSoftwareCursor(null, 0, 0);
        cursorImage.Destroy();
    }
}
