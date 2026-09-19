using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using Basin.Transport.Waypipe;

namespace Basin.Tests;

internal sealed class LoopbackChannel : IDisposable
{
    public enum Mode
    {
        Tcp,
        DuplexPipe,
    }

    private readonly Socket? _listener;
    private readonly Socket? _peer;
    private readonly Stream? _peerStream;
    private readonly Socket? _accepted;
    private readonly Pipe? _toHost;
    private readonly Pipe? _toPeer;

    public LoopbackChannel(
        WaypipeCompression compression = WaypipeCompression.Lz4,
        WaypipeChannelOptions? options = null,
        Mode mode = Mode.Tcp,
        WaypipePump pump = WaypipePump.Thread,
        bool asyncOnlyWrites = false)
    {
        if (mode == Mode.DuplexPipe)
        {
            _toHost = new Pipe();
            _toPeer = new Pipe();
            var hostSide = new DuplexStream(_toHost.Reader.AsStream(), _toPeer.Writer.AsStream(), asyncOnlyWrites);
            Channel = WaypipeChannel.AttachChannel(hostSide, compression, options: options, pump: pump);
            return;
        }

        _listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        _listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        _listener.Listen(1);

        _peer = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var connecting = _peer.ConnectAsync((IPEndPoint)_listener.LocalEndPoint!);
        _accepted = _listener.Accept();
        connecting.GetAwaiter().GetResult();
        _peer.NoDelay = true;
        _accepted.NoDelay = true;
        _peerStream = new NetworkStream(_peer, ownsSocket: false);

        Channel = WaypipeChannel.AttachChannel(
            new NetworkStream(_accepted, ownsSocket: false), compression, options: options, pump: pump);
    }

    public WaypipeChannel Channel { get; }

    public void Send(ReadOnlySpan<byte> bytes)
    {
        if (_toHost is { } pipe)
        {
            pipe.Writer.Write(bytes);
            pipe.Writer.FlushAsync().AsTask().GetAwaiter().GetResult();
            return;
        }

        _peerStream!.Write(bytes);
    }

    public void SendConnectionHeader(WaypipeCompression compression, WaypipeVideoCodec video = WaypipeVideoCodec.None)
    {
        Span<byte> header = stackalloc byte[WaypipeWire.ConnectionHeaderLength];
        WaypipeWire.WriteConnectionHeader(header, WaypipeWire.ProtocolVersion, compression, video: video);
        Send(header);
    }

    public List<(WaypipeMessageType Type, byte[] Body)> ReadFrames(int millis = 500)
    {
        var frames = new List<(WaypipeMessageType, byte[])>();
        var buffer = new byte[64 * 1024];
        var read = 0;
        var deadline = Environment.TickCount64 + millis;
        var quietAfter = long.MaxValue;
        while (Environment.TickCount64 < deadline)
        {
            var got = TakeAvailable(buffer.AsSpan(read));
            if (got > 0)
            {
                read += got;
                quietAfter = Environment.TickCount64 + 50;
            }
            else if (Environment.TickCount64 >= quietAfter)
            {
                break;
            }
            else
            {
                Thread.Sleep(5);
            }
        }

        var offset = 0;
        while (offset + 4 <= read)
        {
            var (length, type) = WaypipeWire.ParseHeader(BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(offset)));
            if (length < 4 || offset + length > read)
            {
                break;
            }

            frames.Add((type, buffer.AsSpan(offset + 4, length - 4).ToArray()));
            offset += WaypipeWire.Padded(length);
        }

        return frames;
    }

    private int TakeAvailable(Span<byte> into)
    {
        if (_toPeer is { } pipe)
        {
            if (!pipe.Reader.TryRead(out var result))
            {
                return 0;
            }

            var sequence = result.Buffer;
            var take = (int)Math.Min(sequence.Length, into.Length);
            sequence.Slice(0, take).CopyTo(into);
            pipe.Reader.AdvanceTo(sequence.GetPosition(take));
            return take;
        }

        return _peer!.Available > 0 ? _peerStream!.Read(into) : 0;
    }

    public void Dispose()
    {
        Channel.Dispose();
        _toHost?.Writer.Complete();
        _toPeer?.Reader.Complete();
        _peerStream?.Dispose();
        _peer?.Dispose();
        _accepted?.Dispose();
        _listener?.Dispose();
    }

    private sealed class DuplexStream(Stream reader, Stream writer, bool asyncOnlyWrites = false) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => writer.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken) => writer.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count) => reader.Read(buffer, offset, count);

        public override int Read(Span<byte> buffer) => reader.Read(buffer);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            reader.ReadAsync(buffer, cancellationToken);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            reader.ReadAsync(buffer, offset, count, cancellationToken);

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (asyncOnlyWrites)
            {
                throw new NotSupportedException("this stream is written asynchronously");
            }

            writer.Write(buffer);
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            writer.WriteAsync(buffer, cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                reader.Dispose();
                writer.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
