using System.Buffers.Binary;
using Basin.Transport.Waypipe;
using K4os.Compression.LZ4;
using Wayland.Server;
using Wayland.Server.Shm;
using Xunit;

namespace Basin.Tests;

public sealed class WaypipeChannelTests
{
    private static byte[] Frame(WaypipeMessageType type, ReadOnlySpan<byte> body)
    {
        var length = 4 + body.Length;
        var message = new byte[WaypipeWire.Padded(length)];
        BinaryPrimitives.WriteUInt32LittleEndian(message, WaypipeWire.Header(type, length));
        body.CopyTo(message.AsSpan(4));
        return message;
    }

    private static byte[] Checkerboard(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = ((y * width) + x) * 4;
                var value = ((x / 8) + (y / 8)) % 2 == 0 ? (byte)0xff : (byte)0x20;
                pixels[offset] = value;
                pixels[offset + 1] = value;
                pixels[offset + 2] = value;
                pixels[offset + 3] = 0xff;
            }
        }

        return pixels;
    }

    private static byte[] Fill(int remoteId, int start, int end, ReadOnlySpan<byte> contents, WaypipeCompression how)
    {
        var payload = Pack(contents, how);
        var body = new byte[12 + payload.Length];
        BinaryPrimitives.WriteInt32LittleEndian(body, remoteId);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(4), start);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(8), end);
        payload.CopyTo(body.AsSpan(12));
        return Frame(WaypipeMessageType.BufferFill, body);
    }

    private static byte[] Diff(int remoteId, ReadOnlySpan<byte> diff, int trailing, WaypipeCompression how)
    {
        var payload = Pack(diff, how);
        var body = new byte[12 + payload.Length];
        BinaryPrimitives.WriteInt32LittleEndian(body, remoteId);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(4), diff.Length - trailing);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(8), trailing);
        payload.CopyTo(body.AsSpan(12));
        return Frame(WaypipeMessageType.BufferDiff, body);
    }

    private static byte[] Pack(ReadOnlySpan<byte> source, WaypipeCompression how)
    {
        switch (how)
        {
            case WaypipeCompression.Lz4:
            {
                var buffer = new byte[LZ4Codec.MaximumOutputSize(source.Length)];
                var written = LZ4Codec.Encode(source, buffer);
                return buffer.AsSpan(0, written).ToArray();
            }

            case WaypipeCompression.Zstd:
            {
                using var compressor = new ZstdSharp.Compressor();
                var buffer = new byte[ZstdSharp.Compressor.GetCompressBound(source.Length)];
                var written = compressor.Wrap(source, buffer);
                return buffer.AsSpan(0, written).ToArray();
            }

            default:
                return source.ToArray();
        }
    }

    private static void Apply(WaypipeEngine engine, byte[] frame)
    {
        var (length, type) = WaypipeWire.ParseHeader(BinaryPrimitives.ReadUInt32LittleEndian(frame));
        engine.Apply(type, frame.AsSpan(4, length - 4));
    }

    [Fact]
    public void A_frame_header_round_trips_its_type_and_length()
    {
        foreach (var type in Enum.GetValues<WaypipeMessageType>())
        {
            var header = WaypipeWire.Header(type, 4096);
            var (length, parsed) = WaypipeWire.ParseHeader(header);
            Assert.Equal(type, parsed);
            Assert.Equal(4096, length);
        }
    }

    [Fact]
    public void A_connection_header_round_trips_and_names_its_version()
    {
        Span<byte> bytes = stackalloc byte[WaypipeWire.ConnectionHeaderLength];
        WaypipeWire.WriteConnectionHeader(bytes, WaypipeWire.ProtocolVersion, WaypipeCompression.Lz4);
        var header = WaypipeWire.ParseConnectionHeader(bytes, WaypipeCompression.Lz4);

        Assert.Equal(WaypipeWire.ProtocolVersion, header.Version);
        Assert.Equal(WaypipeCompression.Lz4, header.Compression);
        Assert.True(header.RefusesDmabuf);
    }

    [Fact]
    public void A_compression_mismatch_is_refused_rather_than_negotiated()
    {
        Span<byte> bytes = stackalloc byte[WaypipeWire.ConnectionHeaderLength];
        WaypipeWire.WriteConnectionHeader(bytes, WaypipeWire.ProtocolVersion, WaypipeCompression.Zstd);

        var array = bytes.ToArray();
        var error = Assert.Throws<WaypipeException>(
            () => WaypipeWire.ParseConnectionHeader(array, WaypipeCompression.Lz4));
        Assert.Contains("compression", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_old_version_and_a_byte_order_mismatch_are_both_refused()
    {
        var old = new byte[WaypipeWire.ConnectionHeaderLength];
        WaypipeWire.WriteConnectionHeader(old, 0x0f, WaypipeCompression.Lz4);
        Assert.Throws<WaypipeException>(() => WaypipeWire.ParseConnectionHeader(old, WaypipeCompression.Lz4));

        var swapped = new byte[WaypipeWire.ConnectionHeaderLength];
        BinaryPrimitives.WriteUInt32LittleEndian(swapped, 1u << 31);
        Assert.Throws<WaypipeException>(() => WaypipeWire.ParseConnectionHeader(swapped, WaypipeCompression.Lz4));
    }

    [Fact]
    public void The_fd_count_tag_is_stripped_from_the_opcode_word()
    {
        var header2 = (12u << 16) | (1u << 11) | 0u;
        Assert.Equal(1, WaypipeWire.TaggedFdCount(header2));
        Assert.Equal(12, WaypipeWire.MessageLength(header2));

        var stripped = WaypipeWire.StripFdTag(header2);
        Assert.Equal(0, WaypipeWire.TaggedFdCount(stripped));
        Assert.Equal(0u, stripped & 0x7ff);
        Assert.Equal(12, WaypipeWire.MessageLength(stripped));

        Assert.Equal(header2, WaypipeWire.TagFdCount(stripped, 1));
    }

    [Theory]
    [InlineData(WaypipeCompression.None)]
    [InlineData(WaypipeCompression.Lz4)]
    [InlineData(WaypipeCompression.Zstd)]
    public void A_fill_then_a_diff_reconstructs_the_checkerboard_byte_for_byte(WaypipeCompression how)
    {
        const int width = 64;
        const int height = 32;
        var first = Checkerboard(width, height);
        var second = Checkerboard(width, height);
        for (var i = 0; i < 256; i++)
        {
            second[1024 + i] = (byte)(i ^ 0x5a);
        }

        using var transport = new WaypipeClientTransport();
        using var engine = new WaypipeEngine(transport, how);

        var open = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(open, 7);
        BinaryPrimitives.WriteInt32LittleEndian(open.AsSpan(4), first.Length);
        Apply(engine, Frame(WaypipeMessageType.OpenFile, open));
        Apply(engine, Fill(7, 0, first.Length, first, how));

        var diff = new byte[8 + 256];
        BinaryPrimitives.WriteUInt32LittleEndian(diff, 1024 / 4);
        BinaryPrimitives.WriteUInt32LittleEndian(diff.AsSpan(4), (1024 + 256) / 4);
        second.AsSpan(1024, 256).CopyTo(diff.AsSpan(8));
        Apply(engine, Diff(7, diff, trailing: 0, how));

        var slot = transport.Slots.Mint(Resolve(engine, 7));
        using var memory = new TokenSharedMemory(transport.Slots).Map(slot, second.Length);
        Assert.True(memory.Span.SequenceEqual(second));
    }

    [Fact]
    public void A_diff_carries_its_trailing_bytes_to_the_end_of_the_region()
    {
        using var transport = new WaypipeClientTransport();
        using var engine = new WaypipeEngine(transport, WaypipeCompression.None);

        var open = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(open, 3);
        BinaryPrimitives.WriteInt32LittleEndian(open.AsSpan(4), 14);
        Apply(engine, Frame(WaypipeMessageType.OpenFile, open));

        var diff = new byte[8 + 4 + 2];
        BinaryPrimitives.WriteUInt32LittleEndian(diff, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(diff.AsSpan(4), 1);
        diff[8] = 0xde;
        diff[9] = 0xad;
        diff[10] = 0xbe;
        diff[11] = 0xef;
        diff[12] = 0x11;
        diff[13] = 0x22;
        Apply(engine, Diff(3, diff, trailing: 2, WaypipeCompression.None));

        var region = Resolve(engine, 3);
        var span = region.Span;
        Assert.Equal(0xde, span[0]);
        Assert.Equal(0xef, span[3]);
        Assert.Equal(0x11, span[12]);
        Assert.Equal(0x22, span[13]);
    }

    [Fact]
    public void A_file_grows_and_keeps_what_it_held()
    {
        using var transport = new WaypipeClientTransport();
        using var engine = new WaypipeEngine(transport, WaypipeCompression.None);

        var open = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(open, 5);
        BinaryPrimitives.WriteInt32LittleEndian(open.AsSpan(4), 16);
        Apply(engine, Frame(WaypipeMessageType.OpenFile, open));
        Apply(engine, Fill(5, 0, 16, [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16], WaypipeCompression.None));

        var extend = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(extend, 5);
        BinaryPrimitives.WriteInt32LittleEndian(extend.AsSpan(4), 64);
        Apply(engine, Frame(WaypipeMessageType.ExtendFile, extend));

        var region = Resolve(engine, 5);
        Assert.Equal(64, region.Size);
        Assert.Equal(1, region.Span[0]);
        Assert.Equal(16, region.Span[15]);
        Assert.Equal(0, region.Span[16]);
    }

    [Fact]
    public void An_injected_remote_id_becomes_the_fd_slot_of_the_message_that_names_it()
    {
        using var transport = new WaypipeClientTransport();
        using var engine = new WaypipeEngine(transport, WaypipeCompression.None);

        var open = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(open, 9);
        BinaryPrimitives.WriteInt32LittleEndian(open.AsSpan(4), 4096);
        Apply(engine, Frame(WaypipeMessageType.OpenFile, open));

        var inject = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(inject, 9);
        Apply(engine, Frame(WaypipeMessageType.InjectRIDs, inject));

        var request = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(request, 4);
        BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(4), (16u << 16) | (1u << 11) | 0u);
        BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(8), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(12), 4096);
        Apply(engine, Frame(WaypipeMessageType.Protocol, request));

        Span<byte> bytes = stackalloc byte[64];
        Span<int> slots = stackalloc int[4];
        var (read, fds) = transport.TryReadNonBlocking(
            new byte[64], Array.Empty<byte>(), new int[4], Array.Empty<int>());
        Assert.Equal(16, read);
        Assert.Equal(1, fds);
    }

    [Fact]
    public void The_messages_this_transport_refuses_say_so_rather_than_dropping()
    {
        using var transport = new WaypipeClientTransport();
        using var engine = new WaypipeEngine(transport, WaypipeCompression.None);

        foreach (var refused in new[]
        {
            WaypipeMessageType.OpenDmabuf,
            WaypipeMessageType.OpenDmaVidSrc,
            WaypipeMessageType.OpenDmaVidDst,
            WaypipeMessageType.SendDmaVidPacket,
            WaypipeMessageType.OpenTimeline,
            WaypipeMessageType.SignalTimeline,
            WaypipeMessageType.Restart,
        })
        {
            var error = Assert.Throws<WaypipeException>(() => engine.Apply(refused, new byte[8]));
            Assert.Contains(refused.ToString(), error.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_malformed_frame_disconnects_the_client_rather_than_faulting_the_host()
    {
        var corpus = new List<(WaypipeMessageType Type, byte[] Body)>
        {
            (WaypipeMessageType.OpenFile, []),
            (WaypipeMessageType.OpenFile, [0, 0, 0, 0, 0, 0, 0, 0]),
            (WaypipeMessageType.BufferFill, [1, 0, 0, 0, 0, 0, 0, 0, 255, 255, 255, 127, 1, 2, 3]),
            (WaypipeMessageType.BufferDiff, [1, 0, 0, 0, 255, 255, 255, 127, 0, 0, 0, 0]),
            (WaypipeMessageType.InjectRIDs, [1, 2, 3]),
            (WaypipeMessageType.Protocol, [1, 0, 0, 0]),
            (WaypipeMessageType.Protocol, [1, 0, 0, 0, 0, 0, 255, 255]),
            (WaypipeMessageType.PipeTransfer, [1]),
        };

        foreach (var (type, body) in corpus)
        {
            using var transport = new WaypipeClientTransport();
            using var engine = new WaypipeEngine(transport, WaypipeCompression.None);
            try
            {
                engine.Apply(type, body);
            }
            catch (WaypipeException)
            {
            }
        }
    }

    [Fact]
    public void A_region_over_the_budget_is_refused()
    {
        using var transport = new WaypipeClientTransport();
        using var engine = new WaypipeEngine(
            transport, WaypipeCompression.None, new WaypipeLimits { MaxRegionBytes = 1024 });

        var open = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(open, 1);
        BinaryPrimitives.WriteInt32LittleEndian(open.AsSpan(4), 4096);
        Assert.Throws<WaypipeException>(() => Apply(engine, Frame(WaypipeMessageType.OpenFile, open)));
    }

    [Fact]
    public void A_sparse_pool_the_size_a_terminal_ships_fits_the_default_budget()
    {
        using var transport = new WaypipeClientTransport();
        using var engine = new WaypipeEngine(transport, WaypipeCompression.None);

        Apply(engine, OpenFile(1, 512 * 1024 * 1024));
        Apply(engine, OpenFile(2, 512 * 1024 * 1024));

        Assert.Equal(2, engine.LiveRemoteIds);
    }

    [Fact]
    public void A_pipe_the_peer_finished_writing_answers_with_a_read_shutdown_and_retires()
    {
        using var transport = new WaypipeClientTransport();
        using var engine = new WaypipeEngine(transport, WaypipeCompression.None);
        var sent = new List<(WaypipeMessageType Type, int RemoteId)>();
        engine.Send += (type, remoteId, _) => sent.Add((type, remoteId));

        var open = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(open, 11);
        Apply(engine, Frame(WaypipeMessageType.OpenIWPipe, open));

        var transfer = new byte[4 + 5];
        BinaryPrimitives.WriteInt32LittleEndian(transfer, 11);
        "basin"u8.CopyTo(transfer.AsSpan(4));
        Apply(engine, Frame(WaypipeMessageType.PipeTransfer, transfer));

        var shutdown = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(shutdown, 11);
        Apply(engine, Frame(WaypipeMessageType.PipeShutdownW, shutdown));

        Assert.Contains((WaypipeMessageType.PipeShutdownR, 11), sent);
        Assert.Equal(0, engine.LiveRemoteIds);
    }

    [Fact]
    public void A_minted_inbound_pipe_is_announced_and_carries_the_client_bytes_back()
    {
        using var peer = new LoopbackChannel();

        var inbound = new PipeFromClient();
        var slot = peer.Channel.Transport.Slots.Mint(inbound);

        var eventBytes = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(eventBytes, 3);
        BinaryPrimitives.WriteUInt32LittleEndian(eventBytes.AsSpan(4), 8u << 16);
        Assert.Equal(8, peer.Channel.Transport.TryWriteNonBlocking(eventBytes, new[] { slot }));

        var frames = peer.ReadFrames();
        var open = frames.Single(f => f.Type == WaypipeMessageType.OpenIRPipe);
        var remoteId = BinaryPrimitives.ReadInt32LittleEndian(open.Body);
        var inject = frames.Single(f => f.Type == WaypipeMessageType.InjectRIDs);
        Assert.Equal(remoteId, BinaryPrimitives.ReadInt32LittleEndian(inject.Body));

        var transfer = new byte[4 + 5];
        BinaryPrimitives.WriteInt32LittleEndian(transfer, remoteId);
        "hello"u8.CopyTo(transfer.AsSpan(4));
        Apply(peer.Channel.Engine, Frame(WaypipeMessageType.PipeTransfer, transfer));

        var shutdown = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(shutdown, remoteId);
        Apply(peer.Channel.Engine, Frame(WaypipeMessageType.PipeShutdownW, shutdown));

        Assert.Equal("hello"u8.ToArray(), inbound.ReadToEnd(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void A_region_sent_twice_is_created_again_under_a_fresh_remote_id()
    {
        using var peer = new LoopbackChannel();

        var region = new SharedMemoryRegion(64);
        region.Span.Fill(0x5a);
        var slot = peer.Channel.Transport.Slots.Mint(region);

        var eventBytes = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(eventBytes, 3);
        BinaryPrimitives.WriteUInt32LittleEndian(eventBytes.AsSpan(4), 8u << 16);
        Assert.Equal(8, peer.Channel.Transport.TryWriteNonBlocking(eventBytes, new[] { slot }));
        Assert.Equal(8, peer.Channel.Transport.TryWriteNonBlocking(eventBytes, new[] { slot }));

        var frames = peer.ReadFrames();
        var opens = frames.Where(f => f.Type == WaypipeMessageType.OpenFile).ToList();
        Assert.Equal(2, opens.Count);
        var first = BinaryPrimitives.ReadInt32LittleEndian(opens[0].Body);
        var second = BinaryPrimitives.ReadInt32LittleEndian(opens[1].Body);
        Assert.NotEqual(first, second);

        var fills = frames.Where(f => f.Type == WaypipeMessageType.BufferFill).ToList();
        Assert.Equal(2, fills.Count);
        Assert.Equal(first, BinaryPrimitives.ReadInt32LittleEndian(fills[0].Body));
        Assert.Equal(second, BinaryPrimitives.ReadInt32LittleEndian(fills[1].Body));

        var injects = frames.Where(f => f.Type == WaypipeMessageType.InjectRIDs).ToList();
        Assert.Equal(2, injects.Count);
        Assert.Equal(first, BinaryPrimitives.ReadInt32LittleEndian(injects[0].Body));
        Assert.Equal(second, BinaryPrimitives.ReadInt32LittleEndian(injects[1].Body));

        peer.Channel.Transport.CloseFd(slot);
        Assert.True(region.IsReleased);
    }

    [Fact]
    public void A_region_the_client_has_finished_with_is_forgotten_rather_than_held_for_the_channel()
    {
        using var transport = new WaypipeClientTransport();
        using var engine = new WaypipeEngine(transport, WaypipeCompression.None);

        var open = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(open, 21);
        BinaryPrimitives.WriteInt32LittleEndian(open.AsSpan(4), 4096);
        Apply(engine, Frame(WaypipeMessageType.OpenFile, open));

        var inject = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(inject, 21);
        Apply(engine, Frame(WaypipeMessageType.InjectRIDs, inject));

        var request = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(request, 4);
        BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(4), (16u << 16) | (1u << 11) | 0u);
        Apply(engine, Frame(WaypipeMessageType.Protocol, request));

        var slots = new int[4];
        var (_, fds) = transport.TryReadNonBlocking(
            new byte[64], Array.Empty<byte>(), slots, Array.Empty<int>());
        Assert.Equal(1, fds);
        Assert.Equal(1, engine.LiveRemoteIds);

        transport.CloseFd(slots[0]);
        Assert.Equal(0, engine.LiveRemoteIds);

        var again = new byte[12];
        BinaryPrimitives.WriteInt32LittleEndian(again, 21);
        BinaryPrimitives.WriteInt32LittleEndian(again.AsSpan(4), 0);
        BinaryPrimitives.WriteInt32LittleEndian(again.AsSpan(8), 16);
        var stale = Assert.Throws<WaypipeException>(
            () => Apply(engine, Frame(WaypipeMessageType.BufferFill, again)));
        Assert.Contains("names no file", stale.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_frame_longer_than_the_channel_reads_ends_it_before_the_body_arrives()
    {
        using var peer = new ChannelPeer(new WaypipeLimits { MaxFrameBytes = 4096 });

        Span<byte> oversized = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(
            oversized, WaypipeWire.Header(WaypipeMessageType.BufferFill, 1 << 20));
        peer.Send(oversized);

        var failure = peer.WaitForEnd();
        Assert.IsType<WaypipeException>(failure);
        Assert.Contains("4096", failure!.Message, StringComparison.Ordinal);
        Assert.Equal(0, peer.Channel.Engine.LiveRemoteIds);
    }

    [Fact]
    public void A_channel_that_opens_more_shared_memory_than_its_budget_is_ended()
    {
        using var peer = new ChannelPeer(new WaypipeLimits { MaxTotalRegionBytes = 8192 });

        peer.Send(OpenFile(1, 4096));
        peer.Send(OpenFile(2, 4096));
        peer.Send(OpenFile(3, 4096));

        var failure = peer.WaitForEnd();
        Assert.IsType<WaypipeException>(failure);
        Assert.Contains("shared memory", failure!.Message, StringComparison.Ordinal);
        Assert.Equal(2, peer.Channel.Engine.LiveRemoteIds);
    }

    [Fact]
    public void A_channel_that_opens_more_remote_ids_than_its_budget_is_ended()
    {
        using var peer = new ChannelPeer(new WaypipeLimits { MaxRemoteIds = 2 });

        peer.Send(OpenFile(1, 64));
        peer.Send(OpenFile(2, 64));
        peer.Send(OpenFile(3, 64));

        var failure = peer.WaitForEnd();
        Assert.IsType<WaypipeException>(failure);
        Assert.Equal(2, peer.Channel.Engine.LiveRemoteIds);
    }

    [Fact]
    public void A_pipe_the_channel_never_drains_is_ended_rather_than_buffered_without_bound()
    {
        using var peer = new ChannelPeer(new WaypipeLimits { MaxPipeBytes = 64 });

        var open = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(open, 11);
        peer.Send(Frame(WaypipeMessageType.OpenIWPipe, open));

        var transfer = new byte[4 + 96];
        BinaryPrimitives.WriteInt32LittleEndian(transfer, 11);
        peer.Send(Frame(WaypipeMessageType.PipeTransfer, transfer));

        var failure = peer.WaitForEnd();
        Assert.IsType<WaypipeException>(failure);
        Assert.Contains("budget", failure!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_channel_the_peer_closes_ends_without_naming_a_failure()
    {
        using var peer = new ChannelPeer(new WaypipeLimits());

        peer.Send(Frame(WaypipeMessageType.Close, []));

        Assert.Null(peer.WaitForEnd());
        Assert.True(peer.Channel.Engine.Closed);

        foreach (var type in FrameTypes(peer.DrainToEof()))
        {
            Assert.NotEqual(WaypipeMessageType.Close, type);
        }
    }

    [Fact]
    public void A_channel_that_fails_tells_the_peer_and_closes_the_stream()
    {
        using var peer = new ChannelPeer(new WaypipeLimits { MaxRemoteIds = 2 });

        peer.Send(OpenFile(1, 64));
        peer.Send(OpenFile(2, 64));
        peer.Send(OpenFile(3, 64));

        Assert.IsType<WaypipeException>(peer.WaitForEnd());
        Assert.Equal(WaypipeMessageType.Close, FrameTypes(peer.DrainToEof())[^1]);
    }

    [Fact]
    public void Disposing_a_channel_tells_the_peer_before_closing_the_stream()
    {
        using var peer = new ChannelPeer(new WaypipeLimits());

        peer.Channel.Dispose();

        Assert.Equal(WaypipeMessageType.Close, FrameTypes(peer.DrainToEof())[^1]);
    }

    private static List<WaypipeMessageType> FrameTypes(ReadOnlySpan<byte> bytes)
    {
        var types = new List<WaypipeMessageType>();
        var offset = 0;
        while (offset + 4 <= bytes.Length)
        {
            var (length, type) = WaypipeWire.ParseHeader(BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]));
            types.Add(type);
            offset += WaypipeWire.Padded(length);
        }

        Assert.Equal(bytes.Length, offset);
        return types;
    }

    private static byte[] OpenFile(int remoteId, int size)
    {
        var body = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(body, remoteId);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(4), size);
        return Frame(WaypipeMessageType.OpenFile, body);
    }

    private sealed class ChannelPeer : IDisposable
    {
        private readonly System.Net.Sockets.Socket _peer;
        private readonly System.Threading.ManualResetEventSlim _ended = new(false);
        private Exception? _failure;

        internal ChannelPeer(WaypipeLimits limits, WaypipeCompression compression = WaypipeCompression.None)
        {
            using var listener = new System.Net.Sockets.Socket(
                System.Net.Sockets.AddressFamily.InterNetwork,
                System.Net.Sockets.SocketType.Stream,
                System.Net.Sockets.ProtocolType.Tcp);
            listener.Bind(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0));
            listener.Listen(1);

            _peer = new System.Net.Sockets.Socket(
                System.Net.Sockets.AddressFamily.InterNetwork,
                System.Net.Sockets.SocketType.Stream,
                System.Net.Sockets.ProtocolType.Tcp);
            _peer.Connect(listener.LocalEndPoint!);
            var accepted = listener.Accept();

            Channel = WaypipeChannel.AttachChannel(
                new System.Net.Sockets.NetworkStream(accepted, ownsSocket: true),
                compression,
                limits);
            Channel.Ended += ex =>
            {
                _failure = ex;
                _ended.Set();
            };

            Span<byte> header = stackalloc byte[WaypipeWire.ConnectionHeaderLength];
            WaypipeWire.WriteConnectionHeader(header, WaypipeWire.ProtocolVersion, compression);
            Send(header);
        }

        internal WaypipeChannel Channel { get; }

        internal void Send(ReadOnlySpan<byte> bytes) => _peer.Send(bytes);

        internal byte[] DrainToEof()
        {
            _peer.ReceiveTimeout = 10_000;
            var received = new System.IO.MemoryStream();
            var chunk = new byte[4096];
            while (true)
            {
                int got;
                try
                {
                    got = _peer.Receive(chunk);
                }
                catch (System.Net.Sockets.SocketException)
                {
                    break;
                }

                if (got == 0)
                {
                    break;
                }

                received.Write(chunk, 0, got);
            }

            return received.ToArray();
        }

        internal Exception? WaitForEnd()
        {
            Assert.True(_ended.Wait(TimeSpan.FromSeconds(10)), "the channel never ended");
            return _failure;
        }

        public void Dispose()
        {
            Channel.Dispose();
            _peer.Dispose();
            _ended.Dispose();
        }
    }

    [Fact]
    public void A_zstd_fill_crosses_a_real_channel_and_lands_byte_for_byte()
    {
        using var peer = new ChannelPeer(new WaypipeLimits(), WaypipeCompression.Zstd);

        var contents = new byte[4096];
        for (var i = 0; i < contents.Length; i++)
        {
            contents[i] = (byte)(i * 13);
        }

        peer.Send(OpenFile(5, contents.Length));
        peer.Send(Fill(5, 0, contents.Length, contents, WaypipeCompression.Zstd));

        Span<byte> close = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(close, WaypipeWire.Header(WaypipeMessageType.Close, 8));
        peer.Send(close);
        Assert.Null(peer.WaitForEnd());

        var region = Resolve(peer.Channel.Engine, 5);
        Assert.True(region.Span.SequenceEqual(contents));
    }

    [Fact]
    public void An_exported_region_is_filled_with_zstd_on_a_zstd_channel()
    {
        using var peer = new LoopbackChannel(WaypipeCompression.Zstd);

        var region = new SharedMemoryRegion(4096);
        for (var i = 0; i < 4096; i++)
        {
            region.Span[i] = (byte)(i * 31);
        }

        var slot = peer.Channel.Transport.Slots.Mint(region);
        var eventBytes = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(eventBytes, 3);
        BinaryPrimitives.WriteUInt32LittleEndian(eventBytes.AsSpan(4), 8u << 16);
        Assert.Equal(8, peer.Channel.Transport.TryWriteNonBlocking(eventBytes, new[] { slot }));

        var frames = peer.ReadFrames();
        var fill = frames.Single(f => f.Type == WaypipeMessageType.BufferFill);
        var payload = fill.Body.AsSpan(12).ToArray();
        Assert.True(payload.Length < 4096);

        using var decompressor = new ZstdSharp.Decompressor();
        var decoded = new byte[4096];
        Assert.Equal(4096, decompressor.Unwrap(payload, decoded));
        Assert.True(decoded.AsSpan().SequenceEqual(region.Span));

        peer.Channel.Transport.CloseFd(slot);
    }

    private static SharedMemoryRegion Resolve(WaypipeEngine engine, int remoteId)
    {
        var field = typeof(WaypipeEngine).GetField(
            "_regions", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var regions = (Dictionary<int, SharedMemoryRegion>)field.GetValue(engine)!;
        return regions[remoteId];
    }
}
