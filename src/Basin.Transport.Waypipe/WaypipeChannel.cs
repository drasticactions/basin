using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using K4os.Compression.LZ4;
using Wayland.Server.Shm;

namespace Basin.Transport.Waypipe;

public sealed class WaypipeChannel : IDisposable
{
    private readonly object _writeLock = new();
    private readonly WaypipeClientTransport _transport = new();
    private readonly WaypipeEngine _engine;
    private readonly WaypipeLimits _limits;
    private readonly Stream _stream;
    private readonly Socket? _socket;
    private readonly Thread _reader;
    private byte[] _outbound = new byte[8192];
    private ZstdSharp.Compressor? _zstdCompressor;
    private int _nextRemoteId = -1;
    private volatile bool _stopping;
    private int _ended;
    private bool _closeSent;
    private bool _disposed;

    private WaypipeChannel(
        Stream stream, Socket? socket, WaypipeCompression compression, WaypipeLimits? limits, WaypipeChannelOptions? options)
    {
        _stream = stream;
        _socket = socket;
        _limits = limits ?? new WaypipeLimits();
        Options = options ?? new WaypipeChannelOptions();
        Globals = new WaypipeGlobals(Options.CarriesDmabuf, Options.AcceptsVideo && Options.VideoDecoder is not null);
        _engine = new WaypipeEngine(_transport, compression, _limits, Options);
        _engine.Send += OnEngineSend;
        _transport.Outbound += OnOutbound;
        _reader = new Thread(Read) { IsBackground = true, Name = "basin-waypipe-reader" };
    }

    public WaypipeClientTransport Transport => _transport;

    public WaypipeEngine Engine => _engine;

    public WaypipeChannelOptions Options { get; }

    public WaypipeGlobals Globals { get; }

    public event Action<Exception?>? Ended;

    public static WaypipeChannel Listen(
        EndPoint endpoint,
        WaypipeCompression compression = WaypipeCompression.Lz4,
        WaypipeLimits? limits = null,
        CancellationToken cancellation = default,
        WaypipeChannelOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (endpoint is IPEndPoint ip &&
            (ip.Address.Equals(IPAddress.Any) || ip.Address.Equals(IPAddress.IPv6Any)))
        {
            throw new ArgumentException(
                "a waypipe channel binds an explicit address; loopback, a unix socket or an ssh-forwarded stream is the posture",
                nameof(endpoint));
        }

        var protocol = endpoint is UnixDomainSocketEndPoint ? ProtocolType.Unspecified : ProtocolType.Tcp;
        using var listener = new Socket(endpoint.AddressFamily, SocketType.Stream, protocol);
        listener.Bind(endpoint);
        listener.Listen(1);
        var accepted = listener.AcceptAsync(cancellation).AsTask().GetAwaiter().GetResult();
        return Adopt(new NetworkStream(accepted, ownsSocket: false), accepted, compression, limits, options);
    }

    public static WaypipeChannel AttachChannel(
        Stream stream,
        WaypipeCompression compression = WaypipeCompression.Lz4,
        WaypipeLimits? limits = null,
        WaypipeChannelOptions? options = null) =>
        Adopt(stream, null, compression, limits, options);

