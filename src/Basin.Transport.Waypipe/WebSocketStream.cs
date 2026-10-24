using System.Net.WebSockets;

namespace Basin.Transport.Waypipe;

/// <summary>
/// A connected WebSocket as a byte stream for a <see cref="WaypipeChannel"/>. Message boundaries carry no meaning, and every write is one binary message.
/// </summary>
public sealed class WebSocketStream : Stream
{
    private const int ReceiveChunk = 64 * 1024;
    private static int _nextId;
    private readonly int _id = Interlocked.Increment(ref _nextId);
    private readonly WebSocket _socket;
    private readonly bool _ownsSocket;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebSocketStream"/> class.
    /// </summary>
    /// <param name="socket">The connected WebSocket.</param>
    /// <param name="ownsSocket">A value indicating whether disposal aborts and disposes <paramref name="socket"/>.</param>
    public WebSocketStream(WebSocket socket, bool ownsSocket = true)
    {
        ArgumentNullException.ThrowIfNull(socket);
        _socket = socket;
        _ownsSocket = ownsSocket;
    }

    /// <summary>
    /// Connects a client WebSocket and wraps it in a stream that owns it.
    /// </summary>
    /// <param name="uri">The "ws://" or "wss://" URL.</param>
    /// <param name="cancellation">Cancels the connection attempt.</param>
    /// <returns>A task that completes with the stream, which the caller owns.</returns>
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

    /// <summary>
    /// Gets the WebSocket under the stream.
    /// </summary>
    public WebSocket Socket => _socket;

    /// <summary>
    /// Gets a value indicating whether the stream can read. This value is always <see langword="true"/>, but only the asynchronous reads work.
    /// </summary>
    public override bool CanRead => true;

    /// <inheritdoc/>
    public override bool CanSeek => false;

    /// <inheritdoc/>
    public override bool CanWrite => true;

    /// <inheritdoc/>
    public override long Length => throw new NotSupportedException();

    /// <inheritdoc/>
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <summary>
    /// Does nothing, because each write is sent as a complete message.
    /// </summary>
    public override void Flush()
    {
    }

    /// <summary>
    /// Does nothing, because each write is sent as a complete message.
    /// </summary>
    /// <param name="cancellationToken">Not used.</param>
    /// <returns>A completed task.</returns>
    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Always raises <see cref="NotSupportedException"/>, because a WebSocket is read only asynchronously.
    /// </summary>
    /// <param name="buffer">The destination buffer, which is not used.</param>
    /// <param name="offset">The offset in the buffer, which is not used.</param>
    /// <param name="count">The byte count, which is not used.</param>
    /// <returns>No value. The method always raises an exception.</returns>
    public override int Read(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException("a WebSocket is read asynchronously; attach it with the Async pump");

    /// <summary>
    /// Always raises <see cref="NotSupportedException"/>, because a WebSocket is read only asynchronously.
    /// </summary>
    /// <param name="buffer">Not used.</param>
    /// <returns>No value. The method always raises an exception.</returns>
    public override int Read(Span<byte> buffer) =>
        throw new NotSupportedException("a WebSocket is read asynchronously; attach it with the Async pump");

    /// <inheritdoc/>
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    /// <summary>
    /// Receives the next bytes, at most 64 KiB, and skips empty messages.
    /// </summary>
    /// <param name="buffer">The destination.</param>
    /// <param name="cancellationToken">Cancels the receive.</param>
    /// <returns>The number of bytes received, or 0 when the socket closed.</returns>
    /// <exception cref="IOException">The WebSocket failed.</exception>
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

    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    /// <summary>
    /// Sends the bytes as one binary message. It blocks only when the send did not complete synchronously.
    /// </summary>
    /// <param name="buffer">The bytes to send.</param>
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

    /// <inheritdoc/>
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    /// <summary>
    /// Sends the bytes as one binary message.
    /// </summary>
    /// <param name="buffer">The bytes to send.</param>
    /// <param name="cancellationToken">Cancels the send.</param>
    /// <returns>A task that completes when the message is sent.</returns>
    /// <exception cref="IOException">The WebSocket failed.</exception>
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

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <summary>
    /// Aborts and disposes the WebSocket when the stream owns it. A second call does nothing.
    /// </summary>
    /// <param name="disposing">A value indicating whether the call comes from <see cref="Stream.Dispose()"/>.</param>
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
