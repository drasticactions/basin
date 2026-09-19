using System.Net.WebSockets;

namespace Basin.Transport.Waypipe;

public sealed class WebSocketStream : Stream
{
    private const int ReceiveChunk = 64 * 1024;
    private static int _nextId;
    private readonly int _id = Interlocked.Increment(ref _nextId);
    private readonly WebSocket _socket;
    private readonly bool _ownsSocket;
    private bool _disposed;

    public WebSocketStream(WebSocket socket, bool ownsSocket = true)
    {
        ArgumentNullException.ThrowIfNull(socket);
        _socket = socket;
        _ownsSocket = ownsSocket;
    }

    public static async Task<WebSocketStream> ConnectAsync(Uri uri, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(uri);
        var socket = new ClientWebSocket();
        try
        {
            await socket.ConnectAsync(uri, cancellation).ConfigureAwait(false);
        }
        catch
        {
            socket.Dispose();
            throw;
        }

        return new WebSocketStream(socket);
    }

    public WebSocket Socket => _socket;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public override int Read(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException("a WebSocket is read asynchronously; attach it with the Async pump");

    public override int Read(Span<byte> buffer) =>
        throw new NotSupportedException("a WebSocket is read asynchronously; attach it with the Async pump");

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (buffer.IsEmpty)
        {
            return 0;
        }

        while (true)
        {
            if (_socket.State is not (WebSocketState.Open or WebSocketState.CloseSent))
            {
                return 0;
            }

            ValueWebSocketReceiveResult result;
            try
            {
                var slice = buffer.Length > ReceiveChunk ? buffer[..ReceiveChunk] : buffer;
                result = await _socket.ReceiveAsync(slice, cancellationToken).ConfigureAwait(false);
                WaypipeLog.Log.Debug($"web socket {_id}: received {result.Count} of {slice.Length} asked, end of message {result.EndOfMessage}, {result.MessageType}");
            }
            catch (WebSocketException ex)
            {
                throw new IOException("the WebSocket ended", ex);
            }

            if (result.MessageType == WebSocketMessageType.Close)
            {
                return 0;
            }

            if (result.Count > 0)
            {
                return result.Count;
            }
        }
    }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var pending = WriteAsync(buffer.ToArray(), CancellationToken.None);
        if (!pending.IsCompleted)
        {
            pending.AsTask().GetAwaiter().GetResult();
        }
        else if (pending.IsFaulted)
        {
            pending.GetAwaiter().GetResult();
        }
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        try
        {
            await _socket.SendAsync(buffer, WebSocketMessageType.Binary, endOfMessage: true, cancellationToken).ConfigureAwait(false);
        }
        catch (WebSocketException ex)
        {
            throw new IOException("the WebSocket ended", ex);
        }
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (disposing && _ownsSocket)
        {
            _socket.Abort();
            _socket.Dispose();
        }

        base.Dispose(disposing);
    }
}
