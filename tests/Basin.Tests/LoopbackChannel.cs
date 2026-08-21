using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Basin.Transport.Waypipe;

namespace Basin.Tests;

internal sealed class LoopbackChannel : IDisposable
{
    private readonly Socket _listener;
    private readonly Socket _peer;
    private readonly NetworkStream _peerStream;
    private readonly Socket _accepted;

    public LoopbackChannel(WaypipeCompression compression = WaypipeCompression.Lz4, WaypipeChannelOptions? options = null)
    {
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

        Channel = WaypipeChannel.AttachChannel(new NetworkStream(_accepted, ownsSocket: false), compression, options: options);
    }

    public WaypipeChannel Channel { get; }

    public void Send(ReadOnlySpan<byte> bytes) => _peerStream.Write(bytes);

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
            if (_peer.Available > 0)
            {
                read += _peerStream.Read(buffer.AsSpan(read));
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

    public void Dispose()
    {
        Channel.Dispose();
        _peerStream.Dispose();
        _peer.Dispose();
        _accepted.Dispose();
        _listener.Dispose();
    }
}
