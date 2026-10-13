using Basin.Capabilities;

namespace Basin.Ipc;

internal sealed class IpcFrameStatsEvents : IpcEventSource
{
    private readonly IpcServer _server;
    private readonly IpcDescribe _describe;
    private readonly IFrameStats _stats;
    private ToplevelInfo[] _scratch = new ToplevelInfo[16];
    private IEventSource? _timer;

    public IpcFrameStatsEvents(IpcServer server, IpcDescribe describe, IFrameStats stats)
    {
        _server = server;
        _describe = describe;
        _stats = stats;
    }

    protected override void Attach()
    {
        _stats.Acquire();
        _timer = _server.Loop.AddTimer(OnTick);
        _timer.UpdateTimer(1000);
    }

    protected override void Detach()
    {
        _timer?.Remove();
        _timer = null;
        _stats.Release();
    }

    private void OnTick()
    {
        _timer?.UpdateTimer(1000);
        if (_describe.Toplevels is not { } model)
        {
            return;
        }

        int count;
        while ((count = model.Enumerate(_scratch)) < 0 || count > _scratch.Length)
        {
            _scratch = new ToplevelInfo[_scratch.Length * 2];
        }

        var now = MonotonicClock.Nanos;
        for (var i = 0; i < count; i++)
        {
            var info = _scratch[i];
            if (info.Surface is { } surface && _stats.TryGet(surface, now, out var snapshot) && snapshot.Presents > 0)
            {
                _server.Events.Emit(IpcEventNames.FrameStats, _describe.Stats(info, now), IpcJsonContext.Default.IpcFrameStats);
            }
        }

        Array.Clear(_scratch, 0, count);
    }
}
