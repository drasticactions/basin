using Basin.Capabilities;

namespace Basin.Ipc;

internal sealed class IpcWindowWait : IToplevelObserver, IDisposable
{
    private readonly IpcServer _server;
    private readonly IpcDescribe _describe;
    private readonly IToplevelModel _model;
    private readonly string? _appId;
    private readonly string? _title;
    private readonly IpcPendingReply _reply;
    private IEventSource? _timer;
    private bool _done;

    public IpcWindowWait(
        IpcServer server, IpcDescribe describe, IToplevelModel model, string? appId, string? title, IpcPendingReply reply)
    {
        _server = server;
        _describe = describe;
        _model = model;
        _appId = appId;
        _title = title;
        _reply = reply;
    }

    public static bool Matches(in ToplevelInfo info, string? appId, string? title) =>
        (appId is null || info.AppId == appId) &&
        (title is null || info.Title.Contains(title, StringComparison.Ordinal));

    public void Start(int timeoutMs)
    {
        _model.AddObserver(this);
        _timer = _server.Loop.AddTimer(OnTimeout);
        _timer.UpdateTimer(timeoutMs);
        _server.Own(this);
    }

    public void OnToplevelAdded(ulong toplevelId) => Check(toplevelId);

    public void OnToplevelChanged(ulong toplevelId) => Check(toplevelId);

    public void OnToplevelRemoved(ulong toplevelId)
    {
    }

    public void Dispose()
    {
        if (_done)
        {
            return;
        }

        _done = true;
        _model.RemoveObserver(this);
        _timer?.Remove();
        _timer = null;
        _server.Disown(this);
        if (!_reply.IsDone)
        {
            _reply.Error(IpcErrorCodes.Failed, "the compositor stopped before a window matched");
            _ = _reply.Complete();
        }
    }

    private void Check(ulong toplevelId)
    {
        if (_done || !_model.TryGet(toplevelId, out var info) || !Matches(info, _appId, _title))
        {
            return;
        }

        _reply.Write(_describe.Window(info, _describe.WorkspaceOf()), IpcJsonContext.Default.IpcWindow);
        _ = _reply.Complete();
        Dispose();
    }

    private void OnTimeout()
    {
        if (_done)
        {
            return;
        }

        _reply.Error(IpcErrorCodes.Failed, "no matching window mapped before the timeout");
        _ = _reply.Complete();
        Dispose();
    }
}
