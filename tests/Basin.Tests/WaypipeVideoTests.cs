using Basin.Capabilities;
using Basin.Transport.Waypipe;
using Xunit;

namespace Basin.Tests;

public sealed class WaypipeVideoTests
{
    internal sealed class StubVideoDecoder : IVideoDecoder
    {
        private readonly VideoCodec[] _supported;

        public StubVideoDecoder(params VideoCodec[] supported) => _supported = supported;

        public List<(VideoCodec Codec, int Width, int Height, DrmFormat Format)> Opened { get; } = [];

        public List<byte[]> Packets { get; } = [];

        public byte FillValue { get; set; } = 0x77;

        public bool Supports(VideoCodec codec) => _supported.Contains(codec);

        public IVideoDecodeSession Open(VideoCodec codec, int width, int height, DrmFormat format)
        {
            Opened.Add((codec, width, height, format));
            return new StubSession(this, width, height);
        }

        private sealed class StubSession : IVideoDecodeSession
        {
            private readonly StubVideoDecoder _owner;
            private readonly int _width;
            private readonly int _height;

            internal StubSession(StubVideoDecoder owner, int width, int height)
            {
                _owner = owner;
                _width = width;
                _height = height;
            }

            public bool Decode(ReadOnlySpan<byte> packet, nint destination, int stride)
            {
                _owner.Packets.Add(packet.ToArray());
                unsafe
                {
                    for (var y = 0; y < _height; y++)
                    {
                        new Span<byte>((void*)(destination + (y * stride)), _width * 4).Fill(_owner.FillValue);
                    }
                }

                return true;
            }

            public void Dispose()
            {
            }
        }
    }

