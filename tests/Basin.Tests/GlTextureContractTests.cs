using System.Runtime.InteropServices;
using Basin.Render.Gl;
using Xunit;

namespace Basin.Tests;

public sealed class GlTextureContractTests
{
    [DllImport("libc", SetLastError = true)]
    private static extern nint mmap(nint addr, nuint length, int prot, int flags, int fd, nint offset);

    [DllImport("libc")]
    private static extern int munmap(nint addr, nuint length);

    [Fact]
    public void An_argb_shm_texture_reports_alpha_before_its_first_upload()
    {
        CompositorTestHost.SkipUnlessRunnable("gl");
        using var host = new CompositorTestHost(renderer: "gl");
        var device = ((GlRenderer)host.Renderer).Device;
        var argb = new MemoryBuffer(8, 8, DrmFormat.Argb8888);
        var xrgb = new MemoryBuffer(8, 8, DrmFormat.Xrgb8888);
        var withAlpha = new GlShmTexture(device, argb);
        var opaque = new GlShmTexture(device, xrgb);
        Assert.True(withAlpha.HasAlpha);
        Assert.False(opaque.HasAlpha);
        withAlpha.Dispose();
        opaque.Dispose();
        argb.Destroy();
        xrgb.Destroy();
    }

    [Fact]
    public void A_new_yuv_matrix_converts_again_without_MarkDirty()
    {
        CompositorTestHost.SkipUnlessRunnable("gl");
        using var host = new CompositorTestHost(renderer: "gl");
        var renderer = (GlRenderer)host.Renderer;
        Assert.SkipUnless(
            renderer.DmabufTextureFormats.Contains(DrmFormat.Nv12, DrmFormatSet.ModifierLinear), "gl does not import linear NV12 here");
        using var allocator = renderer.Device.CreateAllocator();
        var source = allocator.Allocate(16, 16, DrmFormat.Nv12, [DrmFormatSet.ModifierLinear], BufferUse.Render);
        Assert.SkipWhen(source is null, "GBM cannot allocate linear NV12 here");
        Assert.True(source!.TryGetDmabuf(out var attributes));
        Fill(attributes, y: 81, u: 90, v: 240);

        var texture = renderer.ImportTexture(source);
        var yuv = Assert.IsType<GlYuvTexture>(texture);
        var target = new MemoryBuffer(16, 16, DrmFormat.Argb8888);

        Draw(renderer, yuv, target);
        var bt601 = Pixel(target);
        AssertNear(Decode(81, 90, 240, 1.402, 0.344136, 0.714136, 1.772), bt601, "the BT.601 conversion");

        yuv.Coefficients = YuvCoefficients.Bt709;
        Draw(renderer, yuv, target);
        AssertNear(Decode(81, 90, 240, 1.5748, 0.187324, 0.468124, 1.8556), Pixel(target), "the BT.709 conversion after the change");

        yuv.Dispose();
        target.Destroy();
        source.Destroy();
    }

    private static unsafe void Fill(in DmabufAttributes attributes, byte y, byte u, byte v)
    {
        var size = (nuint)(attributes.Offsets[1] + (attributes.Strides[1] * 8));
        var map = mmap(0, size, 3, 1, attributes.Fds[0], 0);
        Assert.NotEqual(-1, map);
        try
        {
            for (var row = 0; row < 16; row++)
            {
                new Span<byte>((byte*)map + attributes.Offsets[0] + (row * attributes.Strides[0]), 16).Fill(y);
            }

            for (var row = 0; row < 8; row++)
            {
                var chroma = (byte*)map + attributes.Offsets[1] + (row * attributes.Strides[1]);
                for (var x = 0; x < 8; x++)
                {
                    chroma[x * 2] = u;
                    chroma[(x * 2) + 1] = v;
                }
            }
        }
        finally
        {
            _ = munmap(map, size);
        }
    }

    private static void Draw(GlRenderer renderer, ITexture texture, MemoryBuffer target)
    {
        var pass = renderer.BeginBufferPass(target, new RenderPassOptions());
        pass.AddTexture(texture, new TextureRenderOptions { DstBox = new Box(0, 0, 16, 16) });
        Assert.True(pass.Submit());
    }

    private static (int R, int G, int B) Decode(int y, int u, int v, double rv, double gu, double gv, double bu)
    {
        var luma = (y - 16) * 255.0 / 219.0;
        var cb = (u - 128) * 255.0 / 224.0;
        var cr = (v - 128) * 255.0 / 224.0;
        static int Clamp(double value) => (int)Math.Round(Math.Clamp(value, 0, 255));
        return (Clamp(luma + (rv * cr)), Clamp(luma - (gu * cb) - (gv * cr)), Clamp(luma + (bu * cb)));
    }

    private static (int R, int G, int B) Pixel(MemoryBuffer buffer)
    {
        Assert.True(buffer.BeginDataAccess(BufferDataAccess.Read, out var view));
        try
        {
            unsafe
            {
                var value = *(uint*)(view.Data + (8 * view.Stride) + (8 * 4));
                return ((int)((value >> 16) & 0xFF), (int)((value >> 8) & 0xFF), (int)(value & 0xFF));
            }
        }
        finally
        {
            buffer.EndDataAccess();
        }
    }

    private static void AssertNear((int R, int G, int B) expected, (int R, int G, int B) actual, string what)
    {
        Assert.True(
            Math.Abs(expected.R - actual.R) <= 3 && Math.Abs(expected.G - actual.G) <= 3 && Math.Abs(expected.B - actual.B) <= 3,
            $"{what}: expected {expected}, got {actual}");
    }
}
