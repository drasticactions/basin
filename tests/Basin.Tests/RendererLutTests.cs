using Basin.Color;
using Basin.Capabilities;
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

        Assert.NotEqual(ColorTransformCapability.None, host.Renderer.ColorTransform);
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
        using var table = new FixedTable(host.Renderer.ImportLut(InvertingLut()));
        sceneOutput.Resolver = table;
        Assert.Same(table.Lut, sceneOutput.LutFor(node));

        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state));
        Assert.False(sceneOutput.IsDirectScanout);
        var (_, r, g, b) = PixelAt(state.Buffer!, 80, 60);
        AssertNear(127, r, "red");
        AssertNear(127, g, "green");
        AssertNear(127, b, "blue");

        sceneOutput.Resolver = null;
        Assert.Null(sceneOutput.LutFor(node));
        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state));
        (_, r, _, _) = PixelAt(state.Buffer!, 80, 60);
        AssertNear(128, r, "red after clearing the LUT");

        node.Destroy();
        content.Destroy();
    }

    [Theory]
    [MemberData(nameof(Renderers))]
    public void The_oracle_and_the_output_agree_through_the_table(string renderer)
    {
        CompositorTestHost.SkipUnlessRunnable(renderer);
        using var host = new CompositorTestHost(renderer: renderer);
        using var sceneOutput = new Basin.Scene.SceneOutput(host.Scene, host.Output);
        using var swapchain = new Swapchain(new ShmAllocator(), 160, 120, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
        using var state = new OutputState();

        var content = SolidBuffer(60, 40, 0xFF4080C0);
        var node = new Basin.Scene.SceneBuffer(host.Scene.Root) { IsOpaque = true };
        node.SetBuffer(content);
        node.SetPosition(20, 30);
        using var table = new FixedTable(host.Renderer.ImportLut(InvertingLut()));
        sceneOutput.Resolver = table;

        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state));
        var oracle = new MemoryBuffer(160, 120, DrmFormat.Xrgb8888);
        Assert.True(host.Scene.Render(host.Renderer, oracle, new Basin.Scene.SceneRenderOptions
        {
            Background = RenderColor.Black,
            Luts = sceneOutput,
        }));

        var (_, r, g, b) = PixelAt(oracle, 40, 40);
        AssertNear(0xFF - 0x40, r, "oracle red");
        AssertNear(0xFF - 0x80, g, "oracle green");
        AssertNear(0xFF - 0xC0, b, "oracle blue");
        var (_, r2, g2, b2) = PixelAt(state.Buffer!, 40, 40);
        AssertNear(r, r2, "red");
        AssertNear(g, g2, "green");
        AssertNear(b, b2, "blue");

        node.Destroy();
        content.Destroy();
        oracle.Destroy();
    }

    private static readonly ImageDescription PqSource = new()
    {
        PrimariesNamed = ColorPrimaries.Bt2020,
        TransferNamed = ColorTransferFunction.St2084Pq,
        Luminances = (0, 1000, 203),
    };

    private static readonly ImageDescription P3Output = new()
    {
        PrimariesNamed = ColorPrimaries.DisplayP3,
        TransferNamed = ColorTransferFunction.Gamma22,
    };

    [Theory]
    [MemberData(nameof(Renderers))]
    public void A_decomposed_row_matches_the_baked_transform(string renderer)
    {
        CompositorTestHost.SkipUnlessRunnable(renderer);
        using var host = new CompositorTestHost(renderer: renderer);
        Assert.SkipWhen(
            host.Renderer.ColorTransform != ColorTransformCapability.Decomposed,
            $"the {renderer} row keeps the table");

        var target = SolidBuffer(48, 16, 0xFF000000);
        var content = SolidBuffer(8, 8, 0xFF80664D);
        var texture = host.Renderer.ImportTexture(content);
        Assert.NotNull(texture);

        var pass = host.Renderer.BeginBufferPass(target, new RenderPassOptions { ColorDescription = ImageDescription.SdrDefault });
        pass.AddTexture(texture, new TextureRenderOptions { DstBox = new Box(0, 0, 8, 8) });
        pass.AddTexture(texture, new TextureRenderOptions { DstBox = new Box(16, 0, 8, 8), ColorDescription = PqSource });
        pass.AddRect(new RenderColor(0x80 / 255f, 0x66 / 255f, 0x4D / 255f, 1f), new Box(32, 0, 8, 8));
        Assert.True(pass.Submit());

        var (_, r0, g0, b0) = PixelAt(target, 4, 4);
        AssertNear(0x80, r0, "untagged red");
        AssertNear(0x66, g0, "untagged green");
        AssertNear(0x4D, b0, "untagged blue");

        Span<double> expected = stackalloc double[3];
        expected[0] = 0x80 / 255.0;
        expected[1] = 0x66 / 255.0;
        expected[2] = 0x4D / 255.0;
        ColorTransformParameters.From(PqSource, ImageDescription.SdrDefault).Apply(expected);
        var (_, r1, g1, b1) = PixelAt(target, 20, 4);
        AssertNear((byte)Math.Round(expected[0] * 255), r1, "pq red");
        AssertNear((byte)Math.Round(expected[1] * 255), g1, "pq green");
        AssertNear((byte)Math.Round(expected[2] * 255), b1, "pq blue");
        Assert.True(r1 != 0x80 || g1 != 0x66, "the PQ source was converted");

        var (_, r2, g2, b2) = PixelAt(target, 36, 4);
        AssertNear(0x80, r2, "rect red on the default output");
        AssertNear(0x66, g2, "rect green on the default output");
        AssertNear(0x4D, b2, "rect blue on the default output");

        var wide = SolidBuffer(48, 16, 0xFF000000);
        pass = host.Renderer.BeginBufferPass(wide, new RenderPassOptions { ColorDescription = P3Output });
        pass.AddTexture(texture, new TextureRenderOptions { DstBox = new Box(0, 0, 8, 8) });
        pass.AddRect(new RenderColor(0x80 / 255f, 0x66 / 255f, 0x4D / 255f, 1f), new Box(16, 0, 8, 8));
        Assert.True(pass.Submit());

        expected[0] = 0x80 / 255.0;
        expected[1] = 0x66 / 255.0;
        expected[2] = 0x4D / 255.0;
        ColorTransformParameters.From(ImageDescription.SdrDefault, P3Output).Apply(expected);
        var (_, r3, g3, b3) = PixelAt(wide, 4, 4);
        AssertNear((byte)Math.Round(expected[0] * 255), r3, "sdr on p3 red");
        AssertNear((byte)Math.Round(expected[1] * 255), g3, "sdr on p3 green");
        AssertNear((byte)Math.Round(expected[2] * 255), b3, "sdr on p3 blue");
        var (_, r4, g4, b4) = PixelAt(wide, 20, 4);
        AssertNear(r3, r4, "rect matches the surface painted the same colour");
        AssertNear(g3, g4, "rect green matches");
        AssertNear(b3, b4, "rect blue matches");

        texture.Dispose();
        target.Destroy();
        wide.Destroy();
        content.Destroy();
    }

    internal sealed class FixedTable(IColorLut? lut) : IColorTransformResolver, IDisposable
    {
        public IColorLut? Lut { get; } = lut;

        public ColorTransformCapability Capability => ColorTransformCapability.Lut3D;

        public IColorLut? Resolve(ImageDescription source, ImageDescription output) => Lut;

        public void Dispose() => Lut?.Dispose();
    }
}
