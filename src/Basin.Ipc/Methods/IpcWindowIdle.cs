using Basin.Capabilities;

namespace Basin.Ipc;

internal sealed class IpcWindowIdle : IToplevelCommitObserver, IToplevelObserver, IDisposable
{
    private readonly IpcServer _server;
    private readonly IToplevelModel _model;
    private readonly ulong _target;
    private readonly int _quietMs;
    private readonly int _ignoreBelow;
    private readonly int _timeoutMs;
    private readonly IpcPendingReply _reply;
    private readonly long _started = Environment.TickCount64;
    private IEventSource? _quiet;
    private IEventSource? _timeout;
    private long _lastCommit;
    private long _commits;
    private long _ignored;
    private bool _done;

    public IpcWindowIdle(
        IpcServer server, IToplevelModel model, ulong target, int quietMs, int ignoreBelow, int timeoutMs, IpcPendingReply reply)
    {
        _server = server;
        _model = model;
        _target = target;
        _quietMs = quietMs;
        _ignoreBelow = ignoreBelow;
        _timeoutMs = timeoutMs;
        _reply = reply;
    }

    public void Start()
    {
        _model.AddCommitObserver(this);
        _model.AddObserver(this);
        _quiet = _server.Loop.AddTimer(OnQuiet);
        _quiet.UpdateTimer(_quietMs);
        _timeout = _server.Loop.AddTimer(OnTimeout);
        _timeout.UpdateTimer(_timeoutMs);
        _server.Own(this);
    }

    public void OnToplevelCommitted(ulong toplevelId, in Box damage)
    {
        if (_done || (_target != 0 && toplevelId != _target))
        {
            return;
        }

        if (damage.Width <= _ignoreBelow && damage.Height <= _ignoreBelow)
        {
            _ignored++;
            return;
        }

        _commits++;
        _lastCommit = Environment.TickCount64;
        _quiet?.UpdateTimer(_quietMs);
    }

    public void OnToplevelAdded(ulong toplevelId)
    {
    }

    public void OnToplevelChanged(ulong toplevelId)
    {
    }

    public void OnToplevelRemoved(ulong toplevelId)
    {
        if (_done || _target == 0 || toplevelId != _target)
        {
            return;
        }

        _reply.Error(IpcErrorCodes.Failed, $"window {_target} went away before it was idle");
        Finish();
    }

    public void Dispose()
    {
        if (_done)
        {
            return;
        }

        _reply.Error(IpcErrorCodes.Failed, "the compositor stopped before the window was idle");
        Finish();
    }

    private void OnQuiet()
    {
        if (_done)
        {
            return;
        }

        _reply.Write(new IpcWaitIdleResult(Environment.TickCount64 - _started, _commits, _ignored), IpcJsonContext.Default.IpcWaitIdleResult);
        Finish();
    }

    private void OnTimeout()
    {
        if (_done)
        {
            return;
        }

        var what = _target == 0 ? "the windows" : $"window {_target}";
        _reply.Error(IpcErrorCodes.Failed, _commits == 0
            ? $"{what} did not go idle within {_timeoutMs} ms"
            : $"{what} did not go idle within {_timeoutMs} ms; the last commit was {Environment.TickCount64 - _lastCommit} ms ago");
        Finish();
    }

    private void Finish()
    {
        _done = true;
        _model.RemoveCommitObserver(this);
        _model.RemoveObserver(this);
        _quiet?.Remove();
        _quiet = null;
        _timeout?.Remove();
        _timeout = null;
        _server.Disown(this);
        _ = _reply.Complete();
    }
}
