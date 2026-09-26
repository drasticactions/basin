using static Basin.Ipc.IpcLog;

namespace Basin.Ipc;

internal sealed class IpcFdWriter : IDisposable
{
    private const int TimeoutMs = 30_000;

    private readonly IpcServer _server;
    private readonly ClientFd _fd;
    private readonly byte[] _bytes;
    private int _written;
    private IEventSource? _source;
    private IEventSource? _timer;
    private bool _done;

    public IpcFdWriter(IpcServer server, ClientFd fd, byte[] bytes)
    {
        _server = server;
        _fd = fd;
        _bytes = bytes;
    }

    public void Start()
    {
        if (!IpcNative.SetNonBlocking(_fd.Value))
        {
            Log.Debug($"could not make selection fd {_fd.Value} non-blocking (errno {UnixSocket.LastError})");
            Finish();
            return;
        }

        if (Pump())
        {
            return;
        }

        _source = _server.Loop.AddFd(_fd.Value, FdReadiness.Writable, (_, _) => Pump());
        _timer = _server.Loop.AddTimer(() =>
        {
            Log.Debug($"a selection reader stopped reading after {_written} bytes");
            Finish();
        });
        _timer.UpdateTimer(TimeoutMs);
        _server.Own(this);
    }

    public void Dispose() => Finish();

    private bool Pump()
    {
        while (!_done && _written < _bytes.Length)
        {
            var sent = IpcNative.Write(_fd.Value, _bytes.AsSpan(_written));
            if (sent < 0)
            {
                var error = UnixSocket.LastError;
                if (error == UnixSocket.EIntr)
                {
                    continue;
                }

                if (error == UnixSocket.EAgain)
                {
                    return false;
                }

                Finish();
                return true;
            }

            _written += (int)sent;
        }

        Finish();
        return true;
    }

    private void Finish()
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
        _fd.Close();
        _server.Disown(this);
    }
}