    private static Exception? EndChannel(
        WaypipeChannelOptions options, WaypipeVideoCodec video, bool close = false)
    {
        using var peer = new LoopbackChannel(WaypipeCompression.None, options);
        Exception? failure = null;
        using var ended = new ManualResetEventSlim(false);
        peer.Channel.Ended += ex =>
        {
            failure = ex;
            ended.Set();
        };
        peer.SendConnectionHeader(WaypipeCompression.None, video);
        if (close)
        {
            Span<byte> frame = stackalloc byte[8];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(
                frame, WaypipeWire.Header(WaypipeMessageType.Close, 8));
            peer.Send(frame);
        }

        Assert.True(ended.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken), "the channel never ended");
        return failure;
    }

    [Fact]
    public void The_header_codes_and_the_message_codes_are_two_different_tables()
    {
        Span<byte> header = stackalloc byte[WaypipeWire.ConnectionHeaderLength];
        foreach (var (codec, code) in new[]
        {
            (WaypipeVideoCodec.None, 1u),
            (WaypipeVideoCodec.Vp9, 2u),
            (WaypipeVideoCodec.H264, 3u),
            (WaypipeVideoCodec.Av1, 4u),
        })
        {
            WaypipeWire.WriteConnectionHeader(header, WaypipeWire.ProtocolVersion, WaypipeCompression.None, video: codec);
            var lead = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(header);
            Assert.Equal(code, (lead >> 11) & 0x7);
            var parsed = WaypipeWire.ParseConnectionHeader(header, WaypipeCompression.None);
            Assert.Equal(codec, parsed.VideoCodec);
        }

        Assert.Equal(0, (int)Basin.Transport.Waypipe.VideoFormat.H264);
        Assert.Equal(1, (int)Basin.Transport.Waypipe.VideoFormat.Vp9);
        Assert.Equal(2, (int)Basin.Transport.Waypipe.VideoFormat.Av1);
    }

    [Fact]
    public void A_header_naming_a_codec_with_no_decoder_ends_the_channel_before_any_frame()
    {
        var failure = EndChannel(new WaypipeChannelOptions { CarriesDmabuf = true }, WaypipeVideoCodec.H264);
        var error = Assert.IsType<WaypipeException>(failure);
        Assert.Contains("H264", error.Message, StringComparison.Ordinal);
        Assert.Contains("--video", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_decoder_that_cannot_handle_the_named_codec_ends_the_channel_naming_what_it_can()
    {
        var options = new WaypipeChannelOptions
        {
            CarriesDmabuf = true,
            AcceptsVideo = true,
            VideoDecoder = new StubVideoDecoder(VideoCodec.Vp9),
        };
        var failure = EndChannel(options, WaypipeVideoCodec.H264);
        var error = Assert.IsType<WaypipeException>(failure);
        Assert.Contains("H264", error.Message, StringComparison.Ordinal);
        Assert.Contains("Vp9", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_header_the_decoder_can_serve_passes_and_the_channel_runs()
    {
        var options = new WaypipeChannelOptions
        {
            CarriesDmabuf = true,
            AcceptsVideo = true,
            VideoDecoder = new StubVideoDecoder(VideoCodec.H264),
        };
        Assert.Null(EndChannel(options, WaypipeVideoCodec.H264, close: true));
    }

    private static byte[] VideoOpenBody(int remoteId, uint flags, int width, int height, uint fourcc)
    {
        var body = new byte[12 + 64];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(body, remoteId);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(4), 0);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(8), flags);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(12), width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(16), height);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(20), fourcc);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(24), 1);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(44), (uint)(width * 4));
        body[68] = 1;
        return body;
    }

    private static (WaypipeClientTransport Transport, WaypipeEngine Engine, StubVideoDecoder Stub) VideoEngine()
    {
        var stub = new StubVideoDecoder(VideoCodec.H264, VideoCodec.Vp9);
        var transport = new WaypipeClientTransport();
        var engine = new WaypipeEngine(
            transport,
            WaypipeCompression.None,
            options: new WaypipeChannelOptions { CarriesDmabuf = true, AcceptsVideo = true, VideoDecoder = stub })
        {
            ExpectedVideoCodec = VideoCodec.H264,
        };
        return (transport, engine, stub);
    }

    [Fact]
    public void A_video_packet_reaches_the_stub_decoder_undecompressed_and_its_writes_land()
    {
        var (transport, engine, stub) = VideoEngine();
        using var _ = transport;
        using var __ = engine;

        engine.Apply(
            WaypipeMessageType.OpenDmaVidDstV2,
            VideoOpenBody(9, (uint)Basin.Transport.Waypipe.VideoFormat.H264, 16, 8, (uint)DrmFormat.Xrgb8888));
        Assert.Equal((VideoCodec.H264, 16, 8, DrmFormat.Xrgb8888), Assert.Single(stub.Opened));

        var packet = new byte[] { 9, 0, 0, 0, 0x1f, 0x2e, 0x3d, 0x4c, 0x5b };
        engine.Apply(WaypipeMessageType.SendDmaVidPacket, packet);

        Assert.Equal(new byte[] { 0x1f, 0x2e, 0x3d, 0x4c, 0x5b }, Assert.Single(stub.Packets));

        var image = Assert.IsAssignableFrom<IRemoteImage>(ImageOf(engine, 9));
        unsafe
        {
            var pixels = new ReadOnlySpan<byte>((void*)image.Pixels, 16 * 8 * 4);
            foreach (var value in pixels)
            {
                Assert.Equal(0x77, value);
            }
        }
    }

    [Fact]
    public void Every_buffer_opens_its_own_decode_session()
    {
        var (transport, engine, stub) = VideoEngine();
        using var _ = transport;
        using var __ = engine;

        engine.Apply(
            WaypipeMessageType.OpenDmaVidDstV2,
            VideoOpenBody(1, (uint)Basin.Transport.Waypipe.VideoFormat.H264, 16, 8, (uint)DrmFormat.Xrgb8888));
        engine.Apply(
            WaypipeMessageType.OpenDmaVidDstV2,
            VideoOpenBody(2, (uint)Basin.Transport.Waypipe.VideoFormat.H264, 16, 8, (uint)DrmFormat.Xrgb8888));

        Assert.Equal(2, stub.Opened.Count);
        Assert.Equal(2, engine.LiveRemoteIds);
    }

    [Fact]
    public void A_codec_that_disagrees_with_the_header_is_an_error_not_a_renegotiation()
    {
        var (transport, engine, _) = VideoEngine();
        using var t = transport;
        using var e = engine;

        var error = Assert.Throws<WaypipeException>(() => engine.Apply(
            WaypipeMessageType.OpenDmaVidDstV2,
            VideoOpenBody(1, (uint)Basin.Transport.Waypipe.VideoFormat.Av1, 16, 8, (uint)DrmFormat.Xrgb8888)));
        Assert.Contains("H264", error.Message, StringComparison.Ordinal);
        Assert.Contains("Av1", error.Message, StringComparison.Ordinal);
    }

    private static object ImageOf(WaypipeEngine engine, int remoteId)
    {
        var field = typeof(WaypipeEngine).GetField(
            "_images", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var images = (System.Collections.IDictionary)field.GetValue(engine)!;
        return images[remoteId]!;
    }
}
