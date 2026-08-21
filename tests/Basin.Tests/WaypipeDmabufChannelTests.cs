using System.Buffers.Binary;
using Basin.Diagnostics;
using Basin.Transport.Waypipe;
using Wayland.Server;
using Xunit;

namespace Basin.Tests;

public sealed class WaypipeDmabufChannelTests
{
    private const uint RegistryId = 2;
    private const uint DmabufId = 3;
    private const uint CompositorId = 4;
    private const uint ParamsId = 5;
    private const uint BufferId = 6;
    private const uint SurfaceId = 7;
    private const int RemoteId = 77;

    private sealed class ChannelCompositor : IDisposable
    {
        public ChannelCompositor(bool gpu)
            : this(new WaypipeChannelOptions { CarriesDmabuf = gpu }, WaypipeVideoCodec.None)
        {
        }

        public ChannelCompositor(WaypipeChannelOptions options, WaypipeVideoCodec video)
        {
            BasinCounters.Reset();
            Display = WlServerDisplay.Create(new ManagedTransport());
            Loop = new WaylandEventLoop(Display);
            Buffers = new ClientBufferRegistry();
            Compositor = new CompositorGlobal(Display, Buffers);
            Compositor.SurfaceCreated += Surfaces.Add;
            Dmabuf = new LinuxDmabufGlobal(
                Display,
                Buffers,
                WaypipeGlobals.ChannelFormats,
                WaypipeGlobals.SyntheticMainDevice,
                compositor: Compositor);
            Peer = new LoopbackChannel(WaypipeCompression.None, options);
            Peer.Channel.Ended += failure =>
            {
                Failure = failure;
                EndedEvent.Set();
            };
            Display.CreateClient(Peer.Channel.Transport);
            Peer.SendConnectionHeader(WaypipeCompression.None, video);
        }

        public WlServerDisplay Display { get; }

        public WaylandEventLoop Loop { get; }

        public ClientBufferRegistry Buffers { get; }

        public CompositorGlobal Compositor { get; }

        public LinuxDmabufGlobal Dmabuf { get; }

        public LoopbackChannel Peer { get; }

        public List<Surface> Surfaces { get; } = [];

        public List<ManagedWire.WireMessage> Events { get; } = [];

        public Exception? Failure { get; private set; }

        public ManualResetEventSlim EndedEvent { get; } = new(false);

        public void SendFrame(WaypipeMessageType type, ReadOnlySpan<byte> body)
        {
            var length = 4 + body.Length;
            var message = new byte[WaypipeWire.Padded(length)];
            BinaryPrimitives.WriteUInt32LittleEndian(message, WaypipeWire.Header(type, length));
            body.CopyTo(message.AsSpan(4));
            Peer.Send(message);
        }

        public void SendWire(byte[] wire, int fds = 0)
        {
            if (fds > 0)
            {
                var header2 = BinaryPrimitives.ReadUInt32LittleEndian(wire.AsSpan(4));
                BinaryPrimitives.WriteUInt32LittleEndian(wire.AsSpan(4), WaypipeWire.TagFdCount(header2, fds));
            }

            SendFrame(WaypipeMessageType.Protocol, wire);
        }

        public void PumpUntil(Func<bool> condition, int millis = 5000)
        {
            var deadline = Environment.TickCount64 + millis;
            while (Environment.TickCount64 < deadline)
            {
                Loop.Dispatch(0);
                Display.FlushClients();
                foreach (var (type, body) in Peer.ReadFrames(20))
                {
                    if (type == WaypipeMessageType.Protocol)
                    {
                        ManagedWire.ParseInto(Events, body, body.Length);
                    }
                }

                if (condition())
                {
                    return;
                }

                Thread.Sleep(5);
            }

            Assert.Fail("the condition never came true inside the timeout");
        }

        public ManagedWire.WireMessage? ProtocolError()
        {
            foreach (var message in Events)
            {
                if (message.ObjectId == 1 && message.Opcode == 0)
                {
                    return message;
                }
            }

            return null;
        }

