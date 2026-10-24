using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using Basin;

namespace Basin.Greetd;

/// <summary>
/// A fake greetd server in the process, on the compositor loop, for a demo mode. It accepts any user with one password and starts nothing.
/// </summary>
public sealed partial class LoopbackGreetd : IDisposable
{
    private const int AfUnix = 1;
    private const int SockStream = 1;
    private const int SockCloexec = 0x80000;

    private readonly LoopbackGreetdOptions _options;
    private readonly Socket _socket;
    private readonly IEventSource _source;
    private readonly GreetdFrameWriter _writer = new();
    private readonly List<byte> _received = [];
    private readonly byte[] _chunk = new byte[4096];
    private string? _user;
    private int _step;

    private LoopbackGreetd(ICompositorEventLoop loop, Socket socket, LoopbackGreetdOptions options)
    {
        _options = options;
        _socket = socket;
        _socket.Blocking = false;
        _source = loop.AddFd((int)_socket.Handle, FdReadiness.Readable, OnReady);
    }

    /// <summary>
    /// Occurs when the client sends <c>start_session</c> after a correct password. The server answers success and runs nothing.
    /// </summary>
    public event Action<GreetdStartSession>? SessionStarted;

    /// <summary>
    /// Creates a socket pair and serves one end on <paramref name="loop"/>.
    /// </summary>
    /// <param name="loop">The loop that reads the server end.</param>
    /// <param name="options">The conversation, or <see langword="null"/> for the defaults.</param>
    /// <returns>The server and the client socket for <see cref="GreetdClient.Adopt"/>. The caller owns both.</returns>
    /// <exception cref="IOException">The socket pair cannot be made.</exception>
    public static (LoopbackGreetd Server, Socket Client) Create(ICompositorEventLoop loop, LoopbackGreetdOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(loop);
        Span<int> fds = stackalloc int[2];
        if (SocketPair(AfUnix, SockStream | SockCloexec, 0, ref MemoryMarshal.GetReference(fds)) != 0)
        {
            throw new IOException($"The socketpair call failed with errno {Marshal.GetLastPInvokeError()}.");
        }

        var server = new Socket(new SafeSocketHandle(fds[0], ownsHandle: true));
        var client = new Socket(new SafeSocketHandle(fds[1], ownsHandle: true));
        return (new LoopbackGreetd(loop, server, options ?? new LoopbackGreetdOptions()), client);
    }

    /// <summary>
    /// Removes the server from the loop and closes its end of the socket pair. It does not close the client socket.
    /// </summary>
    public void Dispose()
    {
        if (!_source.IsRemoved)
        {
            _source.Remove();
        }

        _socket.Dispose();
        _writer.Dispose();
    }

    [LibraryImport("libc", EntryPoint = "socketpair", SetLastError = true)]
    private static partial int SocketPair(int domain, int type, int protocol, ref int fds);

    private void OnReady(int fd, FdReadiness readiness)
    {
        while (true)
        {
            var read = _socket.Receive(_chunk, 0, _chunk.Length, SocketFlags.None, out var error);
            if (error == SocketError.WouldBlock)
            {
                break;
            }

            if (error != SocketError.Success || read == 0)
            {
                _source.Remove();
                return;
            }

            _received.AddRange(_chunk.AsSpan(0, read));
        }

        while (true)
        {
            var buffer = CollectionsMarshal.AsSpan(_received);
            if (GreetdFrame.TryRead(buffer, out var payload, out var consumed) != GreetdFrameStatus.Complete)
            {
                return;
            }

            var request = JsonSerializer.Deserialize(payload, GreetdJsonContext.Default.GreetdRequest);
            buffer[..consumed].Clear();
            _received.RemoveRange(0, consumed);
            Reply(Answer(request));
        }
    }

    private GreetdResponse Answer(GreetdRequest? request)
    {
        switch (request)
        {
            case GreetdCreateSession create when _user is null && _options.Info is { } info:
                _user = create.Username;
                _step = 1;
                return new GreetdAuthMessage(GreetdAuthMessageType.Info, info);
            case GreetdCreateSession create when _user is null:
                _user = create.Username;
                _step = 2;
                return new GreetdAuthMessage(GreetdAuthMessageType.Secret, _options.Prompt);
            case GreetdCreateSession:
                return new GreetdError(GreetdErrorType.Error, "a session is already being configured");
            case GreetdPostAuthMessageResponse when _step == 1:
                _step = 2;
                return new GreetdAuthMessage(GreetdAuthMessageType.Secret, _options.Prompt);
            case GreetdPostAuthMessageResponse answer when _step == 2:
                if (answer.Response == _options.Password)
                {
                    _step = 3;
                    return new GreetdSuccess();
                }

                return new GreetdError(GreetdErrorType.AuthError, "Authentication failure");
            case GreetdStartSession start when _step == 3:
                SessionStarted?.Invoke(start);
                return new GreetdSuccess();
            case GreetdCancelSession:
                _user = null;
                _step = 0;
                return new GreetdSuccess();
            default:
                return new GreetdError(GreetdErrorType.Error, "unexpected request");
        }
    }

    private void Reply(GreetdResponse response)
    {
        _writer.Write(response);
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
                break;
            }

            frame = frame[sent..];
        }

        _writer.Clear();
    }
}
