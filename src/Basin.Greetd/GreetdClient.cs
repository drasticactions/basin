using System.Buffers;
using System.Net.Sockets;
using System.Text.Json;
using Basin.Diagnostics;
using static Basin.Greetd.GreetdLog;

namespace Basin.Greetd;

/// <summary>
/// A connection to greetd, read as a loop fd source. It allows one outstanding request and raises <see cref="Response"/> on the loop.
/// Every call must come from the thread that created it.
/// </summary>
public sealed class GreetdClient : IDisposable
{
    /// <summary>The environment variable that greetd sets to the path of its socket.</summary>
    public const string SocketVariable = "GREETD_SOCK";

    private const int ReceiveChunk = 4096;

    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly Socket _socket;
    private readonly GreetdFrameWriter _writer = new();
    private readonly IEventSource _source;
    private byte[] _receive = ArrayPool<byte>.Shared.Rent(ReceiveChunk * 2);
    private int _received;
    private bool _disposed;

    private GreetdClient(ICompositorEventLoop loop, Socket socket)
    {
        _socket = socket;
        _socket.Blocking = false;
        _source = loop.AddFd((int)_socket.Handle, FdReadiness.Readable, OnReady);
        BasinCounters.Track();
    }

    /// <summary>
    /// Gets a value indicating whether a request is sent and its response has not arrived.
    /// </summary>
    public bool IsAwaitingResponse { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the connection is closed or disposed. A closed client cannot send.
    /// </summary>
    public bool IsClosed { get; private set; }

    /// <summary>
    /// Occurs on the loop for each response, after <see cref="IsAwaitingResponse"/> becomes false. A handler can send the next request.
    /// </summary>
    public event Action<GreetdResponse>? Response;

    /// <summary>
    /// Occurs once when greetd closes the socket, a read or write fails, or greetd sends a frame the client cannot read. The argument is the reason. <see cref="Dispose"/> does not raise it.
    /// </summary>
    public event Action<string>? Closed;

    /// <summary>
    /// Connects to the greetd socket at <paramref name="path"/> and reads it on <paramref name="loop"/>.
    /// </summary>
    /// <param name="loop">The loop that reads the socket and raises the events.</param>
    /// <param name="path">The path of the Unix socket.</param>
    /// <returns>A connected client. The caller owns it.</returns>
    /// <exception cref="SocketException">The connection fails.</exception>
    public static GreetdClient Connect(ICompositorEventLoop loop, string path)
    {
        ArgumentNullException.ThrowIfNull(loop);
        ArgumentException.ThrowIfNullOrEmpty(path);
        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            socket.Connect(new UnixDomainSocketEndPoint(path));
        }
        catch
        {
            socket.Dispose();
            throw;
        }

        return new GreetdClient(loop, socket);
    }

    /// <summary>
    /// Makes a client from a socket that is already connected, for example the client end of a <see cref="LoopbackGreetd"/>.
    /// </summary>
    /// <param name="loop">The loop that reads the socket and raises the events.</param>
    /// <param name="socket">The connected stream socket. The client takes ownership and sets it to non-blocking.</param>
    /// <returns>The client. The caller owns it.</returns>
    public static GreetdClient Adopt(ICompositorEventLoop loop, Socket socket)
    {
        ArgumentNullException.ThrowIfNull(loop);
        ArgumentNullException.ThrowIfNull(socket);
        return new GreetdClient(loop, socket);
    }

    /// <summary>
    /// Reads the greetd socket path from <see cref="SocketVariable"/>.
    /// </summary>
    /// <returns>The path, or <see langword="null"/> when the variable is not set or empty.</returns>
    public static string? SocketFromEnvironment()
        => Environment.GetEnvironmentVariable(SocketVariable) is { Length: > 0 } path ? path : null;

    /// <summary>
    /// Connects to the socket that <see cref="SocketVariable"/> names.
    /// </summary>
    /// <param name="loop">The loop that reads the socket and raises the events.</param>
    /// <returns>A connected client, or <see langword="null"/> when the process does not run under greetd.</returns>
    /// <exception cref="SocketException">The variable is set but the connection fails.</exception>
    public static GreetdClient? FromEnvironment(ICompositorEventLoop loop)
        => SocketFromEnvironment() is { } path ? Connect(loop, path) : null;

    /// <summary>
    /// Writes <paramref name="request"/> as one frame. A write failure closes the client and raises <see cref="Closed"/> instead of throwing.
    /// </summary>
    /// <param name="request">The request to send.</param>
    /// <returns><see langword="true"/> when the whole frame is written. <see langword="false"/> when the write failed and the client closed.</returns>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    /// <exception cref="InvalidOperationException">The connection is closed, or a response is outstanding.</exception>
    public bool Send(GreetdRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        BeginSend();
        _writer.Write(request);
        return Flush();
    }

