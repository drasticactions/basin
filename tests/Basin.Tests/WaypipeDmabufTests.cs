using System.Buffers.Binary;
using Basin.Transport.Waypipe;
using Xunit;

namespace Basin.Tests;

public sealed class WaypipeDmabufTests
{
    private static readonly WaypipeChannelOptions Dmabuf = new() { CarriesDmabuf = true };

    private static void Apply(WaypipeEngine engine, WaypipeMessageType type, byte[] body) =>
        engine.Apply(type, body);

    private static byte[] Slice(
        int width, int height, uint fourcc, int planes = 1, uint? stride = null, ulong modifier = 0)
    {
        var bytes = new byte[64];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, width);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), height);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), fourcc);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), planes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(32), stride ?? (uint)(width * 4));
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(48), modifier);
        bytes[56] = 1;
        return bytes;
    }

    private static byte[] OpenBody(int remoteId, uint declaredSize, byte[] slice)
    {
        var body = new byte[8 + 64];
        BinaryPrimitives.WriteInt32LittleEndian(body, remoteId);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(4), declaredSize);
        slice.CopyTo(body.AsSpan(8));
        return body;
    }

    private static byte[] FillBody(int remoteId, int start, int end, ReadOnlySpan<byte> contents)
    {
        var body = new byte[12 + contents.Length];
        BinaryPrimitives.WriteInt32LittleEndian(body, remoteId);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(4), start);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(8), end);
        contents.CopyTo(body.AsSpan(12));
        return body;
    }

    [Fact]
    public void An_open_dmabuf_backs_a_linear_region_and_mints_an_image()
    {
        using var transport = new WaypipeClientTransport();
        using var engine = new WaypipeEngine(transport, WaypipeCompression.None, options: Dmabuf);

        const int width = 64, height = 32, stride = width * 4;
        Apply(engine, WaypipeMessageType.OpenDmabuf, OpenBody(7, height * stride, Slice(width, height, (uint)DrmFormat.Xrgb8888)));
        Assert.Equal(1, engine.LiveRemoteIds);

        var pattern = new byte[height * stride];
        for (var i = 0; i < pattern.Length; i++)
        {
            pattern[i] = (byte)(i * 7);
        }

        Apply(engine, WaypipeMessageType.BufferFill, FillBody(7, 0, pattern.Length, pattern));

        var inject = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(inject, 7);
        Apply(engine, WaypipeMessageType.InjectRIDs, inject);

        var request = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(request, 4);
        BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(4), (16u << 16) | (1u << 11) | 0u);
        Apply(engine, WaypipeMessageType.Protocol, request);

        var slots = new int[4];
        var (_, fds) = transport.TryReadNonBlocking(new byte[64], Array.Empty<byte>(), slots, Array.Empty<int>());
        Assert.Equal(1, fds);

        var image = Assert.IsAssignableFrom<IRemoteImage>(transport.Slots.Resolve<object>(slots[0]));
        Assert.Equal(width, image.Width);
        Assert.Equal(height, image.Height);
        Assert.Equal(stride, image.Stride);
        Assert.Equal(DrmFormat.Xrgb8888, image.Format);
        unsafe
        {
            var pixels = new ReadOnlySpan<byte>((void*)image.Pixels, pattern.Length);
            Assert.True(pixels.SequenceEqual(pattern));
        }

        transport.CloseFd(slots[0]);
        Assert.Equal(0, engine.LiveRemoteIds);
    }

    [Fact]
    public void A_dmabuf_diff_updates_only_the_named_bytes()
    {
        using var transport = new WaypipeClientTransport();
        using var engine = new WaypipeEngine(transport, WaypipeCompression.None, options: Dmabuf);

        Apply(engine, WaypipeMessageType.OpenDmabuf, OpenBody(3, 8 * 32, Slice(8, 8, (uint)DrmFormat.Xrgb8888)));

        var diff = new byte[12 + 8 + 4];
        BinaryPrimitives.WriteInt32LittleEndian(diff, 3);
        BinaryPrimitives.WriteInt32LittleEndian(diff.AsSpan(4), 12);
        BinaryPrimitives.WriteInt32LittleEndian(diff.AsSpan(8), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(diff.AsSpan(12), 4);
        BinaryPrimitives.WriteUInt32LittleEndian(diff.AsSpan(16), 5);
        diff[20] = 0xde;
        diff[21] = 0xad;
        diff[22] = 0xbe;
        diff[23] = 0xef;
        Apply(engine, WaypipeMessageType.BufferDiff, diff);

        var image = (WaypipeImage)ImageOf(engine, 3);
        var span = image.Region.Span;
        Assert.Equal(0, span[15]);
        Assert.Equal(0xde, span[16]);
        Assert.Equal(0xef, span[19]);
        Assert.Equal(0, span[20]);
    }

    [Theory]
    [InlineData(2, 0x34325258u, 256u, 32u * 256, "planes")]
    [InlineData(1, 0x12345678u, 256u, 32u * 256, "fourcc")]
    [InlineData(1, 0x34325258u, 100u, 32u * 100, "stride")]
    [InlineData(1, 0x34325258u, 256u, 999u, "declares")]
    public void A_slice_that_lies_ends_the_channel_with_a_named_error(
        int planes, uint fourcc, uint stride, uint declared, string names)
    {
        using var transport = new WaypipeClientTransport();
        using var engine = new WaypipeEngine(transport, WaypipeCompression.None, options: Dmabuf);

        var body = OpenBody(9, declared, Slice(64, 32, fourcc, planes, stride));
        var error = Assert.Throws<WaypipeException>(() => Apply(engine, WaypipeMessageType.OpenDmabuf, body));
        Assert.Contains(names, error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, engine.LiveRemoteIds);
    }

    [Fact]
    public void A_zero_height_slice_is_refused()
    {
        using var transport = new WaypipeClientTransport();
        using var engine = new WaypipeEngine(transport, WaypipeCompression.None, options: Dmabuf);

        var error = Assert.Throws<WaypipeException>(
            () => Apply(engine, WaypipeMessageType.OpenDmabuf, OpenBody(9, 0, Slice(64, 0, (uint)DrmFormat.Xrgb8888))));
        Assert.Contains("64x0", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_channel_not_asked_for_dmabuf_still_refuses_and_names_the_flag()
    {
        using var transport = new WaypipeClientTransport();
        using var engine = new WaypipeEngine(transport, WaypipeCompression.None);

        var body = OpenBody(1, 32u * 256, Slice(64, 32, (uint)DrmFormat.Xrgb8888));
        var error = Assert.Throws<WaypipeException>(() => Apply(engine, WaypipeMessageType.OpenDmabuf, body));
        Assert.Contains("--gpu", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Fifty_dmabuf_ids_created_and_closed_leave_nothing_live()
    {
        using var transport = new WaypipeClientTransport();
        using var engine = new WaypipeEngine(transport, WaypipeCompression.None, options: Dmabuf);

        for (var i = 1; i <= 50; i++)
        {
            Apply(engine, WaypipeMessageType.OpenDmabuf, OpenBody(i, 8 * 32, Slice(8, 8, (uint)DrmFormat.Argb8888)));

            var inject = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(inject, i);
            Apply(engine, WaypipeMessageType.InjectRIDs, inject);

            var request = new byte[16];
            BinaryPrimitives.WriteUInt32LittleEndian(request, 4);
            BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(4), (16u << 16) | (1u << 11) | 0u);
            Apply(engine, WaypipeMessageType.Protocol, request);

            var slots = new int[4];
            var (_, fds) = transport.TryReadNonBlocking(new byte[64], Array.Empty<byte>(), slots, Array.Empty<int>());
            Assert.Equal(1, fds);
            transport.CloseFd(slots[0]);
        }

        Assert.Equal(0, engine.LiveRemoteIds);
    }

    [Fact]
    public void The_globals_answer_follows_what_the_channel_was_asked_to_carry()
    {
        var withheld = new WaypipeGlobals(carriesDmabuf: false);
        Assert.False(withheld.Carries("zwp_linux_dmabuf_v1"));
        Assert.Contains("--gpu", withheld.WhyWithheld("zwp_linux_dmabuf_v1"), StringComparison.Ordinal);

        var carrying = new WaypipeGlobals(carriesDmabuf: true);
        Assert.True(carrying.Carries("zwp_linux_dmabuf_v1"));
        Assert.Null(carrying.WhyWithheld("zwp_linux_dmabuf_v1"));
        Assert.False(carrying.Carries("wp_presentation"));
        Assert.False(carrying.Carries("wp_linux_drm_syncobj_manager_v1"));
        Assert.False(carrying.Carries("wp_drm_lease_device_v1"));
        Assert.True(carrying.Carries("xdg_wm_base"));
    }

    private static IRemoteImage ImageOf(WaypipeEngine engine, int remoteId)
    {
        var field = typeof(WaypipeEngine).GetField(
            "_images", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var images = (Dictionary<int, WaypipeImage>)field.GetValue(engine)!;
        return images[remoteId];
    }
}
