using Basin.Capabilities;

namespace Basin.Ipc;

internal sealed class IpcInputSettle : IFrameSink, IToplevelCommitObserver, IDisposable
{
    private readonly IpcServer _server;
    private readonly IFrameClock? _clock;
    private readonly IToplevelModel? _model;
    private readonly ulong _window;
    private readonly int _quietMs;
    private readonly int _timeoutMs;
    private readonly Action<bool, long> _done;
    private readonly long _started = Environment.TickCount64;
    private IEventSource? _quiet;
    private IEventSource? _timeout;
    private bool _framed;
    private bool _finished;

    public IpcInputSettle(
        IpcServer server, IFrameClock? clock, IToplevelModel? model, ulong window, int quietMs, int timeoutMs, Action<bool, long> done)
    {
        _server = server;
        _clock = clock;
        _model = model;
        _window = window;
        _quietMs = quietMs;
        _timeoutMs = timeoutMs;
        _done = done;
    }

    public void Start()
    {
        _server.Own(this);
        _quiet = _server.Loop.AddTimer(() => Finish(settled: true));
        _timeout = _server.Loop.AddTimer(() => Finish(settled: false));
        _timeout.UpdateTimer(Math.Max(1, _timeoutMs));
        _model?.AddCommitObserver(this);
        if (_clock is { } clock)
        {
            clock.Add(this);
            clock.RequestFrame();
        }
        else
        {
            Framed();
        }
    }

    public void BeginFrame(IOutput output, long predictedVblankNanos)
    {
        if (!_framed && !_finished)
        {
            Framed();
        }
    }

    public void EndFrame(IOutput output, long presentedNanos)
    {
    }

    public void OnToplevelCommitted(ulong toplevelId, in Box damage)
    {
        if (_framed && !_finished && (_window == 0 || toplevelId == _window))
        {
            _quiet?.UpdateTimer(Math.Max(1, _quietMs));
        }
    }

    public void Dispose() => Finish(settled: false);

    private void Framed()
    {
        _framed = true;
        _quiet?.UpdateTimer(Math.Max(1, _quietMs));
    }

    private void Finish(bool settled)
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        _clock?.Remove(this);
        _model?.RemoveCommitObserver(this);
        _quiet?.Remove();
        _quiet = null;
        _timeout?.Remove();
        _timeout = null;
        _server.Disown(this);
        _done(settled, Environment.TickCount64 - _started);
    }
}
