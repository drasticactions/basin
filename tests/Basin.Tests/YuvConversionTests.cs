using Basin.Capabilities;
using Basin.Scene;
using Basin.Renderers;
using Xunit;

namespace Basin.Tests;

public sealed class YuvConversionTests
{

    private static readonly (byte R, byte G, byte B)[] Quadrants =
    [
        (255, 0, 0), (0, 255, 0),
        (0, 0, 255), (200, 180, 40),
    ];

    private static MemoryBuffer Pattern(int width, int height)
    {
        var buffer = new MemoryBuffer(width, height, DrmFormat.Xrgb8888);
        Assert.True(buffer.BeginDataAccess(BufferDataAccess.Write, out var view));
        unsafe
        {
            for (var y = 0; y < height; y++)
            {
                var row = (uint*)(view.Data + (y * view.Stride));
                for (var x = 0; x < width; x++)
                {
                    var (r, g, b) = Quadrants[(y < height / 2 ? 0 : 2) + (x < width / 2 ? 0 : 1)];
                    row[x] = 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | b;
                }
            }
        }

        buffer.EndDataAccess();
        return buffer;
    }

    [Fact]
    public void The_reference_matches_bt709_limited_white_black_and_red()
    {
        Assert.Equal(((byte)235, (byte)128, (byte)128), YuvConversion.Reference(255, 255, 255, YuvCoefficients.Bt709, YuvRange.Limited));
        Assert.Equal(((byte)16, (byte)128, (byte)128), YuvConversion.Reference(0, 0, 0, YuvCoefficients.Bt709, YuvRange.Limited));
        Assert.Equal(((byte)63, (byte)102, (byte)240), YuvConversion.Reference(255, 0, 0, YuvCoefficients.Bt709, YuvRange.Limited));
        Assert.Equal(new Box(0, 0, 64, 36), YuvConversion.Fit(1920, 1080, 64, 36));
        Assert.Equal(new Box(0, 18, 64, 36), YuvConversion.Fit(1920, 1080, 64, 72));
    }

    [Theory]
    [InlineData("gl")]
    [InlineData("vulkan")]
    public void A_renderer_writes_bt709_limited_nv12_planes_to_within_one(string renderer)
    {
        CompositorTestHost.SkipUnlessRunnable(renderer);
        var stack = RendererCatalog.Create(renderer, CompositorTestHost.RenderNodePath);
        try
        {
            Assert.SkipUnless(stack.Renderer.CanConvertYuv, $"{renderer} does not convert to YUV");
            Assert.SkipWhen(stack.DeviceAllocator is null, "no device allocator");
            const int width = 64;
            const int height = 32;
            var target = stack.DeviceAllocator!.Allocate(width, height, DrmFormat.Nv12, [DrmFormatSet.ModifierLinear], BufferUse.Render);
            Assert.SkipWhen(target is null, "the allocator cannot make a linear NV12 buffer");
            var source = Pattern(width, height);
            var texture = stack.Renderer.ImportTexture(source)!;
            Assert.True(stack.Renderer.ConvertToYuv(texture, default, target!, YuvCoefficients.Bt709, YuvRange.Limited));

            Assert.True(target!.TryGetDmabuf(out var attributes));
            Assert.True(target.BeginDataAccess(BufferDataAccess.Read, out var view));
            try
            {
                unsafe
                {
                    var luma = (byte*)view.Data;
                    var chroma = (byte*)view.Data + (attributes.Offsets[1] - attributes.Offsets[0]);
                    for (var quadrant = 0; quadrant < 4; quadrant++)
                    {
                        var x = (quadrant % 2 == 0 ? width / 4 : 3 * width / 4) & ~1;
                        var y = (quadrant < 2 ? height / 4 : 3 * height / 4) & ~1;
                        var (r, g, b) = Quadrants[quadrant];
                        var expected = YuvConversion.Reference(r, g, b, YuvCoefficients.Bt709, YuvRange.Limited);
                        var actualY = luma[(y * attributes.Strides[0]) + x];
                        var uv = chroma + ((y / 2) * attributes.Strides[1]) + x;
                        Assert.InRange(actualY, expected.Y - 1, expected.Y + 1);
                        Assert.InRange(uv[0], expected.Cb - 1, expected.Cb + 1);
                        Assert.InRange(uv[1], expected.Cr - 1, expected.Cr + 1);
                    }
                }
            }
            finally
            {
                target.EndDataAccess();
            }

            texture.Dispose();
            source.Destroy();
            ((BufferBase)target).Destroy();
        }
        finally
        {
            stack.DeviceAllocator?.Dispose();
            stack.Renderer.Dispose();
        }
    }

