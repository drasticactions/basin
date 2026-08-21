using Xunit;

namespace Basin.Tests;

public sealed class RendererLutTests
{
    public static TheoryData<string> Renderers => new() { "pixman", "gl", "vulkan", "skia", "skia-gl", "skia-vulkan", "skia-graphite" };

    private static ColorLut3D InvertingLut()
    {
        var data = new float[2 * 2 * 2 * 3];
        var index = 0;
        for (var b = 0; b < 2; b++)
        {
            for (var g = 0; g < 2; g++)
            {
                for (var r = 0; r < 2; r++)
                {
                    data[index++] = 1f - r;
                    data[index++] = 1f - g;
                    data[index++] = 1f - b;
                }
            }
        }

        return new ColorLut3D(2, data);
    }

    private static MemoryBuffer SolidBuffer(int width, int height, uint pixel)
    {
        var buffer = new MemoryBuffer(width, height, DrmFormat.Argb8888);
        Assert.True(buffer.BeginDataAccess(BufferDataAccess.Write, out var view));
        unsafe
        {
            for (var y = 0; y < height; y++)
            {
                var row = (uint*)(view.Data + y * view.Stride);
                for (var x = 0; x < width; x++)
                {
                    row[x] = pixel;
                }
            }
        }

        buffer.EndDataAccess();
        return buffer;
    }

    private static (byte A, byte R, byte G, byte B) PixelAt(IBuffer buffer, int x, int y)
    {
        Assert.True(buffer.BeginDataAccess(BufferDataAccess.Read, out var view));
        try
        {
            unsafe
            {
                var v = *(uint*)(view.Data + y * view.Stride + x * 4);
                return ((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
            }
        }
        finally
        {
            buffer.EndDataAccess();
        }
    }

    private static void AssertNear(byte expected, byte actual, string what) =>
        Assert.True(Math.Abs(expected - actual) <= 3, $"{what}: expected ~{expected}, got {actual}");

    [Theory]
    [MemberData(nameof(Renderers))]
    public void Lut_applies_on_straight_alpha_and_repremultiplies(string renderer)
    {
        CompositorTestHost.SkipUnlessRunnable(renderer);
        using var host = new CompositorTestHost(renderer: renderer);

        Assert.Equal(ColorTransformCapability.Lut3D, host.Renderer.ColorTransform);
        var target = SolidBuffer(32, 32, 0xFF000000);
        var opaque = SolidBuffer(8, 8, 0xFF808080);
        var translucent = SolidBuffer(8, 8, 0x80404040);

        var lut = host.Renderer.ImportLut(InvertingLut());
        Assert.NotNull(lut);
        var opaqueTexture = host.Renderer.ImportTexture(opaque);
        var translucentTexture = host.Renderer.ImportTexture(translucent);
        Assert.NotNull(opaqueTexture);
        Assert.NotNull(translucentTexture);

        var pass = host.Renderer.BeginBufferPass(target, new RenderPassOptions());
        pass.AddTexture(opaqueTexture, new TextureRenderOptions
        {
            DstBox = new Box(0, 0, 8, 8),
            Lut = lut,
        });
        pass.AddTexture(translucentTexture, new TextureRenderOptions
        {
            DstBox = new Box(16, 0, 8, 8),
            Lut = lut,
        });
        Assert.True(pass.Submit());

        var (a1, r1, g1, b1) = PixelAt(target, 4, 4);
        Assert.Equal(255, a1);
        AssertNear(127, r1, "opaque red");
        AssertNear(127, g1, "opaque green");
        AssertNear(127, b1, "opaque blue");

        var (_, r2, g2, b2) = PixelAt(target, 20, 4);
        AssertNear(64, r2, "translucent red");
        AssertNear(64, g2, "translucent green");
        AssertNear(64, b2, "translucent blue");

        lut!.Dispose();
        opaqueTexture.Dispose();
        translucentTexture.Dispose();
        target.Destroy();
        opaque.Destroy();
        translucent.Destroy();
    }

    [Fact]
    public void Scene_nodes_render_through_their_lut_and_never_scan_out_with_one()
    {
        using var host = new CompositorTestHost();
        using var sceneOutput = new Basin.Scene.SceneOutput(host.Scene, host.Output) { ScanoutEntryThreshold = 1 };
        using var swapchain = new Swapchain(new ShmAllocator(), 160, 120, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
        using var state = new OutputState();

        var content = SolidBuffer(160, 120, 0xFF808080);
        var node = new Basin.Scene.SceneBuffer(host.Scene.Root) { IsOpaque = true };
        node.SetBuffer(content);
        var lut = host.Renderer.ImportLut(InvertingLut());
        node.Lut = lut;

        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state));
        Assert.False(sceneOutput.IsDirectScanout);
        var (_, r, g, b) = PixelAt(state.Buffer!, 80, 60);
        AssertNear(127, r, "red");
        AssertNear(127, g, "green");
        AssertNear(127, b, "blue");

        node.Lut = null;
        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state));
        (_, r, _, _) = PixelAt(state.Buffer!, 80, 60);
        AssertNear(128, r, "red after clearing the LUT");

        node.Destroy();
        content.Destroy();
        lut!.Dispose();
    }

    [Fact]
    public void Impeller_declares_none_and_the_import_agrees()
    {
        Assert.SkipWhen(!File.Exists(CompositorTestHost.RenderNodePath), "no render node");
        using var host = new CompositorTestHost(renderer: "impeller");
        Assert.Equal(ColorTransformCapability.None, host.Renderer.ColorTransform);
        Assert.Null(host.Renderer.ImportLut(InvertingLut()));
    }
}
