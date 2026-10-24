using Basin.Diagnostics;
using Basin.Render.Gl;
using Xunit;

namespace Basin.Tests;

public sealed class BufferCaptureTests
{
    [Fact]
    public void Png_round_trips_what_it_wrote()
    {
        var rgba = new byte[4 * 3 * 4];
        for (var i = 0; i < rgba.Length; i++)
        {
            rgba[i] = (byte)(i * 7);
        }

        var (decoded, width, height) = PngCodec.Decode(PngCodec.Encode(rgba, 4, 3));
        Assert.Equal(4, width);
        Assert.Equal(3, height);
        Assert.Equal(rgba, decoded);
    }

    public static TheoryData<DrmFormat, ulong, uint> FormatRows => new()
    {
        { DrmFormat.Argb8888, 0x40FF8040, 0xFF804040 },
        { DrmFormat.Xrgb8888, 0x00FF8040, 0xFF8040FF },
        { DrmFormat.Abgr8888, 0x404080FF, 0xFF804040 },
        { DrmFormat.Xbgr8888, 0x004080FF, 0xFF8040FF },
        { DrmFormat.Argb2101010, (1u << 30) | (0x3FFu << 20) | (0x200u << 10) | 0x100u, 0xFF804055 },
        { DrmFormat.Xrgb2101010, (1u << 30) | (0x3FFu << 20) | (0x200u << 10) | 0x100u, 0xFF8040FF },
        { DrmFormat.Abgr2101010, (1u << 30) | (0x100u << 20) | (0x200u << 10) | 0x3FFu, 0xFF804055 },
        { DrmFormat.Xbgr2101010, (1u << 30) | (0x100u << 20) | (0x200u << 10) | 0x3FFu, 0xFF8040FF },
        { DrmFormat.Rgb565, (31u << 11) | (32u << 5) | 8u, 0xFF8242FF },
        { DrmFormat.Abgr16161616f, Halves(2.0f, 128 / 255f, 64 / 255f, 0.25f), 0xFF804040 },
        { DrmFormat.Xbgr16161616f, Halves(-1.0f, 128 / 255f, 64 / 255f, 0f), 0x008040FF },
    };

    [Theory]
    [MemberData(nameof(FormatRows))]
    public void Each_format_unpacks_to_rgba(DrmFormat format, ulong pixel, uint expected)
    {
        var buffer = new MemoryBuffer(2, 2, format);
        try
        {
            var size = format.BytesPerPixel();
            Assert.True(buffer.BeginDataAccess(BufferDataAccess.Write, out var view));
            unsafe
            {
                for (var y = 0; y < 2; y++)
                {
                    for (var x = 0; x < 2; x++)
                    {
                        var at = (byte*)(view.Data + (y * view.Stride) + (x * size));
                        for (var b = 0; b < size; b++)
                        {
                            at[b] = (byte)(pixel >> (b * 8));
                        }
                    }
                }
            }

            buffer.EndDataAccess();

            var rgba = BufferCapture.ReadRgba(buffer);
            Assert.Equal(2 * 2 * 4, rgba.Length);
            for (var i = 0; i < rgba.Length; i += 4)
            {
                var actual = ((uint)rgba[i] << 24) | ((uint)rgba[i + 1] << 16) | ((uint)rgba[i + 2] << 8) | rgba[i + 3];
                Assert.Equal(expected.ToString("X8"), actual.ToString("X8"));
            }
        }
        finally
        {
            buffer.Destroy();
        }
    }

    [Fact]
    public void A_format_the_direct_read_cannot_unpack_is_refused()
    {
        var nv12 = new RawBuffer(4, 4, DrmFormat.Nv12, 4);
        try
        {
            Assert.False(BufferCapture.TryReadRgba(nv12, out _));
            var error = Assert.Throws<InvalidOperationException>(() => BufferCapture.ReadRgba(nv12));
            Assert.Contains("Nv12", error.Message);
        }
        finally
        {
            nv12.Destroy();
        }
    }

    [Fact]
    public void A_stride_shorter_than_a_row_is_refused()
    {
        var narrow = new RawBuffer(4, 4, DrmFormat.Argb8888, 8);
        try
        {
            Assert.False(BufferCapture.TryReadRgba(narrow, out _));
        }
        finally
        {
            narrow.Destroy();
        }
    }

    [Fact]
    public void A_mapped_abgr_buffer_reads_back_in_rgba_order_through_the_renderer_overload()
    {
        Assert.SkipWhen(!File.Exists(CompositorTestHost.RenderNodePath), "no render node");

        using var gl = new GlRenderer(CompositorTestHost.RenderNodePath);
        var source = new MemoryBuffer(64, 64, DrmFormat.Abgr8888);
        try
        {
            Assert.True(source.BeginDataAccess(BufferDataAccess.Write, out var view));
            unsafe
            {
                for (var y = 0; y < 64; y++)
                {
                    var row = (uint*)(view.Data + (y * view.Stride));
                    for (var x = 0; x < 64; x++)
                    {
                        row[x] = x < 32 ? 0xFF0000FFu : 0xFF00FF00u;
                    }
                }
            }

            source.EndDataAccess();

            Assert.True(BufferCapture.TryReadRgba(source, gl, out var rgba));
            var left = ((32 * 64) + 8) * 4;
            var right = ((32 * 64) + 56) * 4;
            Assert.Equal([0xFF, 0x00, 0x00, 0xFF], rgba![left..(left + 4)]);
            Assert.Equal([0x00, 0xFF, 0x00, 0xFF], rgba[right..(right + 4)]);
        }
        finally
        {
            source.Destroy();
        }
    }

