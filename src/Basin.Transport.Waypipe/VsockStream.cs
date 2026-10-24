using System.Net.Sockets;
using System.Runtime.Versioning;

namespace Basin.Transport.Waypipe;

[SupportedOSPlatform("linux")]
internal sealed class VsockStream : Stream
{
    private readonly Socket _socket;
    private int _disposed;

    internal VsockStream(int fd)
    {
        _socket = new Socket(new SafeSocketHandle(fd, ownsHandle: true));
    }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        try
        {
            return _socket.Receive(buffer);
        }
        catch (SocketException ex)
        {
            throw new IOException($"The vsock read failed ({ex.SocketErrorCode}).", ex);
        }
    }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        try
        {
            while (!buffer.IsEmpty)
            {
                buffer = buffer[_socket.Send(buffer)..];
            }
        }
        catch (SocketException ex)
        {
            throw new IOException($"The vsock write failed ({ex.SocketErrorCode}).", ex);
        }
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    internal void Shutdown()
    {
        if (_disposed != 0)
        {
            return;
        }

        try
        {
            _socket.Shutdown(SocketShutdown.Both);
        }
        catch (SocketException ex)
        {
            WaypipeLog.Log.Debug(ex, $"vsock: shutdown failed");
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0 && disposing)
        {
            _socket.Dispose();
        }

        base.Dispose(disposing);
    }
}
