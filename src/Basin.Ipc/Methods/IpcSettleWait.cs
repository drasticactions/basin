using Basin.Capabilities;

namespace Basin.Ipc;

internal sealed class IpcSettleWait : IDisposable
{
    private readonly IpcServer _server;
    private readonly IFrameSettle _settle;
    private readonly IpcPendingReply _reply;
    private IEventSource? _timer;
    private bool _done;

    public IpcSettleWait(IpcServer server, IFrameSettle settle, IpcPendingReply reply)
    {
        _server = server;
        _settle = settle;
        _reply = reply;
    }

    public void Start(int timeoutMs)
    {
        _settle.Settled += OnSettled;
        _timer = _server.Loop.AddTimer(OnTimeout);
        _timer.UpdateTimer(timeoutMs);
        _server.Own(this);
    }

    public void Dispose()
    {
        if (_done)
        {
            return;
        }

        _done = true;
        _settle.Settled -= OnSettled;
        _timer?.Remove();
        _timer = null;
        _server.Disown(this);
        if (!_reply.IsDone)
        {
            _reply.Error(IpcErrorCodes.Failed, "The compositor stopped before the outputs settled.");
            _ = _reply.Complete();
        }
    }

    private void OnSettled()
    {
        if (_done)
        {
            return;
        }

        _reply.Write(new IpcSettleResult(_settle.Presented), IpcJsonContext.Default.IpcSettleResult);
        _ = _reply.Complete();
        Dispose();
    }

    private void OnTimeout()
    {
        if (_done)
        {
            return;
        }

        _reply.Error(IpcErrorCodes.Failed, "The outputs did not settle before the timeout.");
        _ = _reply.Complete();
        Dispose();
    }
}