    /// <summary>
    /// Sends a <c>post_auth_message_response</c> with an answer that never becomes a managed string. The frame buffer is cleared after the write.
    /// A write failure closes the client and raises <see cref="Closed"/> instead of throwing.
    /// </summary>
    /// <param name="utf8Response">The answer as UTF-8 bytes. The caller keeps ownership and clears it.</param>
    /// <returns><see langword="true"/> when the whole frame is written. <see langword="false"/> when the write failed and the client closed.</returns>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    /// <exception cref="InvalidOperationException">The connection is closed, or a response is outstanding.</exception>
    public bool SendAuthResponse(ReadOnlySpan<byte> utf8Response)
    {
        BeginSend();
        _writer.WriteAuthResponse(utf8Response);
        return Flush();
    }

    /// <summary>
    /// Removes the fd source, closes the socket and clears the buffers. It does not raise <see cref="Closed"/>. A second call does nothing.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _thread.Assert();
        _disposed = true;
        IsClosed = true;
        if (!_source.IsRemoved)
        {
            _source.Remove();
        }

        _socket.Dispose();
        _writer.Dispose();
        _receive.AsSpan().Clear();
        ArrayPool<byte>.Shared.Return(_receive);
        _receive = [];
        BasinCounters.Untrack();
    }

    private void BeginSend()
    {
        _thread.Assert();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsClosed)
        {
            throw new InvalidOperationException("The greetd connection is closed.");
        }

        if (IsAwaitingResponse)
        {
            throw new InvalidOperationException("A request to greetd is still waiting for its response. Send the next request after the response.");
        }
    }

    private bool Flush()
    {
        try
        {
            var frame = _writer.Frame;
            while (!frame.IsEmpty)
            {
                var sent = _socket.Send(frame, SocketFlags.None, out var error);
                if (error == SocketError.WouldBlock)
                {
                    _socket.Poll(100_000, SelectMode.SelectWrite);
                    continue;
                }

                if (error != SocketError.Success)
                {
                    Close($"write failed ({error})");
                    return false;
                }

                frame = frame[sent..];
            }

            IsAwaitingResponse = true;
            return true;
        }
        finally
        {
            _writer.Clear();
        }
    }

    private void OnReady(int fd, FdReadiness readiness)
    {
        try
        {
            Read();
        }
        catch (Exception error)
        {
            Log.Error(error, $"connection: read failed");
            Close("read failed");
        }
    }

    private void Read()
    {
        while (!IsClosed)
        {
            if (_receive.Length - _received < ReceiveChunk)
            {
                var grown = ArrayPool<byte>.Shared.Rent(Math.Min(_receive.Length * 2, GreetdFrame.MaxLength + GreetdFrame.HeaderLength + ReceiveChunk));
                _receive.AsSpan(0, _received).CopyTo(grown);
                _receive.AsSpan().Clear();
                ArrayPool<byte>.Shared.Return(_receive);
                _receive = grown;
            }

            var read = _socket.Receive(_receive.AsSpan(_received), SocketFlags.None, out var error);
            if (error == SocketError.WouldBlock)
            {
                return;
            }

            if (error == SocketError.Interrupted)
            {
                continue;
            }

            if (error != SocketError.Success)
            {
                Close($"read failed ({error})");
                return;
            }

            if (read == 0)
            {
                Close("greetd closed the connection");
                return;
            }

            _received += read;
            Drain();
        }
    }

    private void Drain()
    {
        var offset = 0;
        while (!IsClosed)
        {
            var status = GreetdFrame.TryRead(_receive.AsSpan(offset, _received - offset), out var payload, out var consumed);
            if (status == GreetdFrameStatus.Incomplete)
            {
                break;
            }

            if (status == GreetdFrameStatus.TooLarge)
            {
                Close($"greetd sent a frame over {GreetdFrame.MaxLength} bytes");
                return;
            }

            GreetdResponse? response;
            try
            {
                response = JsonSerializer.Deserialize(payload, GreetdJsonContext.Default.GreetdResponse);
            }
            catch (JsonException error)
            {
                Close($"greetd sent a malformed response ({error.Message})");
                return;
            }

            offset += consumed;
            if (response is null)
            {
                Close("greetd sent a null response");
                return;
            }

            if (!IsAwaitingResponse)
            {
                Close("greetd sent a response with no request outstanding");
                return;
            }

            IsAwaitingResponse = false;
            Response?.Invoke(response);
        }

        if (offset > 0 && !_disposed)
        {
            _receive.AsSpan(offset, _received - offset).CopyTo(_receive);
            _receive.AsSpan(_received - offset, offset).Clear();
            _received -= offset;
        }
    }

    private void Close(string reason)
    {
        if (IsClosed)
        {
            return;
        }

        IsClosed = true;
        IsAwaitingResponse = false;
        if (!_source.IsRemoved)
        {
            _source.Remove();
        }

        Log.Info($"connection: closed, {reason}");
        Closed?.Invoke(reason);
    }
}