        public void BindGlobals()
        {
            SendWire(ManagedWire.Request(1, 1, RegistryId));
            uint dmabufName = 0, compositorName = 0;
            PumpUntil(() =>
            {
                foreach (var message in Events.Where(e => e.ObjectId == RegistryId && e.Opcode == 0))
                {
                    var iface = message.StringAt(4, out _);
                    if (iface == "zwp_linux_dmabuf_v1")
                    {
                        dmabufName = message.UintAt(0);
                    }
                    else if (iface == "wl_compositor")
                    {
                        compositorName = message.UintAt(0);
                    }
                }

                return dmabufName != 0 && compositorName != 0;
            });

            SendWire(ManagedWire.Request(RegistryId, 0, dmabufName, "zwp_linux_dmabuf_v1", 4u, DmabufId));
            SendWire(ManagedWire.Request(RegistryId, 0, compositorName, "wl_compositor", 4u, CompositorId));
        }

        public void Dispose()
        {
            Peer.Dispose();
            Loop.Dispatch(0);
            Dmabuf.Dispose();
            Compositor.Dispose();
            Display.Dispose();
            EndedEvent.Dispose();
        }
    }

    private static byte[] OpenBody(int remoteId, uint declaredSize, int width, int height, uint fourcc, int planes = 1)
    {
        var body = new byte[8 + 64];
        BinaryPrimitives.WriteInt32LittleEndian(body, remoteId);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(4), declaredSize);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(8), width);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(12), height);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(16), fourcc);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(20), planes);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(40), (uint)(width * 4));
        body[64] = 1;
        return body;
    }

    [Fact]
    public void A_filled_dmabuf_reaches_the_surface_and_a_diff_updates_it()
    {
        const int width = 16, height = 8, stride = width * 4;
        using var host = new ChannelCompositor(gpu: true);

        host.SendFrame(WaypipeMessageType.OpenDmabuf, OpenBody(RemoteId, height * stride, width, height, (uint)DrmFormat.Xrgb8888));

        var pattern = new byte[height * stride];
        for (var i = 0; i < pattern.Length; i++)
        {
            pattern[i] = (byte)(i ^ 0xA5);
        }

        var fill = new byte[12 + pattern.Length];
        BinaryPrimitives.WriteInt32LittleEndian(fill, RemoteId);
        BinaryPrimitives.WriteInt32LittleEndian(fill.AsSpan(4), 0);
        BinaryPrimitives.WriteInt32LittleEndian(fill.AsSpan(8), pattern.Length);
        pattern.CopyTo(fill.AsSpan(12));
        host.SendFrame(WaypipeMessageType.BufferFill, fill);

        host.BindGlobals();
        host.SendWire(ManagedWire.Request(DmabufId, 1, ParamsId));

        var inject = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(inject, RemoteId);
        host.SendFrame(WaypipeMessageType.InjectRIDs, inject);
        host.SendWire(ManagedWire.Request(ParamsId, 1, 0u, 0u, (uint)stride, 0u, 0u), fds: 1);
        host.SendWire(ManagedWire.Request(ParamsId, 3, BufferId, width, height, (uint)DrmFormat.Xrgb8888, 0u));
        host.SendWire(ManagedWire.Request(CompositorId, 0, SurfaceId));
        host.SendWire(ManagedWire.Request(SurfaceId, 1, BufferId, 0, 0));
        host.SendWire(ManagedWire.Request(SurfaceId, 6));

        host.PumpUntil(() => host.Surfaces.Count == 1 && host.Surfaces[0].Current.Buffer is not null);
        Assert.Null(host.ProtocolError());

        var buffer = Assert.IsType<RemoteImageBuffer>(host.Surfaces[0].Current.Buffer);
        Assert.True(buffer.BeginDataAccess(BufferDataAccess.Read, out var view));
        unsafe
        {
            var pixels = new ReadOnlySpan<byte>((void*)view.Data, pattern.Length);
            Assert.True(pixels.SequenceEqual(pattern));
        }

        var diff = new byte[12 + 8 + 4];
        BinaryPrimitives.WriteInt32LittleEndian(diff, RemoteId);
        BinaryPrimitives.WriteInt32LittleEndian(diff.AsSpan(4), 12);
        BinaryPrimitives.WriteInt32LittleEndian(diff.AsSpan(8), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(diff.AsSpan(12), 8);
        BinaryPrimitives.WriteUInt32LittleEndian(diff.AsSpan(16), 9);
        diff[20] = 1;
        diff[21] = 2;
        diff[22] = 3;
        diff[23] = 4;
        host.SendFrame(WaypipeMessageType.BufferDiff, diff);

        host.PumpUntil(() =>
        {
            unsafe
            {
                return *((byte*)view.Data + 32) == 1 && *((byte*)view.Data + 35) == 4;
            }
        });

        buffer.EndDataAccess();
    }

    [Fact]
    public void A_video_stream_lands_on_the_surface_through_a_stub_decoder()
    {
        const int width = 16, height = 8, stride = width * 4;
        var stub = new WaypipeVideoTests.StubVideoDecoder(Basin.Capabilities.VideoCodec.H264) { FillValue = 0x5c };
        using var host = new ChannelCompositor(
            new WaypipeChannelOptions { CarriesDmabuf = true, AcceptsVideo = true, VideoDecoder = stub },
            WaypipeVideoCodec.H264);

        var open = new byte[12 + 64];
        BinaryPrimitives.WriteInt32LittleEndian(open, RemoteId);
        BinaryPrimitives.WriteUInt32LittleEndian(open.AsSpan(8), 0u);
        BinaryPrimitives.WriteInt32LittleEndian(open.AsSpan(12), width);
        BinaryPrimitives.WriteInt32LittleEndian(open.AsSpan(16), height);
        BinaryPrimitives.WriteUInt32LittleEndian(open.AsSpan(20), (uint)DrmFormat.Xrgb8888);
        BinaryPrimitives.WriteInt32LittleEndian(open.AsSpan(24), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(open.AsSpan(44), (uint)stride);
        open[68] = 1;
        host.SendFrame(WaypipeMessageType.OpenDmaVidDstV2, open);

        var packet = new byte[4 + 6];
        BinaryPrimitives.WriteInt32LittleEndian(packet, RemoteId);
        "basin!"u8.CopyTo(packet.AsSpan(4));
        host.SendFrame(WaypipeMessageType.SendDmaVidPacket, packet);

        host.BindGlobals();
        host.SendWire(ManagedWire.Request(DmabufId, 1, ParamsId));

        var inject = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(inject, RemoteId);
        host.SendFrame(WaypipeMessageType.InjectRIDs, inject);
        host.SendWire(ManagedWire.Request(ParamsId, 1, 0u, 0u, (uint)stride, 0u, 0u), fds: 1);
        host.SendWire(ManagedWire.Request(ParamsId, 3, BufferId, width, height, (uint)DrmFormat.Xrgb8888, 0u));
        host.SendWire(ManagedWire.Request(CompositorId, 0, SurfaceId));
        host.SendWire(ManagedWire.Request(SurfaceId, 1, BufferId, 0, 0));
        host.SendWire(ManagedWire.Request(SurfaceId, 6));

        host.PumpUntil(() => host.Surfaces.Count == 1 && host.Surfaces[0].Current.Buffer is not null);
        Assert.Null(host.ProtocolError());
        Assert.Equal("basin!"u8.ToArray(), Assert.Single(stub.Packets));

        var buffer = Assert.IsType<RemoteImageBuffer>(host.Surfaces[0].Current.Buffer);
        Assert.True(buffer.BeginDataAccess(BufferDataAccess.Read, out var view));
        unsafe
        {
            var pixels = new ReadOnlySpan<byte>((void*)view.Data, height * stride);
            foreach (var value in pixels)
            {
                Assert.Equal(0x5c, value);
            }
        }

        buffer.EndDataAccess();
    }

    [Fact]
    public void A_lying_slice_ends_the_channel_and_leaves_the_fd_table_at_baseline()
    {
        Console.Out.Write(string.Empty);
        Console.Error.Write(string.Empty);
        Console.Out.Flush();
        Console.Error.Flush();
        using (var warm = new ChannelCompositor(gpu: true))
        {
        }

        var baseline = FdSnapshot.Take();
        using (var host = new ChannelCompositor(gpu: true))
        {
            host.SendFrame(
                WaypipeMessageType.OpenDmabuf,
                OpenBody(RemoteId, 8 * 32, 8, 8, (uint)DrmFormat.Xrgb8888, planes: 2));
            Assert.True(
                host.EndedEvent.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken),
                "the channel never ended");
            var failure = Assert.IsType<WaypipeException>(host.Failure);
            Assert.Contains("planes", failure.Message, StringComparison.Ordinal);
            Assert.Equal(0, host.Peer.Channel.Engine.LiveRemoteIds);
        }

        Assert.Empty(FdSnapshot.Diff(baseline, FdSnapshot.Take()));
    }
}