    private static WaypipeChannel Adopt(
        Stream stream, Socket? socket, WaypipeCompression compression, WaypipeLimits? limits, WaypipeChannelOptions? options)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var channel = new WaypipeChannel(stream, socket, compression, limits, options);
        channel._reader.Start();
        return channel;
    }

    public int CreateWritablePipe() => OpenPipe(WaypipeMessageType.OpenIWPipe, writable: true);

    public int CreateReadablePipe() => OpenPipe(WaypipeMessageType.OpenIRPipe, writable: false);

    private int OpenPipe(WaypipeMessageType kind, bool writable)
    {
        var remoteId = _nextRemoteId--;
        var pipe = _engine.CreateOwnedPipe(remoteId, kind);
        if (writable)
        {
            _engine.AttachWriter(pipe);
        }

        Span<byte> open = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(open, WaypipeWire.Header(kind, 8));
        BinaryPrimitives.WriteInt32LittleEndian(open[4..], remoteId);
        lock (_writeLock)
        {
            if (!_closeSent)
            {
                Write(open);
            }
        }

        return _transport.Slots.Mint(pipe);
    }

    private void OnEngineSend(WaypipeMessageType type, int remoteId, ReadOnlyMemory<byte> payload)
    {
        if (_stopping)
        {
            return;
        }

        try
        {
            var message = new byte[8 + payload.Length];
            BinaryPrimitives.WriteUInt32LittleEndian(message, WaypipeWire.Header(type, message.Length));
            BinaryPrimitives.WriteInt32LittleEndian(message.AsSpan(4), remoteId);
            payload.Span.CopyTo(message.AsSpan(8));
            lock (_writeLock)
            {
                if (!_closeSent)
                {
                    Write(message);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            _stopping = true;
            EndPeer();
            _transport.EndOfStream();
            End(ex);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stopping = true;
        EndPeer();
        _stream.Dispose();
        _socket?.Dispose();
        _reader.Join(1000);
        _transport.Outbound -= OnOutbound;
        _engine.Send -= OnEngineSend;
        _engine.Dispose();
        _transport.Dispose();
        _zstdCompressor?.Dispose();
        _zstdCompressor = null;
    }

    private void Read()
    {
        Exception? failure = null;
        try
        {
            var header = new byte[WaypipeWire.ConnectionHeaderLength];
            ReadExactly(header);
            var connection = WaypipeWire.ParseConnectionHeader(header, _engine.Compression);
            RequireDecoderFor(connection.VideoCodec);

            Span<byte> version = stackalloc byte[8];
            BinaryPrimitives.WriteUInt32LittleEndian(version, WaypipeWire.Header(WaypipeMessageType.Version, 8));
            BinaryPrimitives.WriteUInt32LittleEndian(version[4..], connection.Version);
            lock (_writeLock)
            {
                if (!_closeSent)
                {
                    Write(version);
                }
            }

            var frame = new byte[4];
            var body = new byte[16 * 1024];
            while (!_stopping)
            {
                if (!TryReadExactly(frame))
                {
                    break;
                }

                var (length, type) = WaypipeWire.ParseHeader(BinaryPrimitives.ReadUInt32LittleEndian(frame));
                if (length < 4)
                {
                    throw new WaypipeException($"a {type} frame declares {length} bytes, below its own header");
                }

                if (length > _limits.MaxFrameBytes)
                {
                    throw new WaypipeException(
                        $"a {type} frame declares {length} bytes, over the {_limits.MaxFrameBytes} this channel reads");
                }

                var payload = WaypipeWire.Padded(length) - 4;
                if (payload > body.Length)
                {
                    body = new byte[payload];
                }

                ReadExactly(body.AsSpan(0, payload));
                _engine.Apply(type, body.AsSpan(0, length - 4));
                if (_engine.Closed)
                {
                    break;
                }
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            failure = _stopping ? null : ex;
        }

        _stopping = true;
        EndPeer();
        _transport.EndOfStream();
        End(failure);
    }

    private void RequireDecoderFor(WaypipeVideoCodec codec)
    {
        if (codec == WaypipeVideoCodec.None)
        {
            return;
        }

        if (!Options.AcceptsVideo || Options.VideoDecoder is null)
        {
            throw new WaypipeException(
                $"the peer will send {codec} and this channel has no decoder registered; "
                + "start the session with --video and install libavcodec");
        }

        var wanted = codec switch
        {
            WaypipeVideoCodec.Vp9 => Basin.Capabilities.VideoCodec.Vp9,
            WaypipeVideoCodec.H264 => Basin.Capabilities.VideoCodec.H264,
            _ => Basin.Capabilities.VideoCodec.Av1,
        };
        if (!Options.VideoDecoder.Supports(wanted))
        {
            var handled = string.Join(
                ", ",
                Enum.GetValues<Basin.Capabilities.VideoCodec>().Where(Options.VideoDecoder.Supports));
            throw new WaypipeException(
                $"the peer will send {codec} and the decoder handles [{handled}]");
        }

        _engine.ExpectedVideoCodec = wanted;
    }

    private void End(Exception? failure)
    {
        if (Interlocked.Exchange(ref _ended, 1) == 0)
        {
            Ended?.Invoke(failure);
        }
    }

    private void EndPeer()
    {
        lock (_writeLock)
        {
            if (!_closeSent && !_engine.Closed)
            {
                _closeSent = true;
                Span<byte> close = stackalloc byte[8];
                BinaryPrimitives.WriteUInt32LittleEndian(close, WaypipeWire.Header(WaypipeMessageType.Close, 8));
                try
                {
                    Write(close);
                }
                catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
                {
                }
            }

            try
            {
                if (_socket is not null)
                {
                    _socket.Shutdown(SocketShutdown.Both);
                }
                else
                {
                    _stream.Dispose();
                }
            }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
            {
            }
        }
    }

    private void OnOutbound(ReadOnlySpan<byte> bytes, ReadOnlySpan<int> slots)
    {
        if (_stopping)
        {
            return;
        }

        try
        {
            lock (_writeLock)
            {
                if (!_closeSent)
                {
                    if (slots.Length > 0)
                    {
                        SendRemoteIds(slots);
                    }

                    SendProtocol(bytes, slots.Length);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            _stopping = true;
            EndPeer();
            _transport.EndOfStream();
            End(ex);
        }
    }

    private void SendRemoteIds(ReadOnlySpan<int> slots)
    {
        Span<byte> inject = stackalloc byte[4 + (slots.Length * 4)];
        BinaryPrimitives.WriteUInt32LittleEndian(
            inject, WaypipeWire.Header(WaypipeMessageType.InjectRIDs, inject.Length));

        for (var i = 0; i < slots.Length; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(inject[(4 + (i * 4))..], Export(slots[i]));
        }

        Write(inject);
    }

    private int Export(int slot)
    {
        var payload = _transport.Slots.Resolve<object>(slot);
        if (payload is WaypipePipe pipe)
        {
            return pipe.RemoteId;
        }

        if (payload is Basin.IPipeFromClient inbound)
        {
            return ExportInbound(inbound);
        }

        if (payload is not SharedMemoryRegion region)
        {
            throw new WaypipeException($"fd slot {slot} carries a {payload.GetType().Name}, which no channel message names");
        }

        var remoteId = _nextRemoteId--;

        Span<byte> open = stackalloc byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(open, WaypipeWire.Header(WaypipeMessageType.OpenFile, 12));
        BinaryPrimitives.WriteInt32LittleEndian(open[4..], remoteId);
        BinaryPrimitives.WriteInt32LittleEndian(open[8..], region.Size);
        Write(open);

        SendFill(remoteId, region);
        return remoteId;
    }

    private int ExportInbound(Basin.IPipeFromClient inbound)
    {
        var remoteId = _nextRemoteId--;
        var pipe = _engine.CreateOwnedPipe(remoteId, WaypipeMessageType.OpenIRPipe);
        pipe.ForwardTo(inbound);

        Span<byte> open = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(open, WaypipeWire.Header(WaypipeMessageType.OpenIRPipe, 8));
        BinaryPrimitives.WriteInt32LittleEndian(open[4..], remoteId);
        Write(open);
        return remoteId;
    }

    private void SendFill(int remoteId, SharedMemoryRegion region)
    {
        var contents = region.Span;
        var payload = CompressPayload(contents);

        var message = new byte[16 + payload.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(
            message, WaypipeWire.Header(WaypipeMessageType.BufferFill, message.Length));
        BinaryPrimitives.WriteInt32LittleEndian(message.AsSpan(4), remoteId);
        BinaryPrimitives.WriteInt32LittleEndian(message.AsSpan(8), 0);
        BinaryPrimitives.WriteInt32LittleEndian(message.AsSpan(12), contents.Length);
        payload.CopyTo(message.AsSpan(16));
        Write(message);
    }

    private byte[] CompressPayload(ReadOnlySpan<byte> source)
    {
        switch (_engine.Compression)
        {
            case WaypipeCompression.Lz4:
            {
                var buffer = new byte[LZ4Codec.MaximumOutputSize(source.Length)];
                var written = LZ4Codec.Encode(source, buffer);
                return buffer.AsSpan(0, written).ToArray();
            }

            case WaypipeCompression.Zstd:
            {
                _zstdCompressor ??= new ZstdSharp.Compressor();
                var buffer = new byte[ZstdSharp.Compressor.GetCompressBound(source.Length)];
                var written = _zstdCompressor.Wrap(source, buffer);
                return buffer.AsSpan(0, written).ToArray();
            }

            default:
                return source.ToArray();
        }
    }

    private void SendProtocol(ReadOnlySpan<byte> bytes, int fds)
    {
        if (bytes.IsEmpty)
        {
            return;
        }

        var length = 4 + bytes.Length;
        if (_outbound.Length < length)
        {
            _outbound = new byte[Math.Max(length, _outbound.Length * 2)];
        }

        var message = _outbound.AsSpan(0, length);
        BinaryPrimitives.WriteUInt32LittleEndian(message, WaypipeWire.Header(WaypipeMessageType.Protocol, length));
        bytes.CopyTo(message[4..]);

        if (fds > 0)
        {
            var last = LastMessageOffset(message[4..]);
            var header2 = BinaryPrimitives.ReadUInt32LittleEndian(message[(4 + last + 4)..]);
            BinaryPrimitives.WriteUInt32LittleEndian(
                message[(4 + last + 4)..], WaypipeWire.TagFdCount(header2, fds));
        }

        Write(message);
    }

    private static int LastMessageOffset(ReadOnlySpan<byte> bytes)
    {
        var offset = 0;
        var last = 0;
        while (offset + 8 <= bytes.Length)
        {
            var header2 = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(offset + 4)..]);
            var length = WaypipeWire.MessageLength(header2);
            if (length < 8 || offset + length > bytes.Length)
            {
                break;
            }

            last = offset;
            offset += length;
        }

        return last;
    }

    private void Write(ReadOnlySpan<byte> message)
    {
        _stream.Write(message);
        var padding = WaypipeWire.Padded(message.Length) - message.Length;
        if (padding > 0)
        {
            Span<byte> zeros = stackalloc byte[4];
            zeros.Clear();
            _stream.Write(zeros[..padding]);
        }

        _stream.Flush();
    }

    private void ReadExactly(Span<byte> buffer)
    {
        if (!TryReadExactly(buffer))
        {
            throw new WaypipeException("the channel ended inside a frame");
        }
    }

    private bool TryReadExactly(Span<byte> buffer)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var got = _stream.Read(buffer[read..]);
            if (got <= 0)
            {
                return false;
            }

            read += got;
        }

        return true;
    }
}
