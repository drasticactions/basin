using System.Text;
using Basin.Capabilities;

namespace Basin.Ipc;

internal sealed class IpcClipboardRead : IDisposable
{
    private readonly IpcServer _server;
    private readonly IpcPendingReply _reply;
    private readonly IpcClientState _state;
    private readonly string[] _types;
    private readonly string _mime;
    private readonly long _maxBytes;
    private readonly MemoryStream _data = new();
    private int _fd;
    private IEventSource? _source;
    private IEventSource? _timer;
    private bool _done;

    public IpcClipboardRead(
        IpcServer server, IpcPendingReply reply, int fd, string[] types, string mime, long maxBytes)
    {
        _server = server;
        _reply = reply;
        _state = reply.State;
        _fd = fd;
        _types = types;
        _mime = mime;
        _maxBytes = maxBytes;
    }

    public void Start(int timeoutMs)
    {
        _state.Set(this, this);
        _source = _server.Loop.AddFd(_fd, FdReadiness.Readable, OnReadable);
        _timer = _server.Loop.AddTimer(OnTimeout);
        _timer.UpdateTimer(timeoutMs);
    }

    public void Dispose()
    {
        if (_done)
        {
            return;
        }

        _done = true;
        _source?.Remove();
        _source = null;
        _timer?.Remove();
        _timer = null;
        if (_fd >= 0)
        {
            _ = UnixSocket.Close(_fd);
            _fd = -1;
        }

        _data.Dispose();
        if (!_reply.IsDone)
        {
            _reply.Error(IpcErrorCodes.Failed, "the connection closed before the selection arrived");
            _ = _reply.Complete();
        }

        _ = _state.Remove(this);
    }

    public static void WriteResult(IpcReply reply, string[] types, string? mime, string? text) =>
        reply.Write(new IpcClipboard(types, mime, text), IpcJsonContext.Default.IpcClipboard);

    private void OnReadable(int fd, FdReadiness readiness)
    {
        if (_done)
        {
            return;
        }

        Span<byte> chunk = stackalloc byte[16 * 1024];
        while (true)
        {
            var read = IpcNative.Read(_fd, chunk);
            if (read < 0)
            {
                var error = UnixSocket.LastError;
                if (error == UnixSocket.EIntr)
                {
                    continue;
                }

                if (error == UnixSocket.EAgain)
                {
                    return;
                }

                Finish(IpcErrorCodes.Failed, $"reading the selection failed (errno {error})");
                return;
            }

            if (read == 0)
            {
                WriteResult(_reply, _types, _mime, Encoding.UTF8.GetString(_data.GetBuffer(), 0, (int)_data.Length));
                Finish(null, null);
                return;
            }

            if (_data.Length + read > _maxBytes)
            {
                Finish(IpcErrorCodes.TooLarge, $"the selection is over {_maxBytes} bytes");
                return;
            }

            _data.Write(chunk[..(int)read]);
        }
    }

    private void OnTimeout()
    {
        if (!_done)
        {
            Finish(IpcErrorCodes.Failed, "the selection owner did not finish writing in time");
        }
    }

    private void Finish(string? code, string? message)
    {
        if (code is not null)
        {
            _reply.Error(code, message!);
        }

        _ = _reply.Complete();
        Dispose();
    }
}