    [Theory]
    [InlineData("gl")]
    [InlineData("vulkan")]
    public void An_nv12_capture_allocates_nothing_once_warm(string renderer)
    {
        CompositorTestHost.SkipUnlessRunnable(renderer);
        using var host = new CompositorTestHost(64, 48, renderer);
        Assert.SkipWhen(host.DeviceAllocator is null, "no device allocator");
        _ = new SceneRect(host.Scene.Root, 64, 48, new RenderColor(1f, 0f, 0f, 1f));
        using var capture = new SceneScreenCapture(host.Scene, host.Layout)
        {
            Renderer = host.Renderer,
            Allocator = host.DeviceAllocator,
            Background = RenderColor.Black,
        };
        var target = host.DeviceAllocator!.Allocate(64, 48, DrmFormat.Nv12, [DrmFormatSet.ModifierLinear], BufferUse.Render);
        Assert.SkipWhen(target is null, "the allocator cannot make a linear NV12 buffer");
        var source = CaptureSource.Output(host.Output);
        try
        {
            for (var i = 0; i < 5; i++)
            {
                Assert.True(capture.Capture(source, default, target!));
            }

            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 200; i++)
            {
                capture.Capture(source, default, target!);
            }

            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        }
        finally
        {
            ((BufferBase)target!).Destroy();
        }
    }

    [Theory]
    [InlineData("gl")]
    [InlineData("vulkan")]
    public unsafe void A_linear_nv12_client_buffer_draws_its_bt601_limited_colors(string renderer)
    {
        CompositorTestHost.SkipUnlessRunnable(renderer);
        var stack = RendererCatalog.Create(renderer, CompositorTestHost.RenderNodePath);
        try
        {
            Assert.SkipUnless(
                stack.Renderer.DmabufTextureFormats.Contains(DrmFormat.Nv12, DrmFormatSet.ModifierLinear),
                $"{renderer} does not import linear NV12");
            Assert.SkipWhen(stack.DeviceAllocator is null, "no device allocator");
            const int width = 64;
            const int height = 32;
            var source = stack.DeviceAllocator!.Allocate(width, height, DrmFormat.Nv12, [DrmFormatSet.ModifierLinear], BufferUse.Render);
            Assert.SkipWhen(source is null, "the allocator cannot make a linear NV12 buffer");
            Assert.True(source!.TryGetDmabuf(out var attributes));
            Assert.True(source.BeginDataAccess(BufferDataAccess.Write, out var view));
            try
            {
                var luma = (byte*)view.Data;
                var chroma = (byte*)view.Data + (attributes.Offsets[1] - attributes.Offsets[0]);
                for (var y = 0; y < height; y++)
                {
                    for (var x = 0; x < width; x++)
                    {
                        var (r, g, b) = Quadrants[(y < height / 2 ? 0 : 2) + (x < width / 2 ? 0 : 1)];
                        var encoded = YuvConversion.Reference(r, g, b, YuvCoefficients.Bt601, YuvRange.Limited);
                        luma[(y * attributes.Strides[0]) + x] = encoded.Y;
                        if (x % 2 == 0 && y % 2 == 0)
                        {
                            var uv = chroma + ((y / 2) * attributes.Strides[1]) + x;
                            uv[0] = encoded.Cb;
                            uv[1] = encoded.Cr;
                        }
                    }
                }
            }
            finally
            {
                source.EndDataAccess();
            }

            var texture = stack.Renderer.ImportTexture(source);
            Assert.NotNull(texture);
            var target = new MemoryBuffer(width, height, DrmFormat.Xrgb8888);
            var pass = stack.Renderer.BeginBufferPass(target, new RenderPassOptions());
            pass.AddTexture(texture!, new TextureRenderOptions { DstBox = new Box(0, 0, width, height) });
            Assert.True(pass.Submit());

            Assert.True(target.BeginDataAccess(BufferDataAccess.Read, out var drawn));
            try
            {
                for (var quadrant = 0; quadrant < 4; quadrant++)
                {
                    var x = quadrant % 2 == 0 ? width / 4 : 3 * width / 4;
                    var y = quadrant < 2 ? height / 4 : 3 * height / 4;
                    var pixel = ((uint*)(drawn.Data + (y * drawn.Stride)))[x];
                    var (r, g, b) = Quadrants[quadrant];
                    Assert.InRange((int)((pixel >> 16) & 0xFF), r - 3, r + 3);
                    Assert.InRange((int)((pixel >> 8) & 0xFF), g - 3, g + 3);
                    Assert.InRange((int)(pixel & 0xFF), b - 3, b + 3);
                }
            }
            finally
            {
                target.EndDataAccess();
            }

            texture!.Dispose();
            target.Destroy();
            ((BufferBase)source).Destroy();
        }
        finally
        {
            stack.DeviceAllocator?.Dispose();
            stack.Renderer.Dispose();
        }
    }
}
