using System.Buffers;
using System.Net.Sockets;
using System.Text.Json;
using Basin.Diagnostics;
using static Basin.Greetd.GreetdLog;

namespace Basin.Greetd;

public sealed class GreetdClient : IDisposable
{
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

    public bool IsAwaitingResponse { get; private set; }

    public bool IsClosed { get; private set; }

    public event Action<GreetdResponse>? Response;

    public event Action<string>? Closed;

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

    public static GreetdClient Adopt(ICompositorEventLoop loop, Socket socket)
    {
        ArgumentNullException.ThrowIfNull(loop);
        ArgumentNullException.ThrowIfNull(socket);
        return new GreetdClient(loop, socket);
    }

    public static string? SocketFromEnvironment()
        => Environment.GetEnvironmentVariable(SocketVariable) is { Length: > 0 } path ? path : null;

    public static GreetdClient? FromEnvironment(ICompositorEventLoop loop)
        => SocketFromEnvironment() is { } path ? Connect(loop, path) : null;

    public void Send(GreetdRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        BeginSend();
        _writer.Write(request);
        Flush();
    }

    public void SendAuthResponse(ReadOnlySpan<byte> utf8Response)
    {
        BeginSend();
        _writer.WriteAuthResponse(utf8Response);
        Flush();
    }

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
            throw new InvalidOperationException("the greetd connection is closed");
        }

        if (IsAwaitingResponse)
        {
            throw new InvalidOperationException("greetd answers one request at a time, and one is outstanding");
        }
    }

    private void Flush()
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
                    return;
                }

                frame = frame[sent..];
            }

            IsAwaitingResponse = true;
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
            Log.Error($"greetd read failed: {error.Message}");
            Close($"read failed ({error.Message})");
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

        Log.Info($"greetd connection closed: {reason}");
        Closed?.Invoke(reason);
    }
}