    [Fact]
    public void A_mapped_yuv_buffer_reads_back_through_the_renderer_copy()
    {
        Assert.SkipWhen(!File.Exists(CompositorTestHost.RenderNodePath), "no render node");

        using var gl = new GlRenderer(CompositorTestHost.RenderNodePath);
        using var allocator = gl.Device.CreateAllocator();
        var source = allocator.Allocate(64, 64, DrmFormat.Nv12, [DrmFormatSet.ModifierLinear], BufferUse.Render);
        Assert.SkipWhen(source is null, "GBM declined a linear Nv12 buffer");
        try
        {
            Assert.True(source!.TryGetDmabuf(out var planes));
            Assert.SkipWhen(!source.BeginDataAccess(BufferDataAccess.Write, out var view), "the Nv12 buffer has no CPU mapping");
            unsafe
            {
                var chroma = view.Data - (nint)planes.Offsets[0] + (nint)planes.Offsets[1];
                new Span<byte>((void*)view.Data, view.Stride * 64).Fill(235);
                new Span<byte>((void*)chroma, (int)planes.Strides[1] * 32).Fill(128);
            }

            source.EndDataAccess();

            Assert.False(BufferCapture.TryReadRgba(source, out _));
            Assert.True(BufferCapture.TryReadRgba(source, gl, out var rgba));
            Assert.Equal(64 * 64 * 4, rgba!.Length);
            var middle = ((32 * 64) + 32) * 4;
            Assert.InRange(rgba[middle], 0xF0, 0xFF);
            Assert.InRange(rgba[middle + 1], 0xF0, 0xFF);
            Assert.InRange(rgba[middle + 2], 0xF0, 0xFF);
        }
        finally
        {
            (source as BufferBase)?.Destroy();
        }
    }

    private static ulong Halves(float r, float g, float b, float a) =>
        BitConverter.HalfToUInt16Bits((Half)r) |
        ((ulong)BitConverter.HalfToUInt16Bits((Half)g) << 16) |
        ((ulong)BitConverter.HalfToUInt16Bits((Half)b) << 32) |
        ((ulong)BitConverter.HalfToUInt16Bits((Half)a) << 48);

    private sealed class RawBuffer : BufferBase
    {
        private readonly byte[] _storage;
        private readonly int _stride;
        private System.Runtime.InteropServices.GCHandle _pin;

        public RawBuffer(int width, int height, DrmFormat format, int stride)
            : base(width, height)
        {
            Format = format;
            _stride = stride;
            _storage = new byte[stride * height * 2];
        }

        public override DrmFormat Format { get; }

        protected override bool TryMap(BufferDataAccess access, out BufferDataView view)
        {
            _pin = System.Runtime.InteropServices.GCHandle.Alloc(_storage, System.Runtime.InteropServices.GCHandleType.Pinned);
            view = new BufferDataView(_pin.AddrOfPinnedObject(), _stride, Format);
            return true;
        }

        protected override void Unmap() => _pin.Free();

        protected override void OnFreeStorage()
        {
        }
    }

    [Fact]
    public void Unmappable_buffer_reads_back_through_the_renderer()
    {
        Assert.SkipWhen(!File.Exists(CompositorTestHost.RenderNodePath), "no render node");

        using var gl = new GlRenderer(CompositorTestHost.RenderNodePath);
        using var allocator = gl.Device.CreateAllocator();
        var modifiers = allocator.Formats.ModifiersOf(DrmFormat.Xrgb8888).ToArray();
        var source = allocator.Allocate(64, 64, DrmFormat.Xrgb8888, modifiers, BufferUse.Render)
            ?? allocator.Allocate(64, 64, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear], BufferUse.Render);
        Assert.SkipWhen(source is null, "GBM declined an Xrgb8888 render target");

        var fill = gl.BeginBufferPass(source!, new RenderPassOptions());
        fill.AddRect(new RenderColor(1, 0, 0, 1), new Box(0, 0, 64, 64));
        fill.AddRect(new RenderColor(0, 1, 0, 1), new Box(32, 0, 32, 64));
        Assert.True(fill.Submit());

        if (source!.BeginDataAccess(BufferDataAccess.Read, out _))
        {
            source.EndDataAccess();
            Assert.Skip("gbm buffers are CPU-mappable on this driver; the renderer path is not what would run");
        }

        Assert.False(BufferCapture.TryReadRgba(source, out _));

        Assert.True(BufferCapture.TryReadRgba(source, gl, out var rgba));
        Assert.Equal(64 * 64 * 4, rgba!.Length);
        var left = ((32 * 64) + 8) * 4;
        var right = ((32 * 64) + 56) * 4;
        Assert.Equal(0xFF, rgba[left]);
        Assert.Equal(0x00, rgba[left + 1]);
        Assert.Equal(0x00, rgba[right]);
        Assert.Equal(0xFF, rgba[right + 1]);
        Assert.Equal(0xFF, rgba[left + 3]);
    }
}
